using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Fees;

/// <summary>
/// What an office is told when a charge cannot be raised because it has not stated the rate.
///
/// <para>
/// Said in one place so every path says the same thing, and says what to do about it. The alternative was what
/// the platform used to do: bill the reference municipality's figure and tell the office nothing, which is how
/// Madrid came to charge Cantilan's per-kilo weighing fee on its own vendors.
/// </para>
/// </summary>
public static class FeeRateMessages
{
    /// <summary>Names the rate the office has to set, in the words its own Configuration screen uses.</summary>
    public static string NotStated(FeeRateKey key) =>
        $"This office has not set the {FacilityDisplay.RateLabel(key)} for this facility, so the amount cannot be "
        + "worked out. Set it under Facility Configuration, then record this again.";
}
