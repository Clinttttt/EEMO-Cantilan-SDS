using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Revenue.GetCanonicalMonthlyIncome;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Application.Revenue;

/// <summary>
/// Canonical Monthly Income foundation: tenant, classification identity, Collection business-date period, and the
/// ADR-005 AsOf versus LatestCorrected knowledge bases. Legacy sources are out of scope by definition.
/// </summary>
public sealed class GetCanonicalMonthlyIncomeQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 4, 0, 0, DateTimeKind.Utc);

    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Caller(Guid municipalityId, string role = "SuperAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Username => "income-test";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => municipalityId;
    }

    private sealed class Tenant
    {
        public required Guid Id { get; init; }
        public required RevenueClassification Rent { get; init; }
        public required RevenueClassificationPolicy RentPolicy { get; init; }
        public required RevenueClassification Ecf { get; init; }
        public required RevenueClassificationPolicy EcfPolicy { get; init; }
        public required RevenueClassification Wcf { get; init; }
        public required RevenueClassificationPolicy WcfPolicy { get; init; }
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"canonical-income-{Guid.NewGuid()}")
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;

    private static AppDbContext Context(DbContextOptions<AppDbContext> options, Guid tenantId) =>
        new(options, new FixedMunicipality(tenantId));

    private static async Task<Tenant> SeedTenantAsync(DbContextOptions<AppDbContext> options)
    {
        var id = Guid.NewGuid();
        var rent = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, id);
        var ecf = RevenueClassification.Create(RevenueClassificationCodes.Ecf, id);
        var wcf = RevenueClassification.Create(RevenueClassificationCodes.Wcf, id);
        var tenant = new Tenant
        {
            Id = id,
            Rent = rent,
            RentPolicy = RevenueClassificationPolicy.Create(rent.Id, new DateOnly(2020, 1, 1), "Stall Rental", RevenueInstrumentType.OfficialReceipt, id),
            Ecf = ecf,
            EcfPolicy = RevenueClassificationPolicy.Create(ecf.Id, new DateOnly(2020, 1, 1), "Electricity Consumption Fee", RevenueInstrumentType.OfficialReceipt, id),
            Wcf = wcf,
            WcfPolicy = RevenueClassificationPolicy.Create(wcf.Id, new DateOnly(2020, 1, 1), "Water Consumption Fee", RevenueInstrumentType.CashTicket, id)
        };
        await using var context = Context(options, id);
        context.AddRange(rent, ecf, wcf, tenant.RentPolicy, tenant.EcfPolicy, tenant.WcfPolicy);
        await context.SaveChangesAsync();
        return tenant;
    }

    private static Collection Post(DateOnly businessDate, DateTime recordedAtUtc, params CollectionLineDraft[] lines) =>
        Collection.Post(businessDate, recordedAtUtc, "actor", "Actor", "Collector", lines);

    private static CollectionCorrection Reverse(Guid tenantId, Collection original, decimal amount,
        DateOnly effectiveDate, DateTime recordedAtUtc) =>
        CollectionCorrection.Record(tenantId, original.Id, null, null, null, CollectionCorrectionType.Reversal,
            effectiveDate, recordedAtUtc, -amount, "test reversal", "head", "Head",
            [new CollectionCorrectionLineDraft(original.Lines.Single().Id, -amount)]);

    private static async Task AddAsync(DbContextOptions<AppDbContext> options, Guid tenantId, params object[] entities)
    {
        await using var context = Context(options, tenantId);
        context.AddRange(entities);
        await context.SaveChangesAsync();
    }

    private static async Task<Result<CanonicalMonthlyIncomeDto>> RunAsync(
        DbContextOptions<AppDbContext> options, Guid tenantId, GetCanonicalMonthlyIncomeQuery query,
        string role = "SuperAdmin")
    {
        await using var context = Context(options, tenantId);
        return await new GetCanonicalMonthlyIncomeQueryHandler(context, new Caller(tenantId, role),
            new FixedMunicipality(tenantId), new FixedClock(Now)).Handle(query, default);
    }

    private static GetCanonicalMonthlyIncomeQuery September(CanonicalReportingBasis basis, DateTimeOffset? asOf = null) =>
        new(2026, 9, basis, asOf);

    [Fact]
    public async Task ClassifiedLineMoneyIsReportedPerClassificationNotPerWholeEventOrAllocation()
    {
        var options = Options();
        var t = await SeedTenantAsync(options);
        var rentSource = Guid.NewGuid();
        // One OR-backed Collection: rent 500 allocated across two periods + ECF 500. Allocations restate line money.
        var visit = Post(new DateOnly(2026, 9, 12), new DateTime(2026, 9, 12, 2, 0, 0, DateTimeKind.Utc),
            new CollectionLineDraft(t.Rent, t.RentPolicy, 500m, CollectionSourceKind.PaymentRecord, rentSource, null, null,
                [new CollectionAllocationDraft(CollectionSourceKind.PaymentRecord, rentSource, 300m),
                 new CollectionAllocationDraft(CollectionSourceKind.PaymentRecord, Guid.NewGuid(), 200m)]),
            new CollectionLineDraft(t.Ecf, t.EcfPolicy, 500m));
        var water = Post(new DateOnly(2026, 9, 10), new DateTime(2026, 9, 10, 2, 0, 0, DateTimeKind.Utc),
            new CollectionLineDraft(t.Wcf, t.WcfPolicy, 10m, CollectionSourceKind.UtilityBill, Guid.NewGuid(), CollectionSourcePart.Water));
        var august = Post(new DateOnly(2026, 8, 31), new DateTime(2026, 9, 1, 1, 0, 0, DateTimeKind.Utc),
            new CollectionLineDraft(t.Wcf, t.WcfPolicy, 99m, CollectionSourceKind.UtilityBill, Guid.NewGuid(), CollectionSourcePart.Water));
        await AddAsync(options, t.Id, visit, water, august);

        var result = await RunAsync(options, t.Id, September(CanonicalReportingBasis.LatestCorrected));

        Assert.True(result.IsSuccess, result.Error);
        var report = result.Value!;
        Assert.Equal(3, report.Rows.Count); // only classifications with canonical evidence; no fabricated zero rows
        Assert.Equal(500m, report.Rows.Single(x => x.SemanticCode == RevenueClassificationCodes.PermanentStallRent).Total.NetCollected);
        Assert.Equal(500m, report.Rows.Single(x => x.SemanticCode == RevenueClassificationCodes.Ecf).Total.NetCollected);
        var wcf = report.Rows.Single(x => x.SemanticCode == RevenueClassificationCodes.Wcf);
        Assert.Equal(10m, wcf.Total.GrossOriginalCollected); // Aug 31 business date stays in August despite Sep recording
        Assert.Equal("Water Consumption Fee", wcf.DisplayName);
        Assert.Equal(1010m, report.GrandTotal.NetCollected);
        Assert.Equal(2, report.CollectionCount);
        Assert.False(report.LegacySourcesIncluded);
        Assert.Equal("CanonicalCollectionsOnly", report.SourceCoverage);
        Assert.Equal(new DateOnly(2026, 9, 1), report.PeriodStart);
        Assert.Equal(new DateOnly(2026, 9, 30), report.PeriodEnd);
        var water9 = Assert.Single(report.Sources, x => x.SourceKind == "UtilityBill" && x.SourcePart == "Water");
        Assert.Equal(10m, water9.GrossOriginalCollected);
    }

    [Fact]
    public async Task AsOfExcludesLaterRecordedReversalWhileLatestCorrectedNetsItAgainstTheOriginalPeriod()
    {
        var options = Options();
        var t = await SeedTenantAsync(options);
        // ADR-005 example: +300 recorded 26 Sep 10:00 (PH), reversal recorded 27 Sep 09:00 (PH).
        var original = Post(new DateOnly(2026, 9, 26), new DateTime(2026, 9, 26, 2, 0, 0, DateTimeKind.Utc),
            new CollectionLineDraft(t.Rent, t.RentPolicy, 300m));
        var reversal = Reverse(t.Id, original, 300m, new DateOnly(2026, 9, 27), new DateTime(2026, 9, 27, 1, 0, 0, DateTimeKind.Utc));
        await AddAsync(options, t.Id, original, reversal);

        var asOf = await RunAsync(options, t.Id, September(CanonicalReportingBasis.AsOf,
            new DateTimeOffset(2026, 9, 26, 23, 59, 0, TimeSpan.FromHours(8))));
        var latest = await RunAsync(options, t.Id, September(CanonicalReportingBasis.LatestCorrected));

        Assert.True(asOf.IsSuccess, asOf.Error);
        Assert.Equal(300m, asOf.Value!.GrandTotal.GrossOriginalCollected);
        Assert.Equal(0m, asOf.Value.GrandTotal.CorrectionEffect);
        Assert.Equal(300m, asOf.Value.GrandTotal.NetCollected);
        Assert.Equal(new DateTime(2026, 9, 26, 15, 59, 0, DateTimeKind.Utc), asOf.Value.KnowledgeCutoffUtc);
        Assert.Equal(0, asOf.Value.CorrectionCount);

        Assert.True(latest.IsSuccess, latest.Error);
        Assert.Equal(300m, latest.Value!.GrandTotal.GrossOriginalCollected); // the original stays visible
        Assert.Equal(-300m, latest.Value.GrandTotal.CorrectionEffect);
        Assert.Equal(0m, latest.Value.GrandTotal.NetCollected);
        Assert.Equal(0m, latest.Value.GrandTotal.CorrectionEffectDatedOutsidePeriod);
        Assert.Equal(Now, latest.Value.KnowledgeCutoffUtc);
        Assert.Equal(1, latest.Value.CorrectionCount);
    }

    [Fact]
    public async Task BackdatedCorrectionIsBoundedByRecordedKnowledgeAndCrossPeriodEffectStaysVisible()
    {
        var options = Options();
        var t = await SeedTenantAsync(options);
        var original = Post(new DateOnly(2026, 9, 10), new DateTime(2026, 9, 10, 2, 0, 0, DateTimeKind.Utc),
            new CollectionLineDraft(t.Ecf, t.EcfPolicy, 800m));
        var partial = Post(new DateOnly(2026, 9, 11), new DateTime(2026, 9, 11, 2, 0, 0, DateTimeKind.Utc),
            new CollectionLineDraft(t.Ecf, t.EcfPolicy, 200m));
        // Recorded 2 Oct but backdated to 20 Sep: must not leak into a 30 Sep recorded-knowledge report.
        var backdated = Reverse(t.Id, original, 800m, new DateOnly(2026, 9, 20), new DateTime(2026, 10, 2, 1, 0, 0, DateTimeKind.Utc));
        // A September event reversed with an October effective date: attributed to September, flagged cross-period.
        var october = Reverse(t.Id, partial, 200m, new DateOnly(2026, 10, 1), new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc));
        await AddAsync(options, t.Id, original, partial, backdated, october);

        var asOfSeptemberClose = await RunAsync(options, t.Id, September(CanonicalReportingBasis.AsOf,
            new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.FromHours(8))));
        var latest = await RunAsync(options, t.Id, September(CanonicalReportingBasis.LatestCorrected));

        Assert.Equal(1000m, asOfSeptemberClose.Value!.GrandTotal.NetCollected);
        Assert.Equal(0m, asOfSeptemberClose.Value.GrandTotal.CorrectionEffect);
        var cell = latest.Value!.GrandTotal;
        Assert.Equal(1000m, cell.GrossOriginalCollected);
        Assert.Equal(-1000m, cell.CorrectionEffect);
        Assert.Equal(0m, cell.NetCollected);
        Assert.Equal(-200m, cell.CorrectionEffectDatedOutsidePeriod);
        var monthCell = Assert.Single(latest.Value.MonthTotals);
        Assert.Equal(9, monthCell.Month);
        Assert.Equal(-200m, monthCell.CorrectionEffectDatedOutsidePeriod);

        var octoberReport = await RunAsync(options, t.Id, new GetCanonicalMonthlyIncomeQuery(2026, 10, CanonicalReportingBasis.LatestCorrected));
        Assert.Empty(octoberReport.Value!.Rows); // no later-period adjustment is invented for October
        Assert.Equal(0m, octoberReport.Value.GrandTotal.NetCollected);
    }

    [Fact]
    public async Task DocumentOnlyCorrectionAddsNoRevenueAndReplacementCollectionIsItsOwnEvent()
    {
        var options = Options();
        var t = await SeedTenantAsync(options);
        var original = Post(new DateOnly(2026, 9, 5), new DateTime(2026, 9, 5, 2, 0, 0, DateTimeKind.Utc),
            new CollectionLineDraft(t.Rent, t.RentPolicy, 500m));
        var documentOnly = CollectionCorrection.Record(t.Id, original.Id, Guid.NewGuid(), Guid.NewGuid(), null,
            CollectionCorrectionType.DocumentCorrection, new DateOnly(2026, 9, 6),
            new DateTime(2026, 9, 6, 1, 0, 0, DateTimeKind.Utc), 0m, "OR reprinted", "head", "Head", []);
        var mistaken = Post(new DateOnly(2026, 9, 7), new DateTime(2026, 9, 7, 2, 0, 0, DateTimeKind.Utc),
            new CollectionLineDraft(t.Rent, t.RentPolicy, 400m));
        var replacementEvent = Post(new DateOnly(2026, 9, 8), new DateTime(2026, 9, 8, 2, 0, 0, DateTimeKind.Utc),
            new CollectionLineDraft(t.Rent, t.RentPolicy, 350m));
        var replacement = CollectionCorrection.Record(t.Id, mistaken.Id, Guid.NewGuid(), Guid.NewGuid(), replacementEvent.Id,
            CollectionCorrectionType.Replacement, new DateOnly(2026, 9, 8), new DateTime(2026, 9, 8, 3, 0, 0, DateTimeKind.Utc),
            -400m, "wrong amount", "head", "Head", [new CollectionCorrectionLineDraft(mistaken.Lines.Single().Id, -400m)]);
        await AddAsync(options, t.Id, original, documentOnly, mistaken, replacementEvent, replacement);

        var report = (await RunAsync(options, t.Id, September(CanonicalReportingBasis.LatestCorrected))).Value!;

        var rent = Assert.Single(report.Rows);
        Assert.Equal(1250m, rent.Total.GrossOriginalCollected); // 500 + 400 + 350 posted events
        Assert.Equal(-400m, rent.Total.CorrectionEffect);
        Assert.Equal(850m, rent.Total.NetCollected); // 500 (document-only adds nothing) + 350 replacement
        Assert.Equal(1, rent.Total.CorrectionLineCount);
    }

    [Fact]
    public async Task AsOfExcludesCollectionsRecordedAfterTheCutoffEvenWithAnEarlierBusinessDate()
    {
        var options = Options();
        var t = await SeedTenantAsync(options);
        var lateSync = Post(new DateOnly(2026, 9, 29), new DateTime(2026, 10, 2, 1, 0, 0, DateTimeKind.Utc),
            new CollectionLineDraft(t.Wcf, t.WcfPolicy, 10m, CollectionSourceKind.UtilityBill, Guid.NewGuid(), CollectionSourcePart.Water));
        await AddAsync(options, t.Id, lateSync);

        var asOf = await RunAsync(options, t.Id, September(CanonicalReportingBasis.AsOf,
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));
        var latest = await RunAsync(options, t.Id, September(CanonicalReportingBasis.LatestCorrected));

        Assert.Empty(asOf.Value!.Rows);
        Assert.Equal(10m, latest.Value!.GrandTotal.NetCollected);
    }

    [Fact]
    public async Task YearViewHasTwelveMonthCellsAndTotalsAcrossThem()
    {
        var options = Options();
        var t = await SeedTenantAsync(options);
        await AddAsync(options, t.Id,
            Post(new DateOnly(2026, 1, 15), new DateTime(2026, 1, 15, 2, 0, 0, DateTimeKind.Utc), new CollectionLineDraft(t.Rent, t.RentPolicy, 100m)),
            Post(new DateOnly(2026, 9, 15), new DateTime(2026, 9, 15, 2, 0, 0, DateTimeKind.Utc), new CollectionLineDraft(t.Rent, t.RentPolicy, 250m)),
            Post(new DateOnly(2025, 12, 31), new DateTime(2025, 12, 31, 2, 0, 0, DateTimeKind.Utc), new CollectionLineDraft(t.Rent, t.RentPolicy, 999m)));

        var report = (await RunAsync(options, t.Id, new GetCanonicalMonthlyIncomeQuery(2026, null, CanonicalReportingBasis.LatestCorrected))).Value!;

        var row = Assert.Single(report.Rows);
        Assert.Equal(12, row.Months.Count);
        Assert.Equal(100m, row.Months.Single(x => x.Month == 1).NetCollected);
        Assert.Equal(250m, row.Months.Single(x => x.Month == 9).NetCollected);
        Assert.Equal(0m, row.Months.Single(x => x.Month == 5).NetCollected);
        Assert.Equal(350m, row.Total.NetCollected);
        Assert.Equal(12, report.MonthTotals.Count);
        Assert.Equal(new DateOnly(2026, 12, 31), report.PeriodEnd);
    }

    [Fact]
    public async Task AnotherTenantsCollectionsAndCorrectionsAreNeverCounted()
    {
        var options = Options();
        var a = await SeedTenantAsync(options);
        var b = await SeedTenantAsync(options);
        var aCollection = Post(new DateOnly(2026, 9, 3), new DateTime(2026, 9, 3, 2, 0, 0, DateTimeKind.Utc), new CollectionLineDraft(a.Rent, a.RentPolicy, 70m));
        var bCollection = Post(new DateOnly(2026, 9, 3), new DateTime(2026, 9, 3, 2, 0, 0, DateTimeKind.Utc), new CollectionLineDraft(b.Rent, b.RentPolicy, 5000m));
        await AddAsync(options, a.Id, aCollection);
        await AddAsync(options, b.Id, bCollection, Reverse(b.Id, bCollection, 5000m, new DateOnly(2026, 9, 4), new DateTime(2026, 9, 4, 1, 0, 0, DateTimeKind.Utc)));

        var report = (await RunAsync(options, a.Id, September(CanonicalReportingBasis.LatestCorrected))).Value!;

        Assert.Equal(a.Id, report.MunicipalityId);
        var row = Assert.Single(report.Rows);
        Assert.Equal(a.Rent.Id, row.RevenueClassificationId);
        Assert.Equal(70m, report.GrandTotal.NetCollected);
        Assert.Equal(0, report.CorrectionCount);
    }

    [Fact]
    public async Task FutureCutoffCollectorCallerAndMismatchedTenantAreRefused()
    {
        var options = Options();
        var t = await SeedTenantAsync(options);

        var future = await RunAsync(options, t.Id, September(CanonicalReportingBasis.AsOf, new DateTimeOffset(Now.AddMinutes(5))));
        var collector = await RunAsync(options, t.Id, September(CanonicalReportingBasis.LatestCorrected), role: "Collector");
        await using var context = Context(options, t.Id);
        var mismatched = await new GetCanonicalMonthlyIncomeQueryHandler(context, new Caller(Guid.NewGuid()),
            new FixedMunicipality(t.Id), new FixedClock(Now)).Handle(September(CanonicalReportingBasis.LatestCorrected), default);

        Assert.False(future.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, future.Status);
        Assert.Equal(ResultStatus.Forbidden, collector.Status);
        Assert.Equal(ResultStatus.Forbidden, mismatched.Status);
    }

    [Fact]
    public void ValidatorRequiresAnExplicitCutoffOnlyForAsOf()
    {
        var validator = new GetCanonicalMonthlyIncomeQueryValidator();

        Assert.False(validator.Validate(new GetCanonicalMonthlyIncomeQuery(2026, 9, CanonicalReportingBasis.AsOf)).IsValid);
        Assert.False(validator.Validate(new GetCanonicalMonthlyIncomeQuery(2026, 9, CanonicalReportingBasis.LatestCorrected, DateTimeOffset.UtcNow)).IsValid);
        Assert.False(validator.Validate(new GetCanonicalMonthlyIncomeQuery(2026, 13, CanonicalReportingBasis.LatestCorrected)).IsValid);
        Assert.False(validator.Validate(new GetCanonicalMonthlyIncomeQuery(2026, 9, (CanonicalReportingBasis)9)).IsValid);
        Assert.True(validator.Validate(new GetCanonicalMonthlyIncomeQuery(2026, null, CanonicalReportingBasis.LatestCorrected)).IsValid);
        Assert.True(validator.Validate(new GetCanonicalMonthlyIncomeQuery(2026, 9, CanonicalReportingBasis.AsOf, DateTimeOffset.UtcNow)).IsValid);
    }
}
