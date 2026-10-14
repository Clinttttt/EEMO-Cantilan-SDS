using System;
using System.Collections.Generic;
using System.Linq;

namespace EEMOCantilanSDS.Domain.Constants;

/// <summary>
/// The revenue operations a municipality may LIST in its Head Portal directory and name in its own words, with the stable
/// identity each keeps whatever it is called.
/// </summary>
/// <remarks>
/// <para>
/// This is PRESENTATION configuration. A code here never grants authority to post money: classification, instrument, source
/// registration, Collector assignment and readiness are enforced by their own existing policies, and a tenant's label or
/// listing never feeds any of them. The list is deliberately only the operations that are NOT owned by a facility — stall
/// fees, commercial/barbecue/ice rentals, slaughterhouse, terminal and the weekly market follow the facility a municipality
/// configures, and are not offered twice.
/// </para>
/// <para>
/// <see cref="Entry.LegacyDirectoryLabel"/> is the wording the Head Portal directory printed before tenant configuration
/// existed. It is what a municipality that has not adopted tenant configuration still sees, and what is recorded as its
/// label when an existing municipality adopts configuration, so adopting never renames anything on its screens.
/// </para>
/// </remarks>
public static class TenantOperationCatalog
{
    public const string MarketGroup = "MARKET";
    public const string SpaceGroup = "SPACE";

    public const int MaxLabelLength = 80;

    /// <param name="Code">Stable identity (matches <see cref="CollectorOperationCodes"/> where one exists).</param>
    /// <param name="DefaultLabel">The neutral standard name used where a municipality sets none.</param>
    /// <param name="GroupKey">The official Monthly Income group (see <c>OfficialMonthlyIncomeStructure</c>).</param>
    /// <param name="ClassificationCode">The revenue classification code activity is recorded under, for the "has recorded activity" check only.</param>
    /// <param name="LegacyDirectoryLabel">What the directory printed before tenant configuration existed.</param>
    /// <param name="PolicyNotice">
    /// A rule the platform applies to this operation that was accepted for ONE municipality's office and is not yet configurable per
    /// municipality. Shown, never enforced differently: it tells another municipality's Head to confirm the rule against its own
    /// ordinance. Null when no such rule is known.
    /// </param>
    public sealed record Entry(
        string Code, string DefaultLabel, string GroupKey, string ClassificationCode, string LegacyDirectoryLabel, string? PolicyNotice = null);

    /// <summary>
    /// Signed Kanmanggay space contracts default to three years but retain the actual approved term (2026-10-14 clarification).
    /// It is Cantilan's accepted office policy; for any other
    /// municipality it is an imposed number until a per-municipality duration policy is approved and modelled.
    /// </summary>
    public const string SpaceContractDurationNotice =
        "Signed space contracts use Cantilan's three-year default, with the actual signed term recorded when different. " +
        "That duration rule is not yet configurable per municipality; confirm it matches your ordinance before registering signed contracts.";

    public static readonly IReadOnlyList<Entry> All =
    [
        new(CollectorOperationCodes.FishMeatVendorFee, "Fish / Meat Vendor Fee", MarketGroup, RevenueClassificationCodes.FishMeatVendorFee, "Fish | Meat Vendor Fees"),
        new(CollectorOperationCodes.WeightAndMeasure, "Weight & Measure", MarketGroup, RevenueClassificationCodes.WeightAndMeasure, "Weight & Measure | Registration"),
        new(CollectorOperationCodes.MarketFees, "Market Fees", MarketGroup, RevenueClassificationCodes.MarketFees, "Market Fees"),
        new(CollectorOperationCodes.KanmanggaySpaceRental, "Municipal Space Rental", SpaceGroup, RevenueClassificationCodes.KanmanggaySpaceRental, "Kanmanggay", SpaceContractDurationNotice),
        new(CollectorOperationCodes.FiestaArawLotRental, "Event / Temporary Lot Rental", SpaceGroup, RevenueClassificationCodes.FiestaArawLotRental, "Fiesta | Araw"),
        new(CollectorOperationCodes.VegetableFruitSpaceRental, "Vegetable / Fruit Space Rental", SpaceGroup, RevenueClassificationCodes.VegetableFruitSpaceRental, "Vegetable | Fruits"),
        new("ECF", "Electricity Consumption Fees", MarketGroup, RevenueClassificationCodes.Ecf, "Electricity Consumption Fees"),
        new(CollectorOperationCodes.Wcf, "Water Consumption Fees", MarketGroup, RevenueClassificationCodes.Wcf, "Water Consumption Fees"),
        new(CollectorOperationCodes.LandingBerthing, "Landing / Berthing", MarketGroup, RevenueClassificationCodes.LandingBerthing, "Landing | Berthing"),
        new(CollectorOperationCodes.Transportation, "Transportation / Parking", MarketGroup, RevenueClassificationCodes.TransportationParking, "Transportation | Parking"),
        new(CollectorOperationCodes.TransferLargeCattle, "Transfer Large Cattle", MarketGroup, RevenueClassificationCodes.TransferLargeCattle, "Transfer Large Cattle"),
    ];

    public static Entry? Find(string? code) => All.FirstOrDefault(e => string.Equals(e.Code, code, StringComparison.Ordinal));

    public static bool IsKnown(string? code) => Find(code) is not null;

    /// <summary>
    /// The entry a revenue SOURCE key belongs to: the operation code itself, or the classification code activity is recorded under
    /// (e.g. <c>TRANSPORTATION_PARKING</c> for the <c>TRANSPORTATION</c> operation). Null for anything this catalog does not cover
    /// (facility rent, Tabo, Slaughterhouse, Fines, Arrears), which keeps its own name.
    /// </summary>
    public static Entry? FindBySourceKey(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null
        : All.FirstOrDefault(e => string.Equals(e.Code, key, StringComparison.Ordinal) || string.Equals(e.ClassificationCode, key, StringComparison.Ordinal));

    /// <summary>Trims a label and treats blank as "no label", so the neutral name is used.</summary>
    public static string? NormalizeLabel(string? label)
    {
        var trimmed = label?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Why <paramref name="label"/> cannot name <paramref name="code"/>, or null when it can. A name may not be too long, hold
    /// control characters, read as ANOTHER operation's standard name, or repeat the name another LISTED operation already uses
    /// — the office would otherwise be unable to tell two collections apart on its own screens. An operation may use its own
    /// standard name.
    /// </summary>
    public static string? LabelProblem(string code, string? label, IEnumerable<(string Code, string? Label, bool Listed)> siblings)
    {
        var name = NormalizeLabel(label);
        if (name is null) return null;
        if (name.Length > MaxLabelLength) return $"A name can be at most {MaxLabelLength} characters.";
        if (name.Any(char.IsControl)) return "A name cannot contain control characters.";

        foreach (var other in All)
        {
            if (other.Code != code && string.Equals(other.DefaultLabel, name, StringComparison.OrdinalIgnoreCase))
                return $"“{name}” is the standard name of another operation ({other.DefaultLabel}).";
        }

        foreach (var (otherCode, otherLabel, listed) in siblings)
        {
            if (otherCode != code && listed && string.Equals(NormalizeLabel(otherLabel), name, StringComparison.OrdinalIgnoreCase))
                return $"Another operation already uses the name “{name}”.";
        }

        return null;
    }
}
