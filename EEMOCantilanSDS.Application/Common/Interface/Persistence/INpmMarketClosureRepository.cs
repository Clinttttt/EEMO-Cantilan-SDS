using EEMOCantilanSDS.Domain.Entities.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface INpmMarketClosureRepository
{
    /// <summary>Tracked lookup of a closure for a date (null if the market was open).</summary>
    Task<NpmMarketClosure?> GetAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>All closure dates within a calendar month (read-only).</summary>
    Task<IReadOnlyList<NpmMarketClosure>> GetByMonthAsync(int year, int month, CancellationToken ct = default);

    Task AddAsync(NpmMarketClosure entity, CancellationToken ct = default);

    void Remove(NpmMarketClosure entity);
}
