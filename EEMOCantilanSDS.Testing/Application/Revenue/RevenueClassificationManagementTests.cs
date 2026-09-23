using EEMOCantilanSDS.Application.Command.Revenue.AppendRevenueClassificationPolicy;
using EEMOCantilanSDS.Application.Command.Revenue.CreateRevenueClassification;
using EEMOCantilanSDS.Application.Command.Revenue.RetireRevenueClassification;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueClassificationPolicyHistory;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueClassifications;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Application.Revenue;

public sealed class RevenueClassificationManagementTests
{
    private static readonly DateOnly Today = new(2026, 9, 23);
    private static readonly IClock Clock = new FixedClock(new DateTime(2026, 9, 22, 16, 0, 0, DateTimeKind.Utc));

    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Caller(Guid? municipalityId, bool authenticated = true, string? username = "head.carmen")
        : ICurrentUserService
    {
        public bool IsAuthenticated => authenticated;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => username;
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => municipalityId;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"revenue-management-{Guid.NewGuid()}")
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;

    private static AppDbContext Context(DbContextOptions<AppDbContext> options, Guid tenantId) =>
        new(options, new FixedMunicipality(tenantId));

    private static RevenueClassificationPolicy Policy(
        Guid classificationId,
        Guid tenantId,
        DateOnly date,
        string displayName,
        RevenueInstrumentType? instrument = RevenueInstrumentType.OfficialReceipt) =>
        RevenueClassificationPolicy.Create(
            classificationId, date, displayName, instrument, tenantId, createdBy: "seed");

    [Fact]
    public async Task ListResolvesAsOfPolicy_LeavesFutureVersionUnapplied_AndIncludesUnconfiguredClasses()
    {
        var options = Options();
        var tenant = Guid.NewGuid();
        Guid priorId;
        Guid futureOnlyId;

        await using (var seed = Context(options, tenant))
        {
            var priorClassification = RevenueClassification.Create("MARKET_FEES", tenant, "seed");
            priorId = priorClassification.Id;
            var futureClassification = RevenueClassification.Create("TABO", tenant, "seed");
            futureOnlyId = futureClassification.Id;
            seed.AddRange(
                priorClassification,
                Policy(priorClassification.Id, tenant, new DateOnly(2026, 8, 1), "Market Fees - prior", RevenueInstrumentType.CashTicket),
                Policy(priorClassification.Id, tenant, new DateOnly(2026, 10, 1), "Market Fees - future", RevenueInstrumentType.OfficialReceipt),
                futureClassification,
                Policy(futureClassification.Id, tenant, new DateOnly(2026, 10, 1), "Tabo", RevenueInstrumentType.CashTicket),
                RevenueClassification.Create("UNCONFIGURED", tenant, "seed"));
            await seed.SaveChangesAsync();
        }

        await using var context = Context(options, tenant);
        var handler = new GetRevenueClassificationsQueryHandler(context, new Caller(tenant), Clock);
        var result = await handler.Handle(new(new DateOnly(2026, 9, 23)), default);

        Assert.True(result.IsSuccess);
        var rows = result.Value!;
        var priorResult = Assert.Single(rows, x => x.Id == priorId);
        Assert.Equal("MARKET_FEES", priorResult.SemanticCode);
        Assert.True(priorResult.HasPolicyVersions);
        Assert.Equal("Market Fees - prior", priorResult.EffectivePolicy!.DisplayName);
        Assert.Equal(new DateOnly(2026, 8, 1), priorResult.EffectivePolicy.EffectiveDate);
        Assert.Equal(RevenueInstrumentType.CashTicket, priorResult.EffectivePolicy.PermittedInstrumentType);

        var futureOnlyResult = Assert.Single(rows, x => x.Id == futureOnlyId);
        Assert.True(futureOnlyResult.HasPolicyVersions);
        Assert.Null(futureOnlyResult.EffectivePolicy);

        var unconfigured = Assert.Single(rows, x => x.SemanticCode == "UNCONFIGURED");
        Assert.False(unconfigured.HasPolicyVersions);
        Assert.Null(unconfigured.EffectivePolicy);
    }

    [Fact]
    public async Task OmittedAsOfUsesPhilippineBusinessDate_AndPolicyHistoryIsNewestFirst()
    {
        var options = Options();
        var tenant = Guid.NewGuid();
        Guid classificationId;
        await using (var seed = Context(options, tenant))
        {
            var classification = RevenueClassification.Create("ECF", tenant, "seed");
            classificationId = classification.Id;
            seed.AddRange(
                classification,
                Policy(classification.Id, tenant, Today.AddDays(-1), "Old", RevenueInstrumentType.OfficialReceipt),
                Policy(classification.Id, tenant, Today, "Current", RevenueInstrumentType.OfficialReceipt),
                Policy(classification.Id, tenant, Today.AddDays(1), "Future", RevenueInstrumentType.CashTicket));
            await seed.SaveChangesAsync();
        }

        await using var context = Context(options, tenant);
        var current = await new GetRevenueClassificationsQueryHandler(context, new Caller(tenant), Clock)
            .Handle(new(), default);
        Assert.Equal("Current", Assert.Single(current.Value!, x => x.Id == classificationId).EffectivePolicy!.DisplayName);

        var history = await new GetRevenueClassificationPolicyHistoryQueryHandler(context, new Caller(tenant))
            .Handle(new(classificationId), default);
        Assert.True(history.IsSuccess);
        Assert.Equal(new[] { "Future", "Current", "Old" }, history.Value!.Select(x => x.DisplayName));
    }

    [Fact]
    public async Task CreateUsesCallerTenantAndActor_AllowsNullInstrument_ButRejectsSameTenantDuplicate()
    {
        var options = Options();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var command = new CreateRevenueClassificationCommand(
            "MARKET_FEES", "Market Fees", Today, null, null);

        await using (var asA = Context(options, tenantA))
        {
            var handler = new CreateRevenueClassificationCommandHandler(asA, new Caller(tenantA, username: "head.a"));
            var created = await handler.Handle(command, default);
            Assert.True(created.IsSuccess);
            Assert.Equal("MARKET_FEES", created.Value!.SemanticCode);
            Assert.True(created.Value.HasPolicyVersions);
            Assert.Null(created.Value.EffectivePolicy!.PermittedInstrumentType);
            Assert.Equal("head.a", created.Value.EffectivePolicy.CreatedBy);

            var duplicate = await handler.Handle(command, default);
            Assert.Equal(ResultStatus.Conflict, duplicate.Status);
        }

        // The same stable meaning is independently configurable in another tenant.
        await using (var asB = Context(options, tenantB))
        {
            var created = await new CreateRevenueClassificationCommandHandler(asB, new Caller(tenantB))
                .Handle(command with { DisplayName = "Market Fees - B" }, default);
            Assert.True(created.IsSuccess);
        }

        Assert.Null(typeof(CreateRevenueClassificationCommand).GetProperty("MunicipalityId"));
    }

    [Fact]
    public async Task AppendingPolicyPreservesOldVersion_ResolvesOnItsEffectiveDate_AndRejectsDuplicateDate()
    {
        var options = Options();
        var tenant = Guid.NewGuid();
        Guid classificationId;
        Guid originalPolicyId;
        await using (var seed = Context(options, tenant))
        {
            var classification = RevenueClassification.Create("ECF", tenant, "seed");
            var original = Policy(classification.Id, tenant, Today, "ECF old", RevenueInstrumentType.OfficialReceipt);
            classificationId = classification.Id;
            originalPolicyId = original.Id;
            seed.AddRange(classification, original);
            await seed.SaveChangesAsync();
        }

        await using (var context = Context(options, tenant))
        {
            var handler = new AppendRevenueClassificationPolicyCommandHandler(context, new Caller(tenant, username: "head.a"));
            var appended = await handler.Handle(new(
                classificationId, Today.AddDays(1), "ECF revised", "Tenant wording", RevenueInstrumentType.CashTicket), default);
            Assert.True(appended.IsSuccess);
            Assert.Equal("head.a", appended.Value!.CreatedBy);

            var duplicate = await handler.Handle(new(
                classificationId, Today.AddDays(1), "Another wording", null, RevenueInstrumentType.OfficialReceipt), default);
            Assert.Equal(ResultStatus.Conflict, duplicate.Status);
        }

        await using var verify = Context(options, tenant);
        var history = await new GetRevenueClassificationPolicyHistoryQueryHandler(verify, new Caller(tenant))
            .Handle(new(classificationId), default);
        Assert.Equal(2, history.Value!.Count);
        var originalVersion = Assert.Single(history.Value, x => x.PolicyId == originalPolicyId);
        Assert.Equal("ECF old", originalVersion.DisplayName);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, originalVersion.PermittedInstrumentType);

        var beforeEffectiveDate = await new GetRevenueClassificationsQueryHandler(verify, new Caller(tenant), Clock)
            .Handle(new(Today), default);
        Assert.Equal("ECF old", Assert.Single(beforeEffectiveDate.Value!).EffectivePolicy!.DisplayName);
        var onEffectiveDate = await new GetRevenueClassificationsQueryHandler(verify, new Caller(tenant), Clock)
            .Handle(new(Today.AddDays(1)), default);
        var current = Assert.Single(onEffectiveDate.Value!);
        Assert.Equal("ECF revised", current.EffectivePolicy!.DisplayName);
        Assert.Equal(RevenueInstrumentType.CashTicket, current.EffectivePolicy.PermittedInstrumentType);
    }

    [Fact]
    public async Task RetirementIsAttributableAndPreservesRowAndPolicyHistory()
    {
        var options = Options();
        var tenant = Guid.NewGuid();
        Guid classificationId;
        Guid policyId;
        await using (var seed = Context(options, tenant))
        {
            var classification = RevenueClassification.Create("ARREARS", tenant, "seed");
            var policy = Policy(classification.Id, tenant, Today, "Arrears", null);
            classificationId = classification.Id;
            policyId = policy.Id;
            seed.AddRange(classification, policy);
            await seed.SaveChangesAsync();
        }

        await using (var context = Context(options, tenant))
        {
            var result = await new RetireRevenueClassificationCommandHandler(context, new Caller(tenant, username: "head.retiring"))
                .Handle(new(classificationId), default);
            Assert.True(result.IsSuccess);
            await context.SaveChangesAsync();
        }

        await using var verify = Context(options, tenant);
        var listed = await new GetRevenueClassificationsQueryHandler(verify, new Caller(tenant), Clock)
            .Handle(new(Today), default);
        var retired = Assert.Single(listed.Value!);
        Assert.False(retired.IsActive);
        Assert.Equal("ARREARS", retired.SemanticCode);

        var stored = await verify.RevenueClassifications.SingleAsync(x => x.Id == classificationId);
        Assert.False(stored.IsDeleted);
        Assert.Null(stored.DeletedAt);
        Assert.Equal("head.retiring", stored.UpdatedBy);
        Assert.Contains(await verify.RevenueClassificationPolicies.ToListAsync(), x => x.Id == policyId);

        var history = await new GetRevenueClassificationPolicyHistoryQueryHandler(verify, new Caller(tenant))
            .Handle(new(classificationId), default);
        Assert.Single(history.Value!);
    }

    [Fact]
    public async Task CrossTenantReadAppendAndRetirementTreatClassificationAsUnavailable()
    {
        var options = Options();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid classificationId;
        await using (var asB = Context(options, tenantB))
        {
            var classification = RevenueClassification.Create("MARKET_FEES", tenantB, "seed");
            classificationId = classification.Id;
            asB.AddRange(classification, Policy(classification.Id, tenantB, Today, "B market fees", RevenueInstrumentType.OfficialReceipt));
            await asB.SaveChangesAsync();
        }

        await using var asA = Context(options, tenantA);
        var callerA = new Caller(tenantA);
        var list = await new GetRevenueClassificationsQueryHandler(asA, callerA, Clock).Handle(new(), default);
        Assert.Empty(list.Value!);

        var history = await new GetRevenueClassificationPolicyHistoryQueryHandler(asA, callerA)
            .Handle(new(classificationId), default);
        Assert.Equal(ResultStatus.NotFound, history.Status);

        var append = await new AppendRevenueClassificationPolicyCommandHandler(asA, callerA)
            .Handle(new(classificationId, Today.AddDays(1), "Attempt", null, RevenueInstrumentType.CashTicket), default);
        Assert.Equal(ResultStatus.NotFound, append.Status);

        var retire = await new RetireRevenueClassificationCommandHandler(asA, callerA)
            .Handle(new(classificationId), default);
        Assert.Equal(ResultStatus.NotFound, retire.Status);

        await using var verifyB = Context(options, tenantB);
        Assert.True((await verifyB.RevenueClassifications.SingleAsync(x => x.Id == classificationId)).IsActive);
        Assert.Equal("B market fees", (await verifyB.RevenueClassificationPolicies.SingleAsync()).DisplayName);
    }

    [Fact]
    public async Task UnresolvedOrUnauthenticatedTenantCannotReadOrWriteConfiguration()
    {
        var options = Options();
        await using var context = Context(options, Guid.Empty);
        var unresolved = new Caller(null);

        Assert.Equal(ResultStatus.Forbidden,
            (await new GetRevenueClassificationsQueryHandler(context, unresolved, Clock).Handle(new(), default)).Status);
        Assert.Equal(ResultStatus.Forbidden,
            (await new GetRevenueClassificationPolicyHistoryQueryHandler(context, unresolved)
                .Handle(new(Guid.NewGuid()), default)).Status);
        Assert.Equal(ResultStatus.Forbidden,
            (await new CreateRevenueClassificationCommandHandler(context, unresolved)
                .Handle(new("ECF", "ECF", Today, null, RevenueInstrumentType.OfficialReceipt), default)).Status);

        var unauthenticated = new Caller(Guid.NewGuid(), authenticated: false);
        Assert.Equal(ResultStatus.Forbidden,
            (await new CreateRevenueClassificationCommandHandler(context, unauthenticated)
                .Handle(new("TABO", "Tabo", Today, null, RevenueInstrumentType.CashTicket), default)).Status);
    }

    [Fact]
    public async Task PolicyValidatorsRejectBackdating_AndCreationValidatorRejectsInvalidSemanticIdentity()
    {
        var create = new CreateRevenueClassificationCommandValidator(Clock);
        var invalidCode = await create.ValidateAsync(
            new CreateRevenueClassificationCommand("Market fees", "Market Fees", Today, null, null));
        Assert.False(invalidCode.IsValid);

        var missingName = await create.ValidateAsync(
            new CreateRevenueClassificationCommand("MARKET_FEES", "  ", Today, null, null));
        Assert.False(missingName.IsValid);

        var longDescription = await create.ValidateAsync(
            new CreateRevenueClassificationCommand("MARKET_FEES", "Market Fees", Today, new string('x', 501), null));
        Assert.False(longDescription.IsValid);

        var invalidInstrument = await create.ValidateAsync(
            new CreateRevenueClassificationCommand("MARKET_FEES", "Market Fees", Today, null, (RevenueInstrumentType)999));
        Assert.False(invalidInstrument.IsValid);

        var unresolvedInstrument = await create.ValidateAsync(
            new CreateRevenueClassificationCommand("ARREARS", "Arrears", Today, null, null));
        Assert.True(unresolvedInstrument.IsValid);

        var pastCreate = await create.ValidateAsync(
            new CreateRevenueClassificationCommand("MARKET_FEES", "Market Fees", Today.AddDays(-1), null, null));
        Assert.False(pastCreate.IsValid);

        var append = new AppendRevenueClassificationPolicyCommandValidator(Clock);
        var todayPolicy = await append.ValidateAsync(
            new AppendRevenueClassificationPolicyCommand(Guid.NewGuid(), Today, "Name", null, null));
        Assert.True(todayPolicy.IsValid);

        var pastPolicy = await append.ValidateAsync(
            new AppendRevenueClassificationPolicyCommand(Guid.NewGuid(), Today.AddDays(-1), "Name", null, null));
        Assert.False(pastPolicy.IsValid);
        var futurePolicy = await append.ValidateAsync(
            new AppendRevenueClassificationPolicyCommand(Guid.NewGuid(), Today.AddDays(1), "Name", null, null));
        Assert.True(futurePolicy.IsValid);
    }
}
