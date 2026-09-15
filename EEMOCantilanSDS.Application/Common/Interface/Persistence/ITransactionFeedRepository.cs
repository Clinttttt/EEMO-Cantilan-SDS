using EEMOCantilanSDS.Application.Dtos.Transactions;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface ITransactionFeedRepository
{
    /// <summary>
    /// Returns recorded transactions across all facilities (or a single facility), newest first,
    /// capped at <paramref name="limit"/>. When <paramref name="onDate"/> is supplied, only that
    /// Philippine calendar day's transactions are returned. Aggregates stall rent payments, NPM daily
    /// collections, slaughterhouse transactions, terminal trips, and Tabo-an market attendance.
    /// </summary>
    /// <param name="window">
    /// An explicit UTC window to report over, for a caller that means a PERIOD rather than a day — the Financial Report
    /// asking for the month or year it is showing. Ignored when <paramref name="onDate"/> is given, since that is already
    /// a window of one day. Null means no date restriction, which is the behaviour every caller had before this existed.
    /// </param>
    /// <remarks>
    /// There is deliberately no page parameter. Each of the five sources is queried and capped independently and the
    /// results are merged in memory, so the newest <paramref name="limit"/> rows overall are exact — but an offset would
    /// be applied to five separately-capped sets, and rows could be dropped or repeated between pages. A caller that
    /// needs more than a screenful should raise the limit and say how many it is showing, or send the office to the
    /// transactions page, which is built for browsing.
    /// </remarks>
    Task<IReadOnlyList<TransactionFeedDto>> GetRecentTransactionsAsync(
        FacilityCode? facility, DateOnly? onDate, int limit, CancellationToken ct = default,
        (DateTime StartUtc, DateTime EndUtc)? window = null);
}
