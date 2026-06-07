using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface ISlaughterRepository
{
    Task<SlaughterTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<SlaughterTransactionDto>> GetTransactionsByMonthAsync(int year, int month, CancellationToken ct = default);
    Task<IReadOnlyList<OwnerTransactionGroupDto>> GetGroupedTransactionsByMonthAsync(int year, int month, CancellationToken ct = default);
    Task<OwnerTransactionHistoryDto> GetOwnerTransactionHistoryAsync(string ownerName, int year, int month, CancellationToken ct = default);
    Task<SlaughterOverviewDto> GetOverviewAsync(int year, int month, CancellationToken ct = default);
    Task<SlaughterHistoryDto> GetHistoryAsync(int year, CancellationToken ct = default);
    Task AddAsync(SlaughterTransaction transaction, CancellationToken ct = default);
    Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct = default);
    Task<IReadOnlyList<SlaughterTransaction>> GetTransactionsByOwnerDateORAsync(string ownerName, DateOnly date, string orNumber, CancellationToken ct = default);
    Task RemoveAsync(SlaughterTransaction transaction, CancellationToken ct = default);
    Task<ClientProfileDto?> GetClientProfileAsync(string ownerName, CancellationToken ct = default);
}
