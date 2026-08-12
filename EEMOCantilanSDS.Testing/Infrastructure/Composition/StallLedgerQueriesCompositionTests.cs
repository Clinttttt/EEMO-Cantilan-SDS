using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing.Infrastructure.Composition;

/// <summary>
/// The narrow per-stall ledger reads and the wide payment repository must be the SAME object within a request.
///
/// <para>
/// IStallLedgerQueries was split out of IPaymentRepository so a handler that only reads one account stops depending on
/// something that can also write payments and rule on receipt numbers. The registration deliberately resolves the
/// existing repository rather than registering the type a second time: two instances per request would mean two change
/// trackers, so a read taken after a write in the same request could miss it - the kind of fault that shows up as a
/// figure that is stale by exactly one operation.
/// </para>
///
/// <para>This asserts the RELATIONSHIP the DI registration has to preserve. It is deliberately not a test of the
/// container: it checks that one type satisfies both contracts, which is what makes sharing the instance possible and
/// what a later move of the code must keep true.</para>
/// </summary>
public class StallLedgerQueriesCompositionTests
{
    [Fact]
    public void OneTypeSatisfiesBothContracts()
    {
        Assert.True(typeof(IPaymentRepository).IsAssignableFrom(typeof(PaymentRepository)));
        Assert.True(typeof(IStallLedgerQueries).IsAssignableFrom(typeof(PaymentRepository)));
        Assert.True(typeof(IMissingReceiptQueries).IsAssignableFrom(typeof(PaymentRepository)));
    }

    [Fact]
    public void TheAwaitingReceiptQueueHasItsOwnContract()
    {
        // A different question from what a stall owes: these scan a whole PERIOD across every payor for money already
        // received whose receipt has not been written down. The follow-up reports asked it through a contract that could
        // also write payments.
        var wide = typeof(IPaymentRepository).GetMethods().Select(m => m.Name).ToHashSet();

        Assert.DoesNotContain(nameof(IMissingReceiptQueries.GetUnreceiptedCashPaymentsAsync), wide);
        Assert.DoesNotContain(nameof(IMissingReceiptQueries.GetUnreceiptedCashPaymentsForYearAsync), wide);
    }

    [Fact]
    public void TheWideRepositoryNoLongerOffersThePerStallLedgerReads()
    {
        // The point of the split: a caller cannot reach these through the wide contract any more, so new code has to
        // ask for the narrow one. Named methods rather than a count, so adding an unrelated member does not fail this.
        var wide = typeof(IPaymentRepository).GetMethods().Select(m => m.Name).ToHashSet();

        Assert.DoesNotContain(nameof(IStallLedgerQueries.GetPaymentHistoryAsync), wide);
        Assert.DoesNotContain(nameof(IStallLedgerQueries.GetStallCollectionHistoryAsync), wide);
        Assert.DoesNotContain(nameof(IStallLedgerQueries.GetStallLedgerSummaryAsync), wide);
        Assert.DoesNotContain(nameof(IStallLedgerQueries.GetOutstandingMonthsAsync), wide);
    }

    [Fact]
    public void TheWideRepositoryKeepsWhatItIsFor()
    {
        // Aggregate persistence stays where it was. This is a split, not a rewrite, and nothing about how a payment is
        // written or an OR is judged changed here.
        var wide = typeof(IPaymentRepository).GetMethods().Select(m => m.Name).ToHashSet();

        Assert.Contains("AddAsync", wide);
        Assert.Contains("UpdateAsync", wide);
        Assert.Contains("IsDailyCollectionOrAvailableForStallAsync", wide);
        Assert.Contains("IsMonthlyOrAvailableForStallAsync", wide);

        // Plain OR availability has moved to IOrNumberRegistry, because it is an LGU-wide question and five interfaces
        // each declaring it invited the reading that the market's rule and the terminal's rule were different rules.
        // What stays here are the two allowances that only mean something for a payment record: one receipt may settle
        // several months, or cover several days, of the SAME stall.
        Assert.DoesNotContain("IsORNumberUniqueAsync", wide);
        foreach (var moduleContract in new[] { typeof(ISlaughterRepository), typeof(ITpmRepository), typeof(ITrmRepository), typeof(IUtilityBillRepository) })
            Assert.DoesNotContain("IsORNumberUniqueAsync", moduleContract.GetMethods().Select(m => m.Name));

        var registry = typeof(IOrNumberRegistry).GetMethods().Select(m => m.Name).ToHashSet();
        Assert.Contains(nameof(IOrNumberRegistry.IsAvailableAsync), registry);
        Assert.Contains(nameof(IOrNumberRegistry.IsAvailableForUtilityBillAsync), registry);
    }

    [Fact]
    public void TheCollectorAppsScreensHaveTheirOwnContract()
    {
        // The app needs every payor for a round in one payload, already carrying status and balance, because it must keep
        // working when the signal drops halfway down the market. That is a different shape from anything the office reads,
        // and a handler serving it has no business with stall aggregates or number uniqueness.
        Assert.True(typeof(IStallMobileQueries).IsAssignableFrom(typeof(StallRepository)));
        Assert.True(typeof(IStallRepository).IsAssignableFrom(typeof(StallRepository)));

        var wide = typeof(IStallRepository).GetMethods().Select(m => m.Name).ToHashSet();
        Assert.DoesNotContain(nameof(IStallMobileQueries.GetMobileNpmCollectionAsync), wide);
        Assert.DoesNotContain(nameof(IStallMobileQueries.GetMobileMonthlyCollectionAsync), wide);

        // And it keeps what it is for: loading a stall to modify, and the number rules.
        Assert.Contains("GetByIdWithContractsAsync", wide);
        Assert.Contains("IsStallNoUniqueAsync", wide);
    }

    [Fact]
    public void ACollectorsOwnScreensAreNotTheAccountRepository()
    {
        // ICollectorRepository is an account repository: it loads a collector to modify, finds one for LOGIN, and rules
        // on uniqueness. The three projections a collector reads about their own work had no business sitting beside an
        // authentication lookup, and a test for one of those screens had to stub seventeen members to get at it.
        Assert.True(typeof(ICollectorMobileQueries).IsAssignableFrom(typeof(CollectorRepository)));
        Assert.True(typeof(ICollectorRepository).IsAssignableFrom(typeof(CollectorRepository)));

        var wide = typeof(ICollectorRepository).GetMethods().Select(m => m.Name).ToHashSet();
        Assert.DoesNotContain(nameof(ICollectorMobileQueries.GetCollectorRecordsAsync), wide);
        Assert.DoesNotContain(nameof(ICollectorMobileQueries.GetCollectorReportAsync), wide);
        Assert.DoesNotContain(nameof(ICollectorMobileQueries.GetCollectorProfileAsync), wide);

        // The account side keeps the login lookup and the uniqueness rules.
        Assert.Contains("GetByUsernameOrEmployeeIdAsync", wide);
        Assert.Contains("IsEmployeeIdUniqueAsync", wide);
        Assert.Contains("AddAsync", wide);
    }

    [Fact]
    public void TheOfficesRosterViewsAreNeitherTheAppsNorTheAccounts()
    {
        // Three different questions about one subject, now three contracts. The office asks who collected how much this
        // month; the collector's app asks what to collect next; the account repository is about the person's record. One
        // interface answering all three is how a supervisor's report ends up able to reset a password.
        Assert.True(typeof(ICollectorReportingQueries).IsAssignableFrom(typeof(CollectorRepository)));

        var wide = typeof(ICollectorRepository).GetMethods().Select(m => m.Name).ToHashSet();
        Assert.DoesNotContain(nameof(ICollectorReportingQueries.GetAllCollectorsWithStatsAsync), wide);
        Assert.DoesNotContain(nameof(ICollectorReportingQueries.GetCollectorActivityAsync), wide);

        // And the two read contracts stay apart from each other.
        var mobile = typeof(ICollectorMobileQueries).GetMethods().Select(m => m.Name).ToHashSet();
        Assert.DoesNotContain(nameof(ICollectorReportingQueries.GetAllCollectorsWithStatsAsync), mobile);
    }

    [Fact]
    public void TheFollowUpReadsCannotChangeWhatTheyReportOn()
    {
        // The register of inactive accounts and the contracts needing attention are what the follow-up queue, the
        // follow-up history and the financial report are built from. Off IStallRepository they came with the ability to
        // let, transfer, renew and close a stall — everything the report describes. Two narrow read contracts now carry
        // them, and both resolve to the same StallRepository instance.
        Assert.True(typeof(IClosedStallAccountQueries).IsAssignableFrom(typeof(StallRepository)));
        Assert.True(typeof(IContractAttentionQueries).IsAssignableFrom(typeof(StallRepository)));

        var wide = typeof(IStallRepository).GetMethods().Select(m => m.Name).ToHashSet();
        Assert.DoesNotContain(nameof(IClosedStallAccountQueries.GetClosedStallAccountsAsync), wide);
        Assert.DoesNotContain(nameof(IClosedStallAccountQueries.GetClosedStallAccountsForPeriodAsync), wide);
        Assert.DoesNotContain(nameof(IContractAttentionQueries.GetContractAttentionAsync), wide);
        Assert.DoesNotContain(nameof(IContractAttentionQueries.GetContractAttentionAsOfAsync), wide);

        // The lifetime and period readings must both stay reachable and distinct: serving a period report from the
        // lifetime figures is how a monthly report starts showing arrears that predate the month.
        var closedRegister = typeof(IClosedStallAccountQueries).GetMethods().Select(m => m.Name).ToHashSet();
        Assert.Contains(nameof(IClosedStallAccountQueries.GetClosedStallAccountsAsync), closedRegister);
        Assert.Contains(nameof(IClosedStallAccountQueries.GetClosedStallAccountsForPeriodAsync), closedRegister);
    }

    [Fact]
    public void DisplayingTheRegisterIsNotHoldingTheStallAggregate()
    {
        // The facility pages, the stallholders list, the section summaries and the utility register only DISPLAY spaces.
        // Off IStallRepository each of those six handlers could also let a space, transfer it, close it or decide whether
        // a stall number was free.
        Assert.True(typeof(IStallRegisterQueries).IsAssignableFrom(typeof(StallRepository)));

        var wide = typeof(IStallRepository).GetMethods().Select(m => m.Name).ToHashSet();
        Assert.DoesNotContain(nameof(IStallRegisterQueries.GetStallsByFacilityAsync), wide);
        Assert.DoesNotContain(nameof(IStallRegisterQueries.GetStallsByFacilityPaginatedAsync), wide);
        Assert.DoesNotContain(nameof(IStallRegisterQueries.GetStallHoldersListAsync), wide);
        Assert.DoesNotContain(nameof(IStallRegisterQueries.GetSectionSummariesAsync), wide);

        // What the aggregate is FOR must stay on it: letting a space and ruling on its number.
        Assert.Contains("AddAsync", wide);
        Assert.Contains("AddContractAsync", wide);
        Assert.Contains("UpdateAsync", wide);
        Assert.Contains("IsStallNoUniqueAsync", wide);
        Assert.Contains("GetByIdWithContractsAsync", wide);

        // And the register cannot write: no aggregate mutation may appear on the read contract.
        var register = typeof(IStallRegisterQueries).GetMethods().Select(m => m.Name).ToHashSet();
        Assert.DoesNotContain("AddAsync", register);
        Assert.DoesNotContain("UpdateAsync", register);
        Assert.DoesNotContain("IsStallNoUniqueAsync", register);
    }
}
