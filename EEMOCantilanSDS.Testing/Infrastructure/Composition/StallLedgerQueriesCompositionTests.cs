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
        // Aggregate persistence and receipt-number availability stay where they were. This is a split, not a rewrite,
        // and nothing about how a payment is written or an OR is judged changed here.
        var wide = typeof(IPaymentRepository).GetMethods().Select(m => m.Name).ToHashSet();

        Assert.Contains("AddAsync", wide);
        Assert.Contains("UpdateAsync", wide);
        Assert.Contains("IsORNumberUniqueAsync", wide);
        Assert.Contains("IsDailyCollectionOrAvailableForStallAsync", wide);
        Assert.Contains("IsMonthlyOrAvailableForStallAsync", wide);
    }
}
