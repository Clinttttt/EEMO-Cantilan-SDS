using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IPaymentRepository
{
    Task<PaymentRecord?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PaymentRecordDto?> GetPaymentRecordAsync(Guid stallId, int year, int month, CancellationToken ct);
    Task<IReadOnlyList<FacilityPaymentRecordDto>> GetFacilityPaymentRecordsAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct);
    Task<IReadOnlyList<NpmStallDailyStatusDto>> GetNpmDailyStatusAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct);
    /// <summary>
    /// Fully-paid records whose OR is still blank for the given period — the cash/field "awaiting OR"
    /// queue. Returns monthly records (one per stall) and NPM daily collections (grouped per stall).
    /// Online payments are excluded; they surface via the online awaiting-OR queue.
    /// </summary>
    Task<IReadOnlyList<UnreceiptedPaymentDto>> GetUnreceiptedCashPaymentsAsync(int year, int month, CancellationToken ct);
    /// <summary>
    /// Whole-year variant of <see cref="GetUnreceiptedCashPaymentsAsync"/>: one row per (stall, billing
    /// month) for every blank-OR paid cash/field record in the year. Powers the Follow-up History
    /// "Whole year" Missing-OR aggregation.
    /// </summary>
    Task<IReadOnlyList<UnreceiptedPaymentDto>> GetUnreceiptedCashPaymentsForYearAsync(int year, CancellationToken ct);
    /// <summary>
    /// The stall's UNPAID months with an outstanding balance across the WHOLE contract period (not just
    /// the rolling 12 months, and INCLUDING months with no collection at all) — the source for the
    /// Pay-bill form. NPM synthesises each month's ₱/day obligation (billable days × rate − collected);
    /// <summary>
    /// The stall's UNPAID months with an outstanding balance across the WHOLE contract period (not just
    /// the rolling 12 months, and INCLUDING months with no collection at all) — the source for the
    /// Pay-bill form. NPM synthesises each month's ₱/day obligation (billable days × rate − collected);
    /// monthly facilities use the rent obligation less any partial. Only balance &gt; 0 months are returned.
    /// </summary>
    /// <remarks>
    /// A stall's own history, collection log, ledger totals and outstanding months now live on
    /// <see cref="IStallLedgerQueries"/>. They are reads of one account, and a caller that wants them should not have to
    /// depend on something that can also write payments and rule on receipt numbers.
    /// </remarks>
    Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct);
    /// <summary>
    /// OR availability for an NPM daily-collection receipt: available when unused anywhere in the LGU OR
    /// only already used by daily collections of <paramref name="stallId"/> itself (one receipt covering
    /// several days of the same stall). Rejected when the OR belongs to a different stall or any other module.
    /// </summary>
    Task<bool> IsDailyCollectionOrAvailableForStallAsync(string orNumber, Guid stallId, CancellationToken ct);

    /// <summary>
    /// True when the OR is free to stamp on THIS stall's monthly payment records — one OR may settle
    /// multiple months of the same stall (one "all outstanding" receipt); rejected across a different
    /// stall or another module.
    /// </summary>
    Task<bool> IsMonthlyOrAvailableForStallAsync(string orNumber, Guid stallId, CancellationToken ct);
    Task AddAsync(PaymentRecord payment, CancellationToken ct);
    Task UpdateAsync(PaymentRecord payment, CancellationToken ct);
}
