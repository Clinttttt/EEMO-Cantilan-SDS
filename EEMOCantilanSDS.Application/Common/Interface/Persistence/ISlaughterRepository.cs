using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface ISlaughterRepository
{
    Task<SlaughterTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SlaughterTransactionDto>> GetTransactionsByMonthAsync(int year, int month, CancellationToken ct = default);
    Task<MobileSlaughterCollectionDto> GetMobileSlaughterCollectionAsync(DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<OwnerTransactionGroupDto>> GetGroupedTransactionsByMonthAsync(int year, int month, CancellationToken ct = default);
    Task<OwnerTransactionHistoryDto> GetOwnerTransactionHistoryAsync(string ownerName, int year, int month, CancellationToken ct = default);
    Task<SlaughterOverviewDto> GetOverviewAsync(int year, int month, CancellationToken ct = default);
    Task<SlaughterHistoryDto> GetHistoryAsync(int year, CancellationToken ct = default);
    Task AddAsync(SlaughterTransaction transaction, CancellationToken ct = default);
    Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct = default);
    /// <summary>
    /// Whether <paramref name="orNumber"/> may be used for a slaughter receipt identified by
    /// (<paramref name="ownerName"/>, <paramref name="transactionDate"/>). Returns false if the OR
    /// is used by any other module, or by a slaughter row belonging to a different owner/date
    /// (a separate transaction). The same OR may repeat within the same receipt (multiple animals).
    /// </summary>
    Task<bool> IsORNumberAvailableForReceiptAsync(string orNumber, string ownerName, DateOnly transactionDate, CancellationToken ct = default);
    Task<IReadOnlyList<SlaughterTransaction>> GetTransactionsByOwnerDateORAsync(string ownerName, DateOnly date, string orNumber, CancellationToken ct = default);
    Task RemoveAsync(SlaughterTransaction transaction, CancellationToken ct = default);
    Task<ClientProfileDto?> GetClientProfileAsync(string ownerName, CancellationToken ct = default);
}
