using EEMOCantilanSDS.Application.Dtos.Payments;
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
    Task<StallLedgerSummaryDto> GetStallLedgerSummaryAsync(Guid stallId, CancellationToken ct);
    Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct);
    Task AddAsync(PaymentRecord payment, CancellationToken ct);
    Task UpdateAsync(PaymentRecord payment, CancellationToken ct);
}
