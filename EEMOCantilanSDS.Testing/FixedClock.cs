using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A clock a test can state. Set to a fixed instant and moved on demand, so a rule about elapsed time can be checked
/// without waiting for it — a lockout that lifts after fifteen minutes was previously unverifiable.
///
/// <para>
/// The Philippine members are derived from the same instant using the production offset, so a test cannot accidentally
/// describe a wall-clock date that disagrees with its own UTC instant.
/// </para>
/// </summary>
public sealed class FixedClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; private set; } = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

    public DateTime PhilippineNow => DateTime.SpecifyKind(UtcNow.Add(PhilippineTime.Offset), DateTimeKind.Unspecified);

    public DateOnly PhilippineToday => DateOnly.FromDateTime(PhilippineNow);

    /// <summary>Moves the clock forward, for asserting what changes once a window has elapsed.</summary>
    public FixedClock Advance(TimeSpan by)
    {
        UtcNow = UtcNow.Add(by);
        return this;
    }
}
