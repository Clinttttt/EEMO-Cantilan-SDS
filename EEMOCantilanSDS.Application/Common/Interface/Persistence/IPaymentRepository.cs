using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IPaymentRepository
{
    Task<PaymentRecord?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PaymentRecordDto?> GetPaymentRecordAsync(Guid stallId, int year, int month, CancellationToken ct);
    Task<IReadOnlyList<PaymentHistoryDto>> GetPaymentHistoryAsync(Guid stallId, CancellationToken ct);
    Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct);
    Task AddAsync(PaymentRecord payment, CancellationToken ct);
    Task UpdateAsync(PaymentRecord payment, CancellationToken ct);
}
