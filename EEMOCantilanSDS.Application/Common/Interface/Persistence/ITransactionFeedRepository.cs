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
    Task<IReadOnlyList<TransactionFeedDto>> GetRecentTransactionsAsync(
        FacilityCode? facility, DateOnly? onDate, int limit, CancellationToken ct = default);
}
