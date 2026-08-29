using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// What a metered utility's rate FIELD starts at, for an office that has never stated one.
/// </summary>
/// <remarks>
/// <para>
/// Electricity and water are metered: the amount on a bill is a reading times a rate. Where the office has stated no rate
/// the field read nought, which is a poor thing to hand a clerk, because nought is also a real answer meaning "the clerk
/// types the amount on each bill". So the field now opens at one peso, and the office adjusts it to its own ordinance
/// figure before saving.
/// </para>
/// <para>
/// This is a SUGGESTION ON A FORM and must never become anything else. The distinction matters here more than it looks:
/// a rate nobody stated resolving to a figure somebody is billed is the exact fault found when Madrid was charging
/// Cantilan's per-kilo fee. So this class is reachable only from the drawer that the office opens by pressing Edit rates,
/// it puts its figure where the office can see and change it, and nothing is written until the office saves. A resolver,
/// a report, a bill or a settlement asking this class for a figure would be that same fault in a new place.
/// </para>
/// </remarks>
public static class UtilityRateSuggestion
{
    /// <summary>One peso, which is a figure an office recognises as a placeholder and corrects.</summary>
    public const decimal StartingRate = 1.00m;

    /// <summary>The two metered rates, and only those. A daily stall fee or a monthly rent is not metered and is not guessed at.</summary>
    public static bool IsMetered(string key) =>
        Enum.TryParse<FeeRateKey>(key, ignoreCase: true, out var parsed)
        && parsed is FeeRateKey.ElecPerKwh or FeeRateKey.WaterPerCubicMeter;

    /// <summary>
    /// The figure to open the field at, or null to leave the field exactly as the office's record has it.
    /// </summary>
    /// <param name="key">The rate key as the API names it.</param>
    /// <param name="stated">What the office has stated for it, which is nought where it has stated nothing.</param>
    public static decimal? StartingValueOrNull(string key, decimal stated) =>
        IsMetered(key) && stated <= 0m ? StartingRate : null;
}
