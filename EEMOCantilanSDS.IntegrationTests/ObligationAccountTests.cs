using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The specialized obligation accounts (IA-050): Fish/Meat Vendor Fee, Kanmanggay and event lot rental are monthly (or
/// event) obligations settled by installments on an Official Receipt, each under its own classification, with the
/// balance read only from canonical allocations.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ObligationAccountTests(PostgresFixture db)
{
    // The former Vendor Fee assessment model is historical from IA-064. Kanmanggay/Fiesta
    // retain this engine; their cross-source compatibility is tested at this pre-cutover date.
    private sealed class HistoricalClock : IClock
    {
        public DateOnly PhilippineToday => FishMeatVendorFeeRules.DirectEffectiveDate.AddDays(-1);
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineToday.ToDateTime(TimeOnly.MinValue);
    }
    private sealed record Seed(Guid TenantId, Guid UserId, Guid StallId, Guid PayorId, Guid UnlinkedStallId,
        Guid[] OrIds, DateOnly Today);

    private sealed class TestActor(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "obligation-test";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "obligation-test";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static ObligationWorkflow Setup(AppDbContext context, Seed seed, string role = "SuperAdmin") =>
        new(context, new TestActor(seed.UserId, seed.TenantId, role), new FixedTenant(seed.TenantId), new HistoricalClock());

    [SkippableFact]
    public async Task SpaceImport_ValidatesAllRowsBeforeWriting_SkipsExisting_AndKeepsClosedHistory()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var ctx = db.CreateContext(seed.TenantId);
        var workflow = Setup(ctx, seed);
        var start = new DateOnly(seed.Today.Year, seed.Today.Month, 1).AddMonths(-1);
        var valid = new ImportSpaceHolderRow(new(ObligationKind.KanmanggaySpaceRental, seed.PayorId, null,
            "K-1", null, null, start, 100m));
        var invalid = valid with { Account = valid.Account with { PayorId = Guid.NewGuid(), SubjectLabel = "K-2" } };
        var refused = await workflow.ImportSpaceHoldersAsync(new([valid, invalid]));
        Assert.True(refused.IsSuccess);
        Assert.Single(refused.Value!.NeedsReview);
        Assert.Equal(0, await ctx.ObligationAccounts.CountAsync());
        var imported = await workflow.ImportSpaceHoldersAsync(new([valid, valid with
            { Account = valid.Account with { SubjectLabel = "K-2" }, ClosedOn = start }]));
        Assert.Equal(2, imported.Value!.Imported);
        var replay = await workflow.ImportSpaceHoldersAsync(new([valid]));
        Assert.Equal((0, 1), (replay.Value!.Imported, replay.Value.Skipped));
        var report = (await workflow.GetStatusReportAsync(ObligationKind.KanmanggaySpaceRental, start.Year)).Value!;
        Assert.Contains(report.Accounts, a => a.SubjectLabel == "K-2" && a.ActiveTo == start);
        Assert.Equal(100m, report.Accounts.Single(a => a.SubjectLabel == "K-2").AssessedToDate);
        Assert.Equal(0, await ctx.Collections.CountAsync());
        var admin = await Setup(ctx, seed, "Admin").ImportSpaceHoldersAsync(new([valid]));
        Assert.Equal(ResultStatus.Forbidden, admin.Status);
    }

    private static CollectionComposerWorkflow Composer(AppDbContext context, Seed seed) =>
        new(context, new TestActor(seed.UserId, seed.TenantId, "Admin"), new FixedTenant(seed.TenantId), new HistoricalClock());

    private static async Task<long> PostAsync(CollectionComposerWorkflow composer, EcfCollectionDraftDto draft, Guid orId)
    {
        var selected = await composer.SelectDocumentAsync(draft.DraftId, new SelectEcfDraftDocumentRequest(draft.Revision, orId));
        Assert.True(selected.IsSuccess, selected.Error);
        var reviewed = await composer.ReviewAsync(draft.DraftId, new EcfDraftRevisionRequest(selected.Value!.Revision));
        Assert.True(reviewed.IsSuccess, reviewed.Error);
        var posted = await composer.PostAsync(draft.DraftId,
            new PostEcfCollectionDraftRequest(reviewed.Value!.Revision, Guid.NewGuid()));
        Assert.True(posted.IsSuccess, posted.Error);
        return reviewed.Value.Revision;
    }

    private static Task<Result<ObligationAccountDto>> OpenVendorFee(ObligationWorkflow setup, Seed seed, decimal amount = 900m) =>
        setup.CreateAccountAsync(new CreateObligationAccountRequest(ObligationKind.FishMeatVendorFee, Guid.Empty, seed.StallId,
            "Fish stall 01", null, null, new DateOnly(seed.Today.Year, seed.Today.Month, 1), amount));

    [SkippableFact]
    public async Task VendorFeeIsAMonthlyGoal_SettledByThirtyPesoInstallments_OnItsOwnClassification()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var setup = Setup(context, seed);

        var opened = await OpenVendorFee(setup, seed);
        Assert.True(opened.IsSuccess, opened.Error);
        // The Payor came from the NPM stall's linked occupancy, never from typed text.
        Assert.Equal(seed.PayorId, opened.Value!.PayorId);
        Assert.Equal(900m, opened.Value.CurrentAmount);

        var composer = Composer(context, seed);
        var first = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(opened.Value.Id, seed.Today.Year, seed.Today.Month, 30m, null));
        Assert.True(first.IsSuccess, first.Error);
        await PostAsync(composer, first.Value!, seed.OrIds[0]);

        var second = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(opened.Value.Id, seed.Today.Year, seed.Today.Month, 30m, null));
        Assert.True(second.IsSuccess, second.Error);
        await PostAsync(composer, second.Value!, seed.OrIds[1]);

        var register = await setup.GetRegisterAsync(opened.Value.Id);
        var month = Assert.Single(register.Value!);
        Assert.Equal((900m, 60m, 840m), (month.AssessedAmount, month.SettledAmount, month.OutstandingAmount));

        var lines = await context.CollectionLines.Include(x => x.Allocations).ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.All(l.Allocations, a => Assert.Equal(CollectionSourceKind.ObligationPeriod, a.SourceKind)));
        var classification = await context.RevenueClassifications.SingleAsync(x => x.SemanticCode == RevenueClassificationCodes.FishMeatVendorFee);
        Assert.All(lines, l => Assert.Equal(classification.Id, l.RevenueClassificationId));
        // Nothing in the legacy NPM daily-collection history was created or touched.
        Assert.Equal(0, await context.DailyCollections.CountAsync());
    }

    [SkippableFact]
    public async Task AnAllocationAboveTheRemainingBalance_IsRefused_AndSoIsACombinedOverAllocationInOneDraft()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var setup = Setup(context, seed);
        var account = (await OpenVendorFee(setup, seed, 100m)).Value!;
        var composer = Composer(context, seed);

        var over = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(account.Id, seed.Today.Year, seed.Today.Month, 100.01m, null));
        Assert.False(over.IsSuccess);

        var ok = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(account.Id, seed.Today.Year, seed.Today.Month, 70m, null));
        Assert.True(ok.IsSuccess, ok.Error);
        var combined = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(account.Id, seed.Today.Year, seed.Today.Month, 40m, ok.Value!.Revision));
        Assert.False(combined.IsSuccess);
        Assert.Contains("exceeds", combined.Error, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task KanmanggayAndVendorFee_ForOnePayor_ShareOneOrAsSeparateClassifiedLines()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var setup = Setup(context, seed);
        var vendor = (await OpenVendorFee(setup, seed)).Value!;
        var kanmanggay = await setup.CreateAccountAsync(new CreateObligationAccountRequest(ObligationKind.KanmanggaySpaceRental,
            seed.PayorId, null, "Space K-4", null, null, new DateOnly(seed.Today.Year, seed.Today.Month, 1), 1200m));
        Assert.True(kanmanggay.IsSuccess, kanmanggay.Error);

        var composer = Composer(context, seed);
        var draft = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(vendor.Id, seed.Today.Year, seed.Today.Month, 30m, null));
        var both = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(kanmanggay.Value!.Id, seed.Today.Year, seed.Today.Month, 400m, draft.Value!.Revision));
        Assert.True(both.IsSuccess, both.Error);
        Assert.Equal(2, both.Value!.Lines.Count);
        Assert.Equal(430m, both.Value.TotalAmount);
        await PostAsync(composer, both.Value, seed.OrIds[0]);

        var collection = await context.Collections.Include(x => x.Lines).SingleAsync();
        Assert.Equal(2, collection.Lines.Count);
        Assert.Equal(2, collection.Lines.Select(l => l.RevenueClassificationId).Distinct().Count());
        Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", collection.ReferenceCode);   // one collection, one SRC, two classified lines
    }

    [SkippableFact]
    public async Task OnlyTheHeadOpensAccounts_AVendorFeeNeedsALinkedPayor_AndARateNeverReachesBackInTime()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);

        var admin = await OpenVendorFee(Setup(context, seed, "Admin"), seed);
        Assert.False(admin.IsSuccess);

        var head = Setup(context, seed);
        var unlinked = await head.CreateAccountAsync(new CreateObligationAccountRequest(ObligationKind.FishMeatVendorFee, seed.PayorId,
            seed.UnlinkedStallId, "Fish stall 02", null, null, new DateOnly(seed.Today.Year, seed.Today.Month, 1), 900m));
        Assert.False(unlinked.IsSuccess);
        Assert.Contains("not linked", unlinked.Error, StringComparison.OrdinalIgnoreCase);

        var account = (await OpenVendorFee(head, seed)).Value!;
        var duplicate = await OpenVendorFee(head, seed);
        Assert.False(duplicate.IsSuccess);

        var composer = Composer(context, seed);
        var draft = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(account.Id, seed.Today.Year, seed.Today.Month, 30m, null));
        await PostAsync(composer, draft.Value!, seed.OrIds[0]);

        var retroactive = await head.SetRateAsync(account.Id,
            new SetObligationRateRequest(new DateOnly(seed.Today.Year, seed.Today.Month, 1), 1000m));
        Assert.False(retroactive.IsSuccess);
        var next = new DateOnly(seed.Today.Year, seed.Today.Month, 1).AddMonths(1);
        Assert.True((await head.SetRateAsync(account.Id, new SetObligationRateRequest(next, 1000m))).IsSuccess);
        var month = Assert.Single((await head.GetRegisterAsync(account.Id)).Value!);
        Assert.Equal(900m, month.AssessedAmount);   // the assessed month is history and is never re-priced
    }

    [SkippableFact]
    public async Task ALotRental_HasOneEventPeriod_AndTheEventDateIsNotAMonthlyBillingDate()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var head = Setup(context, seed);

        var eventDate = seed.Today.AddDays(-3);
        var lot = await head.CreateAccountAsync(new CreateObligationAccountRequest(ObligationKind.FiestaArawLotRental,
            seed.PayorId, null, "Lot F-12", LotRentalEvent.Fiesta, eventDate, eventDate, 2500m));
        Assert.True(lot.IsSuccess, lot.Error);

        var register = Assert.Single((await head.GetRegisterAsync(lot.Value!.Id)).Value!);
        Assert.Equal(eventDate, register.PeriodStart);
        Assert.Equal(2500m, register.OutstandingAmount);
        Assert.False((await head.SetRateAsync(lot.Value.Id, new SetObligationRateRequest(seed.Today.AddDays(1), 3000m))).IsSuccess);

        var composer = Composer(context, seed);
        var draft = await composer.AddObligationAllocationAsync(
            new AddObligationDraftAllocationRequest(lot.Value.Id, eventDate.Year, eventDate.Month, 2500m, null));
        Assert.True(draft.IsSuccess, draft.Error);
        await PostAsync(composer, draft.Value!, seed.OrIds[0]);
        Assert.Equal(0m, Assert.Single((await head.GetRegisterAsync(lot.Value.Id)).Value!).OutstandingAmount);
    }

    [SkippableFact]
    public async Task IcePlantRent_CollectsUnderItsOwnClassification_NotPermanentStallRent()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync();
        var period = new DateOnly(seed.Today.Year, seed.Today.Month, 1);
        Guid iceStallId;
        await using (var setup = db.CreateContext(seed.TenantId))
        {
            var facility = Facility.Create(FacilityCode.ICE, "Ice Plant", "ICE", municipalityId: seed.TenantId);
            var stall = Stall.Create(facility.Id, "ICE-01", 1000m, ApplicableFees.BaseRental, municipalityId: seed.TenantId);
            var contract = Contract.Create(stall.Id, "Pedro Vendor", "Pedro Vendor", period, 5, 1000m, createdBy: "test");
            contract.AssociatePayor(seed.PayorId, "test");
            var ice = RevenueClassification.Create(RevenueClassificationCodes.IcePlant, seed.TenantId);
            var iceOr = RevenueClassificationPolicy.Create(ice.Id, new DateOnly(2000, 1, 1), "Ice Plant",
                RevenueInstrumentType.OfficialReceipt, seed.TenantId);
            var rent = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, seed.TenantId);
            var rentOr = RevenueClassificationPolicy.Create(rent.Id, new DateOnly(2000, 1, 1), "Permanent Stall Rent",
                RevenueInstrumentType.OfficialReceipt, seed.TenantId);
            var record = PaymentRecord.Create(stall.Id, period.Year, period.Month, 1000m, "test");
            record.MarkSettlementPendingCutover();
            var at = DateTime.UtcNow.AddMinutes(-3);
            var cutover = CollectionSettlementCutover.Freeze(seed.TenantId, CollectionSourceKind.PaymentRecord, record.Id, null,
                record.SettlementVersion, at, 1000m, 0m, 1000m,
                "{\"pendingMobileOperations\":0,\"issuedDocuments\":0,\"legacyWritersQuiesced\":true}", seed.UserId, at.AddMinutes(1));
            record.ActivateCanonicalSettlement(cutover);
            setup.AddRange(facility, stall, contract, ice, iceOr, rent, rentOr, record);
            setup.CollectionSettlementCutovers.Add(cutover);
            await setup.SaveChangesAsync();
            iceStallId = stall.Id;
        }

        await using var context = db.CreateContext(seed.TenantId);
        var composer = Composer(context, seed);
        var draft = await composer.AddRentAllocationAsync(
            new AddRentDraftAllocationRequest(iceStallId, period.Year, period.Month, 250m));
        Assert.True(draft.IsSuccess, draft.Error);
        await PostAsync(composer, draft.Value!, seed.OrIds[0]);

        var iceClassification = await context.RevenueClassifications.SingleAsync(x => x.SemanticCode == RevenueClassificationCodes.IcePlant);
        var line = await context.CollectionLines.SingleAsync();
        Assert.Equal(iceClassification.Id, line.RevenueClassificationId);

        // Exactly once (IA-050): attribute the posted Collection AND the converted row's legacy projection to one collector.
        // The legacy projection is comparison evidence, so the collector's report shows the canonical receipt line only.
        var collector = CollectorUser.Create("Ana Reyes", "C-01", $"ana-{Guid.NewGuid():N}"[..14], null, null,
            new HashedPassword("h"), seed.TenantId);
        await using (var attach = db.CreateContext(seed.TenantId))
        {
            attach.Add(collector);
            await attach.SaveChangesAsync();
            await attach.Database.ExecuteSqlRawAsync("UPDATE \"Collections\" SET \"CollectorId\" = {0}", collector.Id);
            await attach.Database.ExecuteSqlRawAsync("UPDATE \"PaymentRecords\" SET \"CollectorId\" = {0}", collector.Id);
        }
        await using var read = db.CreateContext(seed.TenantId);
        var report = await new EEMOCantilanSDS.Infrastructure.Repositories.CollectorReportQueries(read)
            .GetCollectionsAsync(collector.Id, seed.Today.AddDays(-1), seed.Today);
        var only = Assert.Single(report.Lines);
        Assert.Equal(("Ice Plant", 250m), (only.Nature, only.Amount));
    }

    private async Task<Seed> SeedAsync()
    {
        var municipality = Municipality.Create($"OBL-{Guid.NewGuid():N}"[..24], "Obligation Test", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"obl-{Guid.NewGuid():N}"[..32]);
        var tenant = municipality.Id;
        var user = Guid.NewGuid();
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var today = new HistoricalClock().PhilippineToday;
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM",
            archetype: BillingArchetype.DailyStall, municipalityId: tenant);
        var stall = Stall.Create(facility.Id, "FISH-01", 0m, ApplicableFees.None, MarketSection.FishSection,
            createdBy: "test", municipalityId: tenant);
        var unlinkedStall = Stall.Create(facility.Id, "FISH-02", 0m, ApplicableFees.None, MarketSection.FishSection,
            createdBy: "test", municipalityId: tenant);
        var payor = Payor.Create(tenant, "Pedro Vendor", BusinessPayorKind.Person, "test");
        var contract = Contract.Create(stall.Id, "Pedro Vendor", "Pedro Vendor",
            new DateOnly(today.Year, today.Month, 1).AddYears(-1), 20, 0m, createdBy: "test");
        contract.AssociatePayor(payor.Id, "test");
        var unlinkedContract = Contract.Create(unlinkedStall.Id, "Unlinked Vendor", "Unlinked Vendor",
            new DateOnly(today.Year, today.Month, 1).AddYears(-1), 20, 0m, createdBy: "test");

        var entities = new List<object> { facility, stall, unlinkedStall, payor, contract, unlinkedContract };
        foreach (var code in new[]
                 {
                     RevenueClassificationCodes.FishMeatVendorFee, RevenueClassificationCodes.KanmanggaySpaceRental,
                     RevenueClassificationCodes.FiestaArawLotRental
                 })
        {
            var classification = RevenueClassification.Create(code, tenant);
            entities.Add(classification);
            entities.Add(RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1), code,
                RevenueInstrumentType.OfficialReceipt, tenant));
        }
        var book = AccountableFormBook.Receive(tenant, RevenueInstrumentType.OfficialReceipt,
            "OBL TEST OR", "OR-", 1, 99, 4, DateTime.UtcNow.AddMinutes(-5), user.ToString("N"), "test");
        var documents = Enumerable.Range(1, 3).Select(i => AccountableDocument.Register(book, i, "test")).ToArray();
        entities.Add(book);
        entities.AddRange(documents);

        await using (var context = db.CreateContext(tenant))
        {
            context.AddRange(entities);
            await context.SaveChangesAsync();
        }
        return new Seed(tenant, user, stall.Id, payor.Id, unlinkedStall.Id, documents.Select(x => x.Id).ToArray(), today);
    }
}
