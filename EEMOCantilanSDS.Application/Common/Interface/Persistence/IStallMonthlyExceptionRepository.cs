using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface IStallMonthlyExceptionRepository
{
    /// <summary>Tracked lookup of a stall's exception for a billing month (null if none).</summary>
    Task<StallMonthlyException?> GetAsync(Guid stallId, int year, int month, CancellationToken ct = default);

    /// <summary>All excused billing months for a stall in a given year (read-only).</summary>
    Task<IReadOnlyList<StallMonthlyException>> GetByStallYearAsync(Guid stallId, int year, CancellationToken ct = default);

    Task AddAsync(StallMonthlyException entity, CancellationToken ct = default);

    void Remove(StallMonthlyException entity);
}
