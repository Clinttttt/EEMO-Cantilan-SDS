namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Resolves the current tenant's weekly (Tabo-an) market weekday from its Municipality registry record.
/// Defaults to <see cref="System.DayOfWeek.Friday"/> when unset, so Cantilan and the Phase-0 goldens are
/// unchanged; other LGUs configure their own day at activation.
/// </summary>
public interface ITpmMarketDayProvider
{
    Task<DayOfWeek> GetMarketDayAsync(CancellationToken ct = default);
}
