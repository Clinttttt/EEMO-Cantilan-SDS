using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Configuration;
using EEMOCantilanSDS.Testing.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Payments;

/// <summary>Application/canonical aggregate tests only. InMemory cannot prove PostgreSQL transaction/FK/SRC behavior.</summary>
public sealed class NpmRentMonthWorkflowTests
{
    [Theory]
    [InlineData(2, 28, 840)]
    [InlineData(3, 1, 900)]
    public async Task Part_payment_preserves_short_month_topup_cutover_without_day_quantization(int currentMonth, int currentDay, decimal expected)
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 30, calendarMonth: 2, year: 2025,
            partPayments: true, businessDate: new DateOnly(2025, currentMonth, currentDay));
        var partial = await f.Workflow.QuoteAsync(f.Stall.Id, f.Month.PeriodStart, f.BusinessDate, NpmRentPaymentScope.PartPayment);
        Assert.True(partial.IsSuccess, partial.Error); Assert.Equal(expected, partial.Value!.EligibleAmount);
        Assert.Equal(900, (await f.Quote()).Value!.EligibleAmount);
    }
    [Fact]
    public async Task Prepared_month_is_unique_and_review_activation_creates_no_collection_or_SRC()
    {
        await using var f = await NpmMonthWorkflowFixture.Create();
        var period = new DateOnly(2025, 9, 1);
        var first = await f.Workflow.PrepareAsync(f.Stall.Id, f.Month.ContractId, period, NpmRentMonthTests.Evidence());
        Assert.True(first.IsSuccess, first.Error);
        Assert.False((await f.Workflow.PrepareAsync(f.Stall.Id, f.Month.ContractId, period, NpmRentMonthTests.Evidence())).IsSuccess);
        var source = await f.Db.NpmRentMonths.SingleAsync(x => x.PeriodStart == period);
        Assert.False(source.CanAcceptCollections);
        var empty = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("[]")));
        var reviewed = await f.Workflow.ReviewAndActivateAsync(f.Stall.Id, period, 1, new(1, [], [], empty), "Fixture approval only");
        Assert.True(reviewed.IsSuccess, reviewed.Error); Assert.True(source.CanAcceptCollections);
        Assert.Empty(f.Db.Collections); Assert.Empty(f.Db.PostingOperations); Assert.Equal(3, f.Db.AuditLogs.Count());
    }

    [Fact]
    public async Task Operating_credit_uses_existing_rule_and_does_not_overwrite_other_approved_credits()
    {
        await using var f = await NpmMonthWorkflowFixture.Create();
        var absent = DailyCollection.Create(f.Stall.Id, new(2025, 8, 1), "Head", 35);
        NpmRentMonthTests.StampTenant(absent, f.TenantId); absent.MarkAbsent("Head");
        f.Db.DailyCollections.Add(absent); await f.Db.SaveChangesAsync();
        await f.Workflow.SynchronizeOperatingCreditsAsync(f.Month.PeriodStart, "Fixture approved absence");
        await f.Db.SaveChangesAsync();
        Assert.Equal(35, f.Month.CreditedAmount); Assert.Equal(865, (await f.Quote()).Value!.Outstanding);
        Assert.Empty(f.Db.Collections); Assert.Empty(f.Db.CollectionAllocations);
        var manual = await f.Workflow.ReviseCreditsAsync(f.Stall.Id, f.Month.PeriodStart, 40,
            "{\"source\":\"other-approved-exemption\"}", f.Month.SettlementVersion, "Fixture separate approved exemption");
        Assert.True(manual.IsSuccess, manual.Error);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Workflow.SynchronizeOperatingCreditsAsync(f.Month.PeriodStart, "Cannot discard prior credit"));
        Assert.Equal(40, f.Month.CreditedAmount);
    }

    [Fact]
    public async Task Disabled_runtime_cannot_prepare_or_activate_month_sources()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(enabled: false);
        Assert.False((await f.Workflow.PrepareAsync(f.Stall.Id, f.Month.ContractId, new(2025, 9, 1), NpmRentMonthTests.Evidence())).IsSuccess);
        Assert.Single(f.Db.NpmRentMonths); Assert.Empty(f.Db.AuditLogs);
    }
    [Fact]
    public async Task Approved_credit_revision_changes_balance_and_quote_version_without_rewriting_cash()
    {
        await using var f = await NpmMonthWorkflowFixture.Create();
        var before = (await f.Quote()).Value!;
        var credit = await f.Workflow.ReviseCreditsAsync(f.Stall.Id, f.Month.PeriodStart, 30,
            "{\"source\":\"approved-fixture-closure\",\"reference\":\"fixture-only\"}", before.Version, "Fixture approved closure");
        Assert.True(credit.IsSuccess, credit.Error);
        Assert.Equal(870, (await f.Quote()).Value!.Outstanding);
        Assert.Equal(900, f.Month.AssessedAmount);
        Assert.Empty(f.Db.Collections); Assert.Empty(f.Db.DailyCollections);
        Assert.Single(f.Db.AuditLogs);
        Assert.False((await f.Workflow.PostAsync(f.Payment(before))).IsSuccess);
        Assert.True((await f.Workflow.PostAsync(f.Payment((await f.Quote()).Value!))).IsSuccess);
        var count = await f.Db.Collections.CountAsync();
        var increase = await f.Workflow.ReviseCreditsAsync(f.Stall.Id, f.Month.PeriodStart, 60,
            "{\"reference\":\"fixture-authorized-credit-correction\"}", f.Month.SettlementVersion, "Additional approved credit");
        Assert.True(increase.IsSuccess, increase.Error);
        var after = (await f.Quote()).Value!;
        Assert.Equal(30, after.HistoricalExcess); Assert.Equal(0, after.Outstanding);
        Assert.Equal(count, await f.Db.Collections.CountAsync());
        Assert.Equal(870, await f.Db.CollectionAllocations.SumAsync(x => x.Amount));
    }

    [Theory]
    [InlineData(900, 35, 2, 0)]
    [InlineData(900, 35, 2, 2)]
    [InlineData(900, 35, 8, 0)]
    [InlineData(900, 35, 8, 25)]
    [InlineData(1000, 35, 8, 28)]
    [InlineData(900, 30, 2, 0)]
    [InlineData(900, 30, 8, 3)]
    [InlineData(1900, 0, 8, 0)]
    public async Task Whole_payment_posts_exact_remaining_cash_without_fake_daily_rows(decimal rent, decimal daily, int month, int paid)
    {
        await using var f = await NpmMonthWorkflowFixture.Create(rent, daily, month, paid);
        var q = (await f.Quote()).Value!;
        Assert.Equal(rent - daily * paid, q.EligibleAmount);
        var request = f.Payment(q);
        var posted = await f.Workflow.PostAsync(request);
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.False(posted.Value!.Existing);
        Assert.Equal(q.EligibleAmount, posted.Value.Amount);
        var replay = await f.Workflow.PostAsync(request);
        Assert.True(replay.IsSuccess, replay.Error); Assert.True(replay.Value!.Existing);
        Assert.Equal(posted.Value.CollectionId, replay.Value.CollectionId);
        Assert.Equal(1, await f.Db.Collections.CountAsync()); Assert.Equal(1, await f.Db.PostingOperations.CountAsync());
        Assert.Empty(await f.Db.CollectionRemittances.ToListAsync());
        var allocation = await f.Db.CollectionAllocations.SingleAsync();
        Assert.Equal(CollectionSourceKind.NpmRentMonth, allocation.SourceKind);
        Assert.Equal(f.Month.Id, allocation.SourceId); Assert.Equal(f.Month.Id, allocation.NpmRentMonthId);
        Assert.Null(allocation.SourcePart);
        Assert.Equal(0, (await f.Quote()).Value!.Outstanding);
        Assert.Equal(paid, await f.Db.DailyCollections.CountAsync());
        Assert.All(await f.Db.DailyCollections.ToListAsync(), x => { Assert.Equal(daily, x.DailyFee); Assert.Equal("Historical fixture evidence", x.ORNumber); });
        Assert.NotNull(f.Month.SettlementSealedAtUtc);
        Assert.Throws<InvalidOperationException>(() => f.Month.ReviseAssessment(NpmRentMonthTests.Evidence(rent + 1),
            f.Month.SettlementVersion, f.Actor, "Head", "SuperAdmin", DateTime.UtcNow, "Cannot reprice posted evidence"));
    }

    [Theory]
    [InlineData(900, 25, 25)]
    [InlineData(1000, 28, 20)]
    public async Task Terminal_remainder_reversal_reopens_exact_month_and_preserves_original_cash(decimal rent, int days, decimal terminal)
    {
        await using var f = await NpmMonthWorkflowFixture.Create(rent, 35, 8, days);
        var q = (await f.Quote()).Value!; Assert.Equal(terminal, q.EligibleAmount);
        Assert.True((await f.Workflow.PostAsync(f.Payment(q))).IsSuccess);
        var collection = await f.Db.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations).SingleAsync();
        var line = collection.Lines.Single(); var allocation = line.Allocations.Single();
        var operation = Guid.NewGuid();
        var reversal = await f.Workflow.ReverseAsync(collection.Id, operation, "Synthetic authorized reversal");
        Assert.True(reversal.IsSuccess, reversal.Error);
        var repeated = await f.Workflow.ReverseAsync(collection.Id, operation, "Synthetic authorized reversal");
        Assert.True(repeated.IsSuccess, repeated.Error); Assert.True(repeated.Value!.Existing);
        Assert.Equal(-terminal, (await f.Db.CollectionCorrections.SingleAsync()).FinancialEffectAmount);
        Assert.Equal(allocation.Id, (await f.Db.CollectionCorrectionAllocations.SingleAsync()).OriginalAllocationId);
        var reopened = (await f.Quote()).Value!;
        Assert.Equal(terminal, reopened.Outstanding); Assert.Equal(terminal, reopened.EligibleAmount);
        Assert.Equal(terminal, collection.TotalAmount); Assert.Equal(1, await f.Db.Collections.CountAsync());
        Assert.Equal(days, await f.Db.DailyCollections.CountAsync());
    }

    [Theory]
    [InlineData(900, 25, 25)]
    [InlineData(1000, 28, 20)]
    public async Task Daily_terminal_remainder_is_exact_and_a_reversal_does_not_create_false_day_debt(decimal rent, int days, decimal terminal)
    {
        await using var f = await NpmMonthWorkflowFixture.Create(rent, 35, 8, days);
        var date = new DateOnly(2025, 8, days + 1);
        var quoted = await f.Workflow.QuoteAsync(f.Stall.Id, f.Month.PeriodStart, f.BusinessDate,
            NpmRentPaymentScope.DailyInstallment, installmentDate: date);
        Assert.True(quoted.IsSuccess, quoted.Error); Assert.Equal(terminal, quoted.Value!.EligibleAmount);
        var posted = await f.Workflow.PostAsync(f.Payment(quoted.Value));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(days, await f.Db.DailyCollections.CountAsync());
        Assert.Equal(0, (await f.Quote()).Value!.Outstanding);
        Assert.True((await f.Workflow.ReverseAsync(posted.Value!.CollectionId, Guid.NewGuid(), "Synthetic reversal")).IsSuccess);
        var reopened = await f.Workflow.QuoteAsync(f.Stall.Id, f.Month.PeriodStart, f.BusinessDate,
            NpmRentPaymentScope.DailyInstallment, installmentDate: date);
        Assert.Equal(terminal, reopened.Value!.EligibleAmount);
        Assert.Equal(terminal, reopened.Value.Outstanding);
    }

    [Fact]
    public async Task A_daily_quote_cannot_bypass_the_general_part_payment_gate()
    {
        await using var f = await NpmMonthWorkflowFixture.Create();
        var quoted = await f.Workflow.QuoteAsync(f.Stall.Id, f.Month.PeriodStart, f.BusinessDate,
            NpmRentPaymentScope.DailyInstallment, installmentDate: f.Month.PeriodStart);
        Assert.True(quoted.IsSuccess, quoted.Error); Assert.Equal(35, quoted.Value!.EligibleAmount);
        Assert.False((await f.Workflow.PostAsync(f.Payment(quoted.Value) with { Amount = 10 })).IsSuccess);
        Assert.Empty(await f.Db.Collections.ToListAsync());
    }

    [Fact]
    public async Task Approved_monthly_only_part_payments_use_exact_money_without_a_daily_rate()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(1900, 0, partPayments: true, dueFrom: "2025-08-31");
        var q = await f.Workflow.QuoteAsync(f.Stall.Id, f.Month.PeriodStart, f.BusinessDate, NpmRentPaymentScope.PartPayment);
        Assert.True(q.IsSuccess, q.Error); Assert.Equal(1900, q.Value!.EligibleAmount);
        var posted = await f.Workflow.PostAsync(f.Payment(q.Value) with { Amount = 125.25m });
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(1774.75m, (await f.Quote()).Value!.Outstanding);
        Assert.Empty(await f.Db.DailyCollections.ToListAsync());
        Assert.Equal(125.25m, (await f.Db.Collections.SingleAsync()).TotalAmount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-date")]
    public async Task Monthly_only_partial_collection_cannot_invent_its_due_policy(string? dueFrom)
    {
        await using var f = await NpmMonthWorkflowFixture.Create(1900, 0, partPayments: true, dueFrom: dueFrom);
        Assert.False((await f.Workflow.QuoteAsync(f.Stall.Id, f.Month.PeriodStart, f.BusinessDate, NpmRentPaymentScope.PartPayment)).IsSuccess);
        Assert.Empty(await f.Db.Collections.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Future_whole_month_requires_explicit_advance_permission(bool approved)
    {
        await using var f = await NpmMonthWorkflowFixture.Create(1900, 0, year: 2027, advanceWhole: approved);
        var q = await f.Quote();
        Assert.Equal(approved, q.IsSuccess);
        if (approved)
        {
            Assert.Equal(1900, q.Value!.EligibleAmount);
            Assert.True((await f.Workflow.PostAsync(f.Payment(q.Value))).IsSuccess);
            Assert.Empty(await f.Db.DailyCollections.ToListAsync());
        }
    }

    [Fact]
    public async Task PureDays_keeps_its_approved_day_derived_assessment()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(1085, 35, basis: NpmMonthBasis.PureDays);
        Assert.Equal(1085, (await f.Quote()).Value!.EligibleAmount);
    }

    [Fact]
    public async Task Settled_month_refuses_extra_day_31_rent_and_creates_no_financial_event()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 30, 8, 30);
        var q = await f.Workflow.QuoteAsync(f.Stall.Id, f.Month.PeriodStart, f.BusinessDate,
            NpmRentPaymentScope.DailyInstallment, installmentDate: new(2025, 8, 31));
        Assert.True(q.IsSuccess, q.Error); Assert.Equal(0, q.Value!.EligibleAmount);
        Assert.False((await f.Workflow.PostAsync(f.Payment(q.Value) with { Amount = 30 })).IsSuccess);
        Assert.Equal(900, await f.Db.DailyCollections.SumAsync(x => x.DailyFee));
        Assert.Empty(await f.Db.Collections.ToListAsync()); Assert.Empty(await f.Db.PostingOperations.ToListAsync());
    }

    [Fact]
    public async Task Changed_intent_and_a_stale_second_payment_cannot_duplicate_collection()
    {
        await using var f = await NpmMonthWorkflowFixture.Create();
        var q = (await f.Quote()).Value!; var first = f.Payment(q); var stale = f.Payment(q);
        Assert.True((await f.Workflow.PostAsync(first)).IsSuccess);
        Assert.False((await f.Workflow.PostAsync(first with { Amount = first.Amount - 1 })).IsSuccess);
        Assert.False((await f.Workflow.PostAsync(stale)).IsSuccess);
        Assert.Equal(1, await f.Db.Collections.CountAsync());
        Assert.Equal(900, await f.Db.CollectionAllocations.SumAsync(x => x.Amount));
        // Sequential stale-token proof, NOT a PostgreSQL concurrent-writer proof.
    }

    [Fact]
    public async Task Dormant_runtime_and_wrong_tenant_cannot_quote_or_create_cash()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(enabled: false);
        Assert.False((await f.Quote()).IsSuccess);
        Assert.Empty(await f.Db.Collections.ToListAsync());
        Assert.False(new NpmRentMonthRuntime().Allows(f.TenantId));
        Assert.False(new NpmRentMonthRuntime(true, true, false, new HashSet<Guid> { f.TenantId }).Allows(f.TenantId));
        Assert.False(new NpmRentMonthRuntime(true, true, true, new HashSet<Guid> { f.TenantId }).Allows(Guid.NewGuid()));
    }

    [Fact]
    public async Task Generic_coordinator_cannot_bypass_the_specialized_month_permit()
    {
        await using var f = await NpmMonthWorkflowFixture.Create();
        var classification = await f.Db.RevenueClassifications.SingleAsync(); var policy = await f.Db.RevenueClassificationPolicies.SingleAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CanonicalCollectionPostingCoordinator(f.Db).PostAsync(f.TenantId,
            Guid.NewGuid(), 1, "unpermitted", "OtherOrigin", f.Actor.ToString(), "Head", "SuperAdmin", f.BusinessDate, "Head",
            [new(classification, policy, 25, CollectionSourceKind.NpmRentMonth, f.Month.Id, null,
                Allocations: [new(CollectionSourceKind.NpmRentMonth, f.Month.Id, 25)])]));
        Assert.Empty(await f.Db.Collections.ToListAsync());
    }
}

internal sealed class NpmMonthWorkflowFixture : IAsyncDisposable
{
    internal required AppDbContext Db { get; init; }
    internal required NpmRentMonthWorkflow Workflow { get; init; }
    internal required NpmRentMonth Month { get; init; }
    internal required Stall Stall { get; init; }
    internal required NpmMonthSettlementService Legacy { get; init; }
    internal required IFeeRateResolver Rates { get; init; }
    internal required ICurrentUserService User { get; init; }
    internal required FixedClock Clock { get; init; }
    internal Guid TenantId => Month.MunicipalityId;
    internal Guid Actor => NpmRentMonthTests.Reviewer;
    internal DateOnly BusinessDate { get; init; } = new(2026, 10, 17);
    internal Task<EEMOCantilanSDS.Application.Common.Result<NpmRentMonthQuote>> Quote() => Workflow.QuoteAsync(Stall.Id, Month.PeriodStart, BusinessDate, NpmRentPaymentScope.WholeMonth);
    internal NpmRentMonthPayment Payment(NpmRentMonthQuote q) => new(Guid.NewGuid(), Stall.Id, Month.PeriodStart, BusinessDate, q.Scope, q.EligibleAmount, q.Token, q.InstallmentDate);

    internal static async Task<NpmMonthWorkflowFixture> Create(decimal rent = 900, decimal daily = 35, int calendarMonth = 8, int paidDays = 0,
        bool enabled = true, NpmMonthBasis basis = NpmMonthBasis.RentGoal, bool partPayments = false, string? dueFrom = null,
        int year = 2025, bool advanceWhole = false, bool asCollector = false, DateOnly? businessDate = null)
    {
        var today = businessDate ?? new DateOnly(2026, 10, 17);
        var now = DateTime.SpecifyKind(today.ToDateTime(new TimeOnly(4, 0)), DateTimeKind.Utc);
        var reviewedAt = businessDate.HasValue ? now.AddDays(-1) : NpmRentMonthTests.Utc;
        var (facility, stall, contract) = NpmRentMonthTests.Source(); var tenant = new Tenant(facility.MunicipalityId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("npm-workflow-" + Guid.NewGuid())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)).Options;
        var db = new FinancialPreviewContext(options, tenant);
        var days = new List<DailyCollection>();
        for (var i = 1; i <= paidDays; i++)
        {
            var day = DailyCollection.Create(stall.Id, new(year, calendarMonth, i), "Fixture", daily);
            NpmRentMonthTests.StampTenant(day, tenant.MunicipalityId); day.MarkPaid("Historical fixture evidence", null); days.Add(day);
        }
        var emptyOrLegacy = days.OrderBy(x => x.Id).Select(x => new { x.Id, x.CollectionDate, x.CollectedFee }).ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(emptyOrLegacy, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        var assessment = NpmRentMonthTests.Evidence(rent, basis: basis) with
        { RateEvidenceJson = JsonSerializer.Serialize(new { schemaVersion = 1, approved = true, dailyInstallmentsApproved = daily > 0,
            partPaymentsApproved = partPayments, monthlyObligationDueFrom = dueFrom, advanceWholeMonthApproved = advanceWhole },
            new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }) };
        var month = NpmRentMonth.Prepare(facility, stall, contract, new(year, calendarMonth, 1), today,
            assessment, NpmRentMonthTests.Reviewer, "Head", "SuperAdmin", reviewedAt).Month;
        month.Reconcile(1, NpmRentMonthTests.Reviewer, "Head", "SuperAdmin", reviewedAt,
            JsonSerializer.Serialize(new NpmRentMonthOpeningEvidence(1, days.Select(x => x.Id).ToArray(), [], fingerprint), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        month.ActivateSettlement(2, NpmRentMonthTests.Reviewer, "Head", "SuperAdmin", reviewedAt,
            "Synthetic isolated fixture activation; no municipal source is activated");
        db.AddRange(facility, stall, contract, month); db.DailyCollections.AddRange(days);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, tenant.MunicipalityId);
        db.RevenueClassifications.Add(classification);
        db.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(classification.Id, new(2020, 1, 1), "Rent", RevenueInstrumentType.OfficialReceipt, tenant.MunicipalityId));
        await db.SaveChangesAsync();
        var user = new Mock<ICurrentUserService>(); user.SetupGet(x => x.IsAuthenticated).Returns(true);
        user.SetupGet(x => x.UserId).Returns(NpmRentMonthTests.Reviewer); user.SetupGet(x => x.Username).Returns("Head");
        user.SetupGet(x => x.Role).Returns("SuperAdmin"); user.SetupGet(x => x.MunicipalityId).Returns(tenant.MunicipalityId);
        var collectors = new Mock<ICollectorRepository>();
        if (asCollector)
        {
            var collector = EEMOCantilanSDS.Domain.Entities.Users.CollectorUser.Create("Fixture", "TEST", "fixture", null, null,
                new EEMOCantilanSDS.Domain.Common.HashedPassword("fixture-only-hash"), tenant.MunicipalityId);
            collector.FacilityAssignments.Add(EEMOCantilanSDS.Domain.Entities.Users.CollectorFacilityAssignment.Create(collector.Id, facility.Id, FacilityCode.NPM));
            user.SetupGet(x => x.CollectorId).Returns(collector.Id); user.SetupGet(x => x.Role).Returns("Collector");
            collectors.Setup(x => x.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        }
        var fees = new Mock<IFeeRateResolver>();
        var entries = new List<FeeRateEntry> { new(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, rent, new(2020, 1, 1)) };
        if (daily > 0) entries.Add(new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, daily, new(2020, 1, 1)));
        fees.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new FeeRateSnapshot(entries, null, basis));
        var closures = new Mock<INpmMarketClosureRepository>();
        closures.Setup(x => x.GetByMonthAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<NpmMarketClosure>());
        var daysRepository = new Mock<IDailyCollectionRepository>();
        daysRepository.Setup(x => x.GetByStallAndMonthAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int requestedYear, int requestedMonth, CancellationToken _) =>
                days.Where(x => x.CollectionDate.Year == requestedYear && x.CollectionDate.Month == requestedMonth).ToArray());
        var clock = new FixedClock(now);
        var legacy = new NpmMonthSettlementService(daysRepository.Object, closures.Object, fees.Object, clock);
        return new() { Db = db, Month = month, Stall = stall, Legacy = legacy, Rates = fees.Object, User = user.Object, Clock = clock, BusinessDate = today, Workflow = new(db, tenant, user.Object, collectors.Object, fees.Object,
            clock, new(enabled, true, true, new HashSet<Guid> { tenant.MunicipalityId }), new(partPayments), closures.Object, assessments: legacy) };
    }
    public ValueTask DisposeAsync() => Db.DisposeAsync();
    private sealed class FinancialPreviewContext(DbContextOptions<AppDbContext> options, ICurrentMunicipalityAccessor tenant)
        : AppDbContext(options, tenant), IAppDbContext
    {
        // In-process transaction-port stand-in ONLY. No atomicity, rollback or concurrency proof is claimed.
        bool IAppDbContext.HasActiveTransaction => true;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            NpmRentMonthFinancialSchema.Configure(modelBuilder);
        }
    }
    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor { public Guid MunicipalityId => id; public void Set(Guid id) => throw new NotSupportedException(); }
}
