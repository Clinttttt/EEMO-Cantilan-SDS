using EEMOCantilanSDS.Application.Command.Onboarding.ActivateMunicipality;
using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Onboarding;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Activating an LGU against a real, disposable PostgreSQL: what is actually written, that it is written atomically and only
/// for the target tenant, and that an office's declared revenue operations (initial intent in its onboarding draft) enable
/// nothing. Nothing here touches any database but the throwaway container.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed partial class OnboardingActivationTests(PostgresFixture db)
{
    private static readonly DateOnly Rated = new(2020, 1, 1);

    private sealed class Operator(Guid userId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "platform.operator";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => null;
    }

    private sealed class FakeEmail : IEmailSender
    {
        public int Sent;
        public Task<bool> SendAsync(string toEmail, string? toName, string subject, string body, CancellationToken ct = default)
        {
            Sent++;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public HashedPassword Hash(string password) => new("hashed");
        public PasswordCheck Check(string hash, string password) => PasswordCheck.Failed;
    }

    private sealed record World(Guid OperatorId, Guid ReferenceTenantId, Guid TargetId, Guid RequestId, string TargetName);

    /// <summary>A platform operator, an already-live reference tenant with its own facility and rate, and an LGU in Activation.</summary>
    private async Task<World> SeedAsync(string? draftConfigJson = null)
    {
        await db.ResetAsync();
        var reference = Municipality.Create("REFLGU", "Reference", "Province", MunicipalityStatus.Active, tenantCode: "ref-lgu");
        var target = Municipality.Create("MADRID", "Madrid", "Surigao del Sur", MunicipalityStatus.Upcoming, tenantCode: "madrid");
        var op = AdminUser.Create("Operator", "operator", "op@platform.ph", new HashedPassword("h"), AdminRole.SuperAdmin,
            reference.Id, isPlatformOperator: true, mustChangePassword: false);
        var request = AssessmentRequest.Create("Madrid", "Surigao del Sur", "Municipality of Madrid", "Focal", "Officer",
            "office@madrid.gov.ph", "09123456789", string.Empty, null, null, true, null);
        request.Approve("https://example.test/onboarding/t", null, "operator");
        request.SubmitForValidation("LGU");
        request.ApproveValidation("operator");

        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.AddRange(reference, target);
            setup.AdminUsers.Add(op);
            setup.AssessmentRequests.Add(request);
            setup.Facilities.Add(Facility.Create(FacilityCode.NPM, "Reference Market", "RM", archetype: BillingArchetype.DailyStall, municipalityId: reference.Id));
            setup.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, Rated, reference.Id, "seed"));
            if (draftConfigJson is not null)
            {
                var draft = OnboardingDraft.Create(request.Id, "Madrid", "Surigao del Sur", "tok-" + Guid.NewGuid().ToString("N"), DateTime.UtcNow.AddDays(7));
                draft.UpdateConfig(draftConfigJson, "LGU");
                setup.OnboardingDrafts.Add(draft);
            }
            await setup.SaveChangesAsync();
        }
        return new World(op.Id, reference.Id, target.Id, request.Id, "Madrid");
    }

    private Task<Result<ActivationResultDto>> ActivateAsync(World w, ActivateMunicipalityCommand command, FakeEmail? email = null)
    {
        var context = db.CreateContext(Guid.Empty);
        var handler = new ActivateMunicipalityCommandHandler(context, new Operator(w.OperatorId), email ?? new FakeEmail(), new FakeHasher());
        return handler.Handle(command, CancellationToken.None);
    }

    private static ActivateMunicipalityCommand Command(World w, IReadOnlyList<ActivationFacility> facilities, IReadOnlyList<ActivationRate> rates,
        IReadOnlyList<ActivationOperation>? operations = null) =>
        new("MADRID",
            new ActivationBranding("Madrid Economic Enterprise Office", "Madrid, Surigao del Sur, Philippines", null, "MEEO"),
            new ActivationAdministrator("Ana Cruz", "madrid.head", "ana@madrid.gov.ph"),
            facilities, rates, AssessmentRequestId: w.RequestId, OperationDeclarations: operations);

    private static ActivationFacility Fac(FacilityCode code, string name, BillingArchetype archetype) =>
        new(code, name, name[..Math.Min(3, name.Length)].ToUpperInvariant(), archetype);

    [SkippableFact]
    public async Task OneSimpleFacility_CreatesOnlyThatFacilityItsRate_AndNothingOperational()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();

        var result = await ActivateAsync(w, Command(w,
            [Fac(FacilityCode.NPM, "Madrid Pamilihan", BillingArchetype.DailyStall)],
            [new ActivationRate(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m)]));

        Assert.True(result.IsSuccess, result.Error);
        // The API states the activation URL; a console never rebuilds a host from the token. It is the environment's own base.
        Assert.Equal(EEMOCantilanSDS.Application.Common.Onboarding.ActivationLinks.Build(result.Value!.ActivationToken),
            result.Value.ActivationUrl);
        await using var verify = db.CreateContext(Guid.Empty);
        var facility = await verify.Facilities.IgnoreQueryFilters().SingleAsync(f => f.MunicipalityId == w.TargetId);
        Assert.Equal(("Madrid Pamilihan", FacilityCode.NPM), (facility.Name, facility.Code));
        var rate = await verify.FacilityRates.IgnoreQueryFilters().SingleAsync(r => r.MunicipalityId == w.TargetId);
        Assert.Equal((FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m), (rate.FacilityCode, rate.RateKey, rate.Amount));

        // Stalls, occupants/payors and collections are created later in the portal, never at activation.
        Assert.Equal(0, await verify.Stalls.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await verify.Payors.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await verify.Collections.IgnoreQueryFilters().CountAsync());

        var madrid = await verify.Municipalities.SingleAsync(m => m.Id == w.TargetId);
        Assert.Equal(MunicipalityStatus.Active, madrid.Status);
        var head = await verify.AdminUsers.IgnoreQueryFilters().SingleAsync(u => u.MunicipalityId == w.TargetId);
        Assert.False(head.IsActive); // provisioned inactive until the Head sets a password through the one-time link
        Assert.Equal(AdminRole.SuperAdmin, head.Role);
    }

    [SkippableFact]
    public async Task SeveralFacilities_KeepTheirOwnNamesAndIdentities_AndCustomOnesTakeReservedSlotsWithNoRates()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();

        var result = await ActivateAsync(w, Command(w,
            [
                Fac(FacilityCode.NPM, "Madrid Pamilihan", BillingArchetype.DailyStall),
                Fac(FacilityCode.TCC, "Plaza Tindahan", BillingArchetype.MonthlyRental),
                Fac(FacilityCode.Custom1, "Night Market", BillingArchetype.MonthlyRental)
                    with { CustomCategory = CustomFacilityCategory.SpaceRental },
                Fac(FacilityCode.Custom2, "Fiesta Hall", BillingArchetype.MonthlyRental),
                Fac(FacilityCode.TRM, "Pantalan Terminal", BillingArchetype.PerTrip),
            ],
            [
                new ActivationRate(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m),
                new ActivationRate(FacilityCode.TRM, FeeRateKey.TrmPerTrip, 25m),
            ]));

        Assert.True(result.IsSuccess, result.Error);
        await using var verify = db.CreateContext(Guid.Empty);
        var facilities = await verify.Facilities.IgnoreQueryFilters().Where(f => f.MunicipalityId == w.TargetId).ToListAsync();
        Assert.Equal(
            new[] { "Fiesta Hall", "Madrid Pamilihan", "Night Market", "Pantalan Terminal", "Plaza Tindahan" },
            facilities.Select(f => f.Name).Order().ToArray());
        Assert.All(facilities.Where(f => f.Code is FacilityCode.Custom1 or FacilityCode.Custom2),
            f => Assert.Equal(BillingArchetype.MonthlyRental, f.Archetype));
        Assert.Equal(CustomFacilityCategory.SpaceRental,
            facilities.Single(f => f.Code == FacilityCode.Custom1).CustomCategory);
        Assert.Equal(CustomFacilityCategory.StallRent,
            facilities.Single(f => f.Code == FacilityCode.Custom2).CustomCategory);
        Assert.Equal(CustomFacilityCategory.StallRent,
            facilities.Single(f => f.Code == FacilityCode.TCC).CustomCategory);

        var rates = await verify.FacilityRates.IgnoreQueryFilters().Where(r => r.MunicipalityId == w.TargetId).ToListAsync();
        Assert.Equal(2, rates.Count); // exactly the two stated; none for a monthly rental, none zero-filled
        Assert.DoesNotContain(rates, r => r.Amount == 0m);
        Assert.DoesNotContain(rates, r => r.FacilityCode is FacilityCode.TCC or FacilityCode.Custom1 or FacilityCode.Custom2);
    }

    [SkippableFact]
    public async Task DeclaredOperationsInTheDraft_EnableNothingAtActivation_AndTheDraftIsKeptAsSaved()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        const string draftJson = """
            {"facilities":[],"operations":{"version":1,"items":[
              {"operationCode":"FIESTA_ARAW_LOT_RENTAL","selected":true,"localDisplayName":"Pista Lot Rental"},
              {"operationCode":"ECF","selected":true},{"operationCode":"WCF","selected":true},
              {"operationCode":"FISH_MEAT_VENDOR_FEE","selected":true}]}}
            """;
        var w = await SeedAsync(draftJson);

        var result = await ActivateAsync(w, Command(w,
            [Fac(FacilityCode.NPM, "Madrid Pamilihan", BillingArchetype.DailyStall)],
            [new ActivationRate(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m)]));

        Assert.True(result.IsSuccess, result.Error);
        await using var verify = db.CreateContext(Guid.Empty);
        // No Collector assignment, governed-service switch, obligation account or utility bill exists because an operation was declared.
        Assert.Equal(0, await verify.CollectorOperationAssignments.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await verify.GovernedServices.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await verify.GovernedServiceSettings.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await verify.ObligationAccounts.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await verify.UtilityBills.IgnoreQueryFilters().CountAsync());

        // The office's declaration is still there, byte for byte, for the Head's setup.
        var draft = await verify.OnboardingDrafts.SingleAsync(d => d.AssessmentRequestId == w.RequestId);
        // (Stored as jsonb, which normalises whitespace and key order, so the declaration is compared as data.)
        using var saved = System.Text.Json.JsonDocument.Parse(draft.ConfigJson!);
        var items = saved.RootElement.GetProperty("operations").GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("operationCode").GetString(),
                i.TryGetProperty("localDisplayName", out var n) ? n.GetString() : null))
            .ToArray();
        Assert.Equal(
            new (string?, string?)[] { ("FIESTA_ARAW_LOT_RENTAL", "Pista Lot Rental"), ("ECF", null), ("WCF", null), ("FISH_MEAT_VENDOR_FEE", null) },
            items);
    }

    [SkippableFact]
    public async Task Activation_LeavesAnotherTenantsFacilitiesAndRatesUntouched_AndInvisibleToIt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();

        var result = await ActivateAsync(w, Command(w,
            [Fac(FacilityCode.NPM, "Madrid Pamilihan", BillingArchetype.DailyStall)],
            [new ActivationRate(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 99m)]));

        Assert.True(result.IsSuccess, result.Error);
        // Read AS the reference tenant: the query filter is the real isolation, so this is what that office would see.
        await using var asReference = db.CreateContext(w.ReferenceTenantId);
        var seen = await asReference.Facilities.ToListAsync();
        Assert.Equal("Reference Market", Assert.Single(seen).Name);
        var rate = await asReference.FacilityRates.SingleAsync();
        Assert.Equal(30m, rate.Amount);
    }

    [SkippableFact]
    public async Task AnAlreadyActiveMunicipality_CannotBeActivatedAgain()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();
        var command = Command(w,
            [Fac(FacilityCode.NPM, "Madrid Pamilihan", BillingArchetype.DailyStall)],
            [new ActivationRate(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m)]);

        Assert.True((await ActivateAsync(w, command)).IsSuccess);
        var again = await ActivateAsync(w, command);

        Assert.False(again.IsSuccess);
        await using var verify = db.CreateContext(Guid.Empty);
        Assert.Equal(1, await verify.Facilities.IgnoreQueryFilters().CountAsync(f => f.MunicipalityId == w.TargetId));
        Assert.Equal(1, await verify.AdminUsers.IgnoreQueryFilters().CountAsync(u => u.MunicipalityId == w.TargetId));
    }


    [SkippableFact]
    public async Task DeclaredOperations_BecomeTheTenantsConfiguration_SelectedListedAndUnselectedExplicitlyNot()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();

        var result = await ActivateAsync(w, Command(w,
            [Fac(FacilityCode.NPM, "Madrid Pamilihan", BillingArchetype.DailyStall)],
            [new ActivationRate(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m)],
            [
                new ActivationOperation("MARKET_FEES", true),
                new ActivationOperation("KANMANGGAY_SPACE_RENTAL", true, "Municipal Plaza Rental"),
                new ActivationOperation("LANDING_BERTHING", false, "Ignored because it is not selected"),
            ]));

        Assert.True(result.IsSuccess, result.Error);
        await using var verify = db.CreateContext(w.TargetId);
        var rows = await verify.TenantOperationSettings.ToListAsync();
        Assert.Equal(TenantOperationCatalog.All.Count, rows.Count); // every operation has an explicit row
        Assert.All(rows, r => Assert.Equal("Onboarding", r.Origin));
        Assert.Equal(new[] { "KANMANGGAY_SPACE_RENTAL", "MARKET_FEES" }, rows.Where(r => r.IsListed).Select(r => r.OperationCode).Order().ToArray());
        Assert.Equal("Municipal Plaza Rental", rows.Single(r => r.OperationCode == "KANMANGGAY_SPACE_RENTAL").LocalDisplayName);
        Assert.Equal(["KANMANGGAY_SPACE_RENTAL", "MARKET_FEES"], await ClassificationsOfAsync(w.TargetId));
        Assert.Empty(await verify.RevenueClassificationPolicies.ToListAsync());
        Assert.False(rows.Single(r => r.OperationCode == "LANDING_BERTHING").IsListed);
        Assert.Null(rows.Single(r => r.OperationCode == "LANDING_BERTHING").LocalDisplayName); // an unselected operation keeps no name

        // The read model the Head Portal uses: Landing / Berthing is not listed, the selected ones carry the LGU's wording.
        var dto = await EEMOCantilanSDS.Application.Common.Onboarding.TenantOperationSettingsStore.ReadAsync(verify, w.TargetId, CancellationToken.None);
        Assert.True(dto.UsesConfiguration);
        Assert.DoesNotContain(dto.Items.Where(i => i.IsListed), i => i.OperationCode == "LANDING_BERTHING");
        Assert.Equal("Municipal Plaza Rental", dto.Items.Single(i => i.OperationCode == "KANMANGGAY_SPACE_RENTAL").DisplayName);

        // Presentation only: nothing was enabled, assigned, filed or registered by declaring operations.
        await using var all = db.CreateContext(Guid.Empty);
        Assert.Equal(0, await all.CollectorOperationAssignments.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await all.GovernedServices.IgnoreQueryFilters().CountAsync());
        Assert.Equal(0, await all.ObligationAccounts.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await all.FacilityRates.IgnoreQueryFilters().CountAsync(r => r.MunicipalityId == w.TargetId));
        // The reference tenant has no configuration written to it.
        Assert.Equal(0, await all.TenantOperationSettings.IgnoreQueryFilters().CountAsync(s => s.MunicipalityId == w.ReferenceTenantId));
    }

    [SkippableFact]
    public async Task NoDeclarationsAtAll_AnOlderOnboarding_LeavesTheMunicipalityOnTheLegacyFallback()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();

        var result = await ActivateAsync(w, Command(w,
            [Fac(FacilityCode.NPM, "Madrid Pamilihan", BillingArchetype.DailyStall)],
            [new ActivationRate(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m)]));

        Assert.True(result.IsSuccess, result.Error);
        await using var verify = db.CreateContext(w.TargetId);
        Assert.Equal(0, await verify.TenantOperationSettings.CountAsync());
        var dto = await EEMOCantilanSDS.Application.Common.Onboarding.TenantOperationSettingsStore.ReadAsync(verify, w.TargetId, CancellationToken.None);
        Assert.False(dto.UsesConfiguration);
        Assert.All(dto.Items, i => Assert.True(i.IsListed)); // never read as "nothing selected"
    }

    [SkippableFact]
    public async Task AnExplicitEmptyDeclaration_MeansNothingSelected_NotNeverAsked()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();

        var result = await ActivateAsync(w, Command(w,
            [Fac(FacilityCode.NPM, "Madrid Pamilihan", BillingArchetype.DailyStall)],
            [new ActivationRate(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m)],
            []));

        Assert.True(result.IsSuccess, result.Error);
        await using var verify = db.CreateContext(w.TargetId);
        var dto = await EEMOCantilanSDS.Application.Common.Onboarding.TenantOperationSettingsStore.ReadAsync(verify, w.TargetId, CancellationToken.None);
        Assert.True(dto.UsesConfiguration);
        Assert.All(dto.Items, i => Assert.False(i.IsListed));
    }

    [SkippableFact]
    public async Task AFailedActivation_RollsBackEverything_AndLeavesTheMunicipalityUpcoming()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();
        var email = new FakeEmail();

        // Two facilities under one code reach the database's unique index (the validator is bypassed on purpose, to prove the
        // handler's own all-or-nothing guarantee rather than the front door's).
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => ActivateAsync(w, Command(w,
            [
                Fac(FacilityCode.NPM, "Madrid Pamilihan", BillingArchetype.DailyStall),
                Fac(FacilityCode.NPM, "Second Market", BillingArchetype.DailyStall),
            ],
            [new ActivationRate(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m)],
            [new ActivationOperation("MARKET_FEES", true, "Pamilihan Fees")]), email));

        await using var verify = db.CreateContext(Guid.Empty);
        var madrid = await verify.Municipalities.SingleAsync(m => m.Id == w.TargetId);
        Assert.Equal(MunicipalityStatus.Upcoming, madrid.Status);
        Assert.Equal(0, await verify.Facilities.IgnoreQueryFilters().CountAsync(f => f.MunicipalityId == w.TargetId));
        Assert.Equal(0, await verify.FacilityRates.IgnoreQueryFilters().CountAsync(r => r.MunicipalityId == w.TargetId));
        Assert.Equal(0, await verify.AdminUsers.IgnoreQueryFilters().CountAsync(u => u.MunicipalityId == w.TargetId));
        // The operation configuration is part of the same transaction: none of it survives a failed activation.
        Assert.Equal(0, await verify.TenantOperationSettings.IgnoreQueryFilters().CountAsync(s => s.MunicipalityId == w.TargetId));
        var request = await verify.AssessmentRequests.SingleAsync(r => r.Id == w.RequestId);
        Assert.Equal("Activation", request.Stage); // still retryable
        Assert.Equal(0, email.Sent); // nobody was told a portal is live
    }

    // ---- ECF / WCF setup for a newly activated office --------------------------------------------------------------------

    private sealed class HeadActor(Guid tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "madrid.head";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "madrid";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : EEMOCantilanSDS.Application.Common.Tenancy.ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static readonly ActivationFacility[] OneMarket = [Fac(FacilityCode.NPM, "Madrid Pamilihan", BillingArchetype.DailyStall)];
    private static readonly ActivationRate[] OneRate = [new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m)];

    private async Task<string[]> ClassificationsOfAsync(Guid tenant)
    {
        await using var verify = db.CreateContext(Guid.Empty);
        return await verify.RevenueClassifications.IgnoreQueryFilters().Where(c => c.MunicipalityId == tenant)
            .OrderBy(c => c.SemanticCode).Select(c => c.SemanticCode).ToArrayAsync();
    }

    private async Task<EEMOCantilanSDS.Application.Common.Result<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Revenue.EcfObligationQuoteDto>>> EcfObligationsAsync(Guid tenant)
    {
        await using var context = db.CreateContext(tenant);
        return await new EEMOCantilanSDS.Application.Common.Revenue.EcfCollectionWorkflow(context, new HeadActor(tenant), new FixedTenant(tenant))
            .GetObligationsAsync(PhilippineTime.Today.Year, PhilippineTime.Today.Month);
    }

    [SkippableFact]
    public async Task SelectedEcfAndWcf_GetADormantClassification_NoPolicy_NoInstrument_AndNothingIsAuthorized()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();
        var result = await ActivateAsync(w, Command(w, OneMarket, OneRate,
            [new ActivationOperation("ECF", true), new ActivationOperation("WCF", true), new ActivationOperation("LANDING_BERTHING", true)]));
        Assert.True(result.IsSuccess, result.Error);

        // All selected operations have a dormant source identity, but none has receipt/posting authority.
        Assert.Equal(["ECF", "LANDING_BERTHING", "WCF"], await ClassificationsOfAsync(w.TargetId));
        await using (var verify = db.CreateContext(Guid.Empty))
        {
            Assert.Equal(0, await verify.RevenueClassificationPolicies.IgnoreQueryFilters().CountAsync(p => p.MunicipalityId == w.TargetId));
            Assert.Equal(0, await verify.UtilityBills.IgnoreQueryFilters().CountAsync(b => b.MunicipalityId == w.TargetId));
            Assert.Equal(0, await verify.Collections.IgnoreQueryFilters().CountAsync(c => c.MunicipalityId == w.TargetId));
        }

        // The classification alone authorizes nothing: the utility still refuses, and says what is missing.
        var refused = await EcfObligationsAsync(w.TargetId);
        Assert.False(refused.IsSuccess);
        Assert.Equal(EEMOCantilanSDS.Application.Common.ResultStatus.Conflict, refused.Status);
        Assert.Contains("does not approve Official Receipt", refused.Error);
    }

    [SkippableFact]
    public async Task TheHeadFinishesEcfSetupInRevenueSetup_ThenTheUtilityWorks_AndStillInventsNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();
        Assert.True((await ActivateAsync(w, Command(w, OneMarket, OneRate, [new ActivationOperation("ECF", true)]))).IsSuccess);
        Guid classificationId;
        await using (var read = db.CreateContext(Guid.Empty))
            classificationId = (await read.RevenueClassifications.IgnoreQueryFilters().SingleAsync(c => c.MunicipalityId == w.TargetId)).Id;

        await using (var context = db.CreateContext(w.TargetId))
        {
            var appended = await new EEMOCantilanSDS.Application.Command.Revenue.AppendRevenueClassificationPolicy.AppendRevenueClassificationPolicyCommandHandler(context, new HeadActor(w.TargetId))
                .Handle(new(classificationId, PhilippineTime.Today, "Electricity Consumption Fees", null, RevenueInstrumentType.OfficialReceipt), CancellationToken.None);
            Assert.True(appended.IsSuccess, appended.Error);
        }

        var obligations = await EcfObligationsAsync(w.TargetId);
        Assert.True(obligations.IsSuccess, obligations.Error);
        Assert.Empty(obligations.Value!); // no source is registered, so no obligation exists: nothing is invented
    }

    [SkippableFact]
    public async Task UnselectedOrUndeclaredUtilities_GetNoClassification_AndExistingOfficesAreUntouched()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var w = await SeedAsync();
        Assert.True((await ActivateAsync(w, Command(w, OneMarket, OneRate, [new ActivationOperation("ECF", false), new ActivationOperation("WCF", true)]))).IsSuccess);
        Assert.Equal(["WCF"], await ClassificationsOfAsync(w.TargetId));
        var unselected = await EcfObligationsAsync(w.TargetId);
        Assert.False(unselected.IsSuccess);
        Assert.Contains("no active revenue classification", unselected.Error);
        Assert.Empty(await ClassificationsOfAsync(w.ReferenceTenantId)); // another office being activated gives an existing office nothing

        var older = await SeedAsync();
        Assert.True((await ActivateAsync(older, Command(older, OneMarket, OneRate, operations: null))).IsSuccess); // an older onboarding that declared nothing
        Assert.Empty(await ClassificationsOfAsync(older.TargetId));
    }
}
