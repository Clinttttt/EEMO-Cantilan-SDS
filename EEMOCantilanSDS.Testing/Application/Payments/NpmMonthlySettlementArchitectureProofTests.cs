using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Payments;

/// <summary>
/// Current-code evidence for Task 2B, not approval of current defects or an implementation of Option C.
/// Uses existing in-process fixtures only. Pending requirements are explicitly separate.
/// </summary>
public sealed class NpmMonthlySettlementArchitectureProofTests
{
    [Theory]
    [InlineData(900, 30, 2, 0, 900, 60, 0)]
    [InlineData(900, 30, 4, 0, 900, 0, 0)]
    [InlineData(900, 30, 8, 0, 900, 0, 0)]
    [InlineData(900, 35, 2, 0, 875, 0, 25)]
    [InlineData(900, 35, 8, 0, 875, 0, 25)]
    [InlineData(1000, 35, 2, 0, 1000, 20, 0)]
    [InlineData(1000, 35, 8, 0, 980, 0, 20)]
    [InlineData(900, 30, 8, 3, 810, 0, 0)]
    [InlineData(900, 30, 8, 30, 0, 0, 0)]
    [InlineData(900, 35, 8, 25, 0, 0, 25)]
    [InlineData(1000, 35, 8, 28, 0, 0, 20)]
    public async Task Current_quote_and_actual_settlement_expose_the_same_whole_installment_limit(
        decimal rent, decimal daily, int month, int earlierDays, decimal available, decimal adjustment, decimal residual)
    {
        var (stall, rows, service) = NpmMonthlyBillingCharacterizationTests.Fixture(dailyFee: daily, monthlyRent: rent);
        Seed(stall, rows, month, earlierDays, daily);
        var original = rows.Select(x => (x.Id, x.DailyFee, x.ORNumber)).ToArray();
        var quote = await service.ComputeWholeMonthPayableAsync(stall, 2025, month, CancellationToken.None);
        Assert.Equal(rent, quote.Obligation);
        Assert.Equal(earlierDays * daily, quote.Collected);
        Assert.Equal(0m, quote.Credits);
        Assert.Equal(available, quote.Amount);
        Assert.Equal(adjustment, quote.Adjustment);

        await service.SettleWholeMonthAsync(stall, 2025, month, null, "Proof", CancellationToken.None, quote.Amount);
        var after = await service.ComputeWholeMonthPayableAsync(stall, 2025, month, CancellationToken.None);
        Assert.Equal(rent - residual, after.Collected);
        Assert.Equal(residual, DomainRules.DailyBilledMonthOutstanding(rent, after.Collected!.Value, after.Credits!.Value));
        Assert.Equal(0m, after.Amount); // A zero quote is not proof of zero outstanding.
        foreach (var prior in original)
        {
            var unchanged = Assert.Single(rows, x => x.Id == prior.Id);
            Assert.Equal(prior.DailyFee, unchanged.DailyFee);
            Assert.Equal(prior.ORNumber, unchanged.ORNumber);
        }
    }

    [Theory]
    [InlineData(900, 25, 25, 10)]
    [InlineData(1000, 28, 20, 15)]
    public async Task Option_A_real_partial_money_settles_month_but_leaves_daily_residual_and_reverses_exactly(
        decimal rent, int earlierDays, decimal terminalAmount, decimal dailyResidual)
    {
        var (stall, rows, service) = NpmMonthlyBillingCharacterizationTests.Fixture(dailyFee: 35m, monthlyRent: rent);
        Seed(stall, rows, 8, earlierDays, 35m);
        var terminal = DailyCollection.Create(stall.Id, new DateOnly(2025, 8, earlierDays + 1), "Proof", 35m);
        terminal.ApplyCanonicalPartial(Guid.NewGuid(), terminalAmount, null, "Proof");
        rows.Add(terminal);
        var paid = await service.ComputeWholeMonthPayableAsync(stall, 2025, 8, CancellationToken.None);
        Assert.Equal(rent, paid.Collected);
        Assert.Equal(0m, paid.Amount);
        Assert.False(terminal.IsPaid);
        Assert.True(terminal.IsPartlyPaid);
        Assert.Equal(dailyResidual, terminal.RemainingBaseFee);
        terminal.ReverseCanonicalPartial(terminalAmount, null, "Proof reversal");
        var reversed = await service.ComputeWholeMonthPayableAsync(stall, 2025, 8, CancellationToken.None);
        Assert.Equal(terminalAmount, DomainRules.DailyBilledMonthOutstanding(rent, reversed.Collected!.Value, reversed.Credits!.Value));
        Assert.Equal(35m, terminal.BaseFee);
        Assert.Equal(35m, terminal.RemainingBaseFee);
        Assert.All(rows.Take(earlierDays), x => Assert.Equal("Historical evidence", x.ORNumber));
    }

    [Fact]
    public async Task Current_daily_discovery_and_ordinary_legacy_writer_still_allow_day_31_after_month_paid()
    {
        var (result, total, listed) = await AttemptDay31();
        Assert.True(listed); // Current uncapped discovery; this is a defect under the new ruling.
        Assert.True(result);
        Assert.Equal(930m, total);
    }

    [Fact]
    public async Task Required_daily_writer_must_refuse_extra_stall_rent_after_month_paid()
    {
        await using var f = await NpmMonthWorkflowFixture.Create(900, 30, paidDays: 30);
        var date = new DateOnly(2025, 8, 31);
        var listed = await NpmMonthSettlementBridgeTests.Bridge(f).GetPayableDaysAsync(f.Stall, 2025, 8, default);
        Assert.DoesNotContain(date, listed);
        var stalls = new Mock<IStallRepository>();
        stalls.Setup(x => x.GetByIdAsync(f.Stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(f.Stall);
        var user = new Mock<ICurrentUserService>(); user.SetupGet(x => x.Role).Returns("SuperAdmin");
        var days = new Mock<IDailyCollectionRepository>(MockBehavior.Strict);
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var quote = await f.Workflow.QuoteAsync(f.Stall.Id, f.Month.PeriodStart, f.BusinessDate,
            EEMOCantilanSDS.Application.Common.Payments.NpmRentPaymentScope.DailyInstallment, installmentDate: date);
        Assert.True(quote.IsSuccess, quote.Error);
        Assert.Equal(0, quote.Value!.EligibleAmount);
        var handler = new RecordDailyCollectionCommandHandler(days.Object, Mock.Of<IPaymentRepository>(), Mock.Of<IOrNumberRegistry>(),
            stalls.Object, Mock.Of<ICollectorRepository>(), user.Object, uow.Object, CacheTestDoubles.Invalidator,
            Mock.Of<IFeeRateResolver>(), CacheTestDoubles.Tenant, monthly: f.Workflow);
        var result = await handler.Handle(new(f.Stall.Id, date, true, ClientOperationId: Guid.NewGuid(),
            ReviewedMonthAmount: 30, MonthQuoteToken: quote.Value.Token), default);
        Assert.False(result.IsSuccess);
        Assert.Equal(900, f.Db.DailyCollections.Sum(x => x.DailyFee));
        Assert.Empty(f.Db.Collections); Assert.Empty(f.Db.PostingOperations);
        days.VerifyNoOtherCalls(); uow.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Existing_canonical_day_void_reopens_only_its_rent_and_keeps_other_daily_evidence()
    {
        var (stall, rows, service) = NpmMonthlyBillingCharacterizationTests.Fixture();
        Seed(stall, rows, 8, 30, 30m);
        var reversed = rows[14];
        reversed.ApplyCanonicalPayment(Guid.NewGuid());
        reversed.ApplyCanonicalVoid("Proof reversal");
        var quote = await service.ComputeWholeMonthPayableAsync(stall, 2025, 8, CancellationToken.None);
        Assert.Equal(870m, quote.Collected);
        Assert.Equal(30m, quote.Amount);
        Assert.Equal(30m, reversed.BaseFee);
        Assert.All(rows.Where(row => row.Id != reversed.Id), row =>
        {
            Assert.True(row.IsPaid);
            Assert.Equal(30m, row.DailyFee);
            Assert.Equal("Historical evidence", row.ORNumber);
        });
        // Projection proof only: no actual CollectionCorrection, SRC or remittance was persisted.
    }

    [Fact]
    public async Task Existing_excess_historical_daily_cash_is_preserved_when_month_outstanding_is_zero()
    {
        var (stall, rows, service) = NpmMonthlyBillingCharacterizationTests.Fixture();
        Seed(stall, rows, 8, 31, 30m);
        var quote = await service.ComputeWholeMonthPayableAsync(stall, 2025, 8, CancellationToken.None);
        Assert.Equal(930m, quote.Collected);
        Assert.Equal(900m, quote.Obligation);
        Assert.Equal(0m, quote.Amount);
        Assert.Equal(0m, DomainRules.DailyBilledMonthOutstanding(900m, quote.Collected!.Value, 0m));
        Assert.Equal(31, rows.Count);
        Assert.All(rows, row => Assert.Equal("Historical evidence", row.ORNumber));
    }

    [Theory]
    [InlineData(CollectionSourceKind.PaymentRecord)]
    [InlineData(CollectionSourceKind.ObligationPeriod)]
    public void Existing_collection_aggregate_accepts_exact_month_source_money_without_daily_allocations(CollectionSourceKind kind)
    {
        var tenant = Guid.NewGuid();
        var date = new DateOnly(2025, 8, 31);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, tenant);
        var policy = RevenueClassificationPolicy.Create(classification.Id, date, "Stall rent", RevenueInstrumentType.OfficialReceipt, tenant);
        var source = Guid.NewGuid();
        var collection = Collection.Post(date, new DateTime(2026, 10, 16, 4, 0, 0, DateTimeKind.Utc),
            "Proof", "Proof", "SuperAdmin",
            [new CollectionLineDraft(classification, policy, 1900m, kind, source, null,
                Allocations: [new CollectionAllocationDraft(kind, source, 1900m)])]);
        var allocation = Assert.Single(Assert.Single(collection.Lines).Allocations);
        Assert.Equal(1900m, allocation.Amount);
        Assert.Equal(source, allocation.SourceId);
        Assert.Null(allocation.SourcePart);
        Assert.Equal(tenant, allocation.MunicipalityId);
        Assert.Equal(string.Empty, collection.ReferenceCode); // No database save and no issued SRC.
        // This is a domain shape proof only: no source lookup, NPM adapter, posting or remittance was exercised.
    }

    [Fact]
    public void Existing_generic_month_adapter_excludes_NPM_and_monthly_source_lacks_occupancy_and_policy_provenance()
    {
        Assert.DoesNotContain(FacilityCode.NPM, MonthlyRentalFacilities.Codes);
        Assert.Null(typeof(PaymentRecord).GetProperty("ContractId"));
        Assert.Null(typeof(PaymentRecord).GetProperty("ObligationRateId"));
        Assert.Null(typeof(PaymentRecord).GetProperty("MonthBasis"));
        Assert.True(Enum.IsDefined(CollectionSourceKind.NpmRentMonth));
        Assert.False(new EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthRuntime().Allows(Guid.NewGuid()));
        // The source contract is now present but dormant; generic monthly facilities still do not own NPM.
    }

    [Fact]
    public async Task Current_low_level_monthly_only_quote_would_materialize_zero_base_days_and_calendar_adjustment()
    {
        var (stall, rows, service) = NpmMonthlyBillingCharacterizationTests.Fixture(dailyFee: 0m, monthlyRent: 1900m);
        var quote = await service.ComputeWholeMonthPayableAsync(stall, 2025, 8, CancellationToken.None);
        Assert.Equal(1900m, quote.Obligation);
        Assert.Equal(1900m, quote.Amount);
        Assert.Equal(1900m, quote.Adjustment);
        Assert.Equal(31, quote.Days);
        await service.SettleWholeMonthAsync(stall, 2025, 8, null, "Proof", CancellationToken.None, quote.Amount);
        Assert.Equal(31, rows.Count);
        Assert.All(rows, day => Assert.Equal(0m, day.BaseFee));
        Assert.Equal(1900m, rows.Sum(day => day.DailyFee));
        // Mock-only evidence of an unsupported low-level shape, not permission to use it for monthly-only LGUs.
    }

    private static void Seed(Stall stall, List<DailyCollection> rows, int month, int count, decimal fee)
    {
        for (var day = 1; day <= count; day++)
        {
            var row = DailyCollection.Create(stall.Id, new DateOnly(2025, month, day), "Proof", fee);
            row.MarkPaid("Historical evidence", null);
            rows.Add(row);
        }
    }

    private static async Task<(bool Succeeded, decimal ActualDailyTotal, bool Day31Listed)> AttemptDay31()
    {
        var (stall, rows, service) = NpmMonthlyBillingCharacterizationTests.Fixture();
        Seed(stall, rows, 8, 30, 30m);
        var date = new DateOnly(2025, 8, 31);
        var listed = (await service.GetPayableDaysAsync(stall, 2025, 8, CancellationToken.None)).Contains(date);
        var days = new Mock<IDailyCollectionRepository>();
        days.Setup(x => x.GetByStallAndDateAsync(stall.Id, date, It.IsAny<CancellationToken>())).ReturnsAsync((DailyCollection?)null);
        days.Setup(x => x.GetByStallAndMonthAsync(stall.Id, 2025, 8, It.IsAny<CancellationToken>())).ReturnsAsync(() => rows.ToArray());
        days.Setup(x => x.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((day, _) => rows.Add(day)).Returns(Task.CompletedTask);
        var stalls = new Mock<IStallRepository>();
        stalls.Setup(x => x.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        var rates = new Mock<IFeeRateResolver>();
        rates.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new FeeRateSnapshot([
            new(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, new(2020, 1, 1)),
            new(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 900m, new(2020, 1, 1))]));
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.Role).Returns("SuperAdmin");
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var handler = new RecordDailyCollectionCommandHandler(days.Object, Mock.Of<IPaymentRepository>(), Mock.Of<IOrNumberRegistry>(),
            stalls.Object, Mock.Of<ICollectorRepository>(), user.Object, uow.Object, CacheTestDoubles.Invalidator,
            rates.Object, CacheTestDoubles.Tenant);
        var result = await handler.Handle(new(stall.Id, date, IsPaid: true), CancellationToken.None);
        return (result.IsSuccess, rows.Where(x => x.IsPaid).Sum(x => x.DailyFee), listed);
    }
}
