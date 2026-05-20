using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IDailyCollectionRepository
{
    Task<DailyCollection?> GetByStallAndDateAsync(Guid stallId, DateOnly collectionDate, CancellationToken ct = default);
    Task<IReadOnlyList<DailyCollection>> GetByStallAndMonthAsync(Guid stallId, int year, int month, CancellationToken ct = default);
    Task AddAsync(DailyCollection dailyCollection, CancellationToken ct = default);
}
