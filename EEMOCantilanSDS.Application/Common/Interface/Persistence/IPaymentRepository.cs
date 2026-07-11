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
    Task<IReadOnlyList<PaymentHistoryDto>> GetPaymentHistoryAsync(Guid stallId, CancellationToken ct);
    /// <summary>
    /// Cursor-paginated transparency log of a stall's collections, newest first. NPM → recorded daily
    /// collections (paid/absent); monthly facilities → payment records. Cursor is the last row's date.
    /// </summary>
    Task<CursorPagedResult<StallCollectionHistoryRowDto>> GetStallCollectionHistoryAsync(Guid stallId, DateTime? cursor, int pageSize, CancellationToken ct);
    Task<StallLedgerSummaryDto> GetStallLedgerSummaryAsync(Guid stallId, CancellationToken ct);
    /// <summary>
    /// The stall's UNPAID months with an outstanding balance across the WHOLE contract period (not just
    /// the rolling 12 months, and INCLUDING months with no collection at all) — the source for the
    /// Pay-bill form. NPM synthesises each month's ₱/day obligation (billable days × rate − collected);
    /// monthly facilities use the rent obligation less any partial. Only balance &gt; 0 months are returned.
    /// </summary>
    Task<IReadOnlyList<PaymentHistoryDto>> GetOutstandingMonthsAsync(Guid stallId, CancellationToken ct);
    Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct);
    Task AddAsync(PaymentRecord payment, CancellationToken ct);
    Task UpdateAsync(PaymentRecord payment, CancellationToken ct);
}
