using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Infrastructure.Time;

/// <summary>
/// The real clock. Each member delegates to what the code used to read inline, so behaviour is unchanged by construction —
/// the point of the port is that a test can substitute it, not that "now" is computed differently.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public DateTime PhilippineNow => PhilippineTime.Now;

    public DateOnly PhilippineToday => PhilippineTime.Today;
}
