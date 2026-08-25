using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// Remittances a collector has made, and the figure they are checked against.
///
/// <para>
/// The office's rule is that a remittance may never exceed what the collector actually collected, so the total below is
/// the fee money that passed through their hands in the covered days. Two things are deliberately outside it. Utility
/// collections, electricity and water, are banked separately as additional income. And the days are matched on WHEN THE
/// MONEY WAS TAKEN, never on the day a fee was for: a payor settling days they owed hands the money over now, and matching
/// on the fee day would leave that cash permanently unremittable.
/// </para>
/// </summary>
public interface ICollectorRemittanceRepository
{
    /// <summary>
    /// Fee money this collector took between the two days, inclusive, ignoring voided records.
    ///
    /// <para>
    /// Where a monthly bill carries electricity or water, only the fee part counts. A part payment is applied to the fee
    /// charge first and capped there, so the figure can never claim more fee money than the fees themselves came to; the
    /// remainder belongs to the utilities, which are banked apart.
    /// </para>
    /// </summary>
    Task<decimal> GetFeeCollectionsTotalAsync(Guid collectorId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>What this collector has already remitted for days inside the range. Voided remittances do not count.</summary>
    Task<decimal> GetRemittedTotalAsync(Guid collectorId, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// The first live remittance of this collector whose covered days touch the range, or null. Overlap is refused because
    /// it is the only thing standing between "not yet remitted" being exact and being a guess.
    /// </summary>
    Task<CollectorRemittance?> FindOverlappingAsync(
        Guid collectorId, DateOnly from, DateOnly to, Guid? excludingId = null, CancellationToken ct = default);

    /// <summary>This collector's live remittances whose covered days touch the range, earliest received first.</summary>
    Task<IReadOnlyList<CollectorRemittance>> ListAsync(
        Guid collectorId, DateOnly from, DateOnly to, CancellationToken ct = default);

    Task<CollectorRemittance?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(CollectorRemittance remittance, CancellationToken ct = default);
}
