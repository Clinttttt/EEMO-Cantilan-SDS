namespace EEMOCantilanSDS.Application.Common.Interface.Time;

/// <summary>
/// The current time, asked for rather than reached for.
///
/// <para>
/// Static clocks made several rules impossible to test honestly: a lockout that expires after fifteen minutes could only be
/// verified by waiting, and a billing rule read from <c>PhilippineTime.Today</c> passes in one month and fails in another.
/// Worse, a test that cannot state "now" tends to be written around whatever today happens to be, which is how a suite
/// starts failing at a month boundary.
/// </para>
///
/// <para>
/// Two kinds of time, deliberately separate, because conflating them shifts values by eight hours. <see cref="UtcNow"/> is
/// an INSTANT and is what persisted timestamps, token expiry and lockout use. <see cref="PhilippineNow"/> and
/// <see cref="PhilippineToday"/> are the office's WALL CLOCK, and are what business-day questions use: which market day it
/// is, whether a term has lapsed, which month a report covers. The Philippines observes no daylight saving, so the offset
/// is fixed.
/// </para>
///
/// <para>
/// Only "now" belongs here. Converting a stored instant to local terms, or bounding a local day or month in UTC, is a pure
/// function of its arguments and stays on <c>PhilippineTime</c> where it can be reasoned about without a clock at all.
/// </para>
/// </summary>
public interface IClock
{
    /// <summary>The current instant, for timestamps, token expiry and lockout.</summary>
    DateTime UtcNow { get; }

    /// <summary>The office's wall-clock time (UTC+8, <see cref="DateTimeKind.Unspecified"/>).</summary>
    DateTime PhilippineNow { get; }

    /// <summary>The office's calendar date — the working day the staff would name.</summary>
    DateOnly PhilippineToday { get; }
}
