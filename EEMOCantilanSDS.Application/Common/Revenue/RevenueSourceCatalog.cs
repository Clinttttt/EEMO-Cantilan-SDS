using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Analytic metadata for the revenue-source performance register: which operational group a source is read under and which
/// business model describes it (rent is monthly; Vendor Fee is a direct additional fee; Kanmanggay is monthly per space,
/// Market Fees, Landing/Berthing, Transportation and Transfer Large Cattle are paid on service, Lot Rental is per event).
/// </summary>
/// <remarks>
/// This is NOT the official Monthly Income placement — that stays in <see cref="OfficialMonthlyIncomeStructure"/>, and a
/// source whose placement is unapproved is flagged rather than placed. The keys are the official row keys, so every source
/// the statement knows (including an unknown classification kept under OTHER_*) appears here exactly once.
/// </remarks>
public static class RevenueSourceCatalog
{
    public const string MarketGroup = "MARKET";
    public const string RentGroup = "RENT";
    public const string SpaceGroup = "SPACE";
    public const string OtherGroup = "OTHER";
    public const string ReceivableGroup = "RECEIVABLE";
    public const string SlaughterhouseGroup = "SLAUGHTERHOUSE";

    public static readonly IReadOnlyList<(string Key, string Label)> Groups =
    [
        (MarketGroup, "Income from Market"),
        (RentGroup, "Rent / facility operations"),
        (SpaceGroup, "Space rental"),
        (OtherGroup, "Other operations"),
        (SlaughterhouseGroup, "Slaughterhouse"),
        (ReceivableGroup, "Receivables context"),
    ];

    public sealed record Entry(string GroupKey, string Model, FacilityCode? Facility = null);

    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal)
    {
        ["MARKET_FEES"] = new(MarketGroup, RevenueSourceModel.Transactional),
        ["ECF"] = new(MarketGroup, RevenueSourceModel.RecurringObligation),
        ["WCF"] = new(MarketGroup, RevenueSourceModel.RecurringObligation),
        ["TABO"] = new(MarketGroup, RevenueSourceModel.Transactional),
        ["FISH_MEAT_VENDOR_FEE"] = new(MarketGroup, RevenueSourceModel.Transactional),
        ["LANDING_BERTHING"] = new(MarketGroup, RevenueSourceModel.Transactional),
        ["TRANSPORTATION_PARKING"] = new(MarketGroup, RevenueSourceModel.Transactional),
        ["WEIGHT_AND_MEASURE"] = new(MarketGroup, RevenueSourceModel.QuantityService),
        ["TRANSFER_LARGE_CATTLE"] = new(MarketGroup, RevenueSourceModel.Transactional),
        ["ICE_PLANT"] = new(MarketGroup, RevenueSourceModel.RecurringObligation, FacilityCode.ICE),

        ["RENT_NPM"] = new(RentGroup, RevenueSourceModel.RecurringObligation, FacilityCode.NPM),
        ["RENT_NCC"] = new(RentGroup, RevenueSourceModel.RecurringObligation, FacilityCode.NCC),
        ["RENT_TCC"] = new(RentGroup, RevenueSourceModel.RecurringObligation, FacilityCode.TCC),
        ["RENT_BBQ"] = new(RentGroup, RevenueSourceModel.RecurringObligation, FacilityCode.BBQ),
        ["RENT_OTHER"] = new(RentGroup, RevenueSourceModel.RecurringObligation),

        ["VEGETABLE_FRUIT_SPACE_RENTAL"] = new(SpaceGroup, RevenueSourceModel.Transactional),
        ["KANMANGGAY_SPACE_RENTAL"] = new(SpaceGroup, RevenueSourceModel.RecurringObligation),
        ["FIESTA_ARAW_LOT_RENTAL"] = new(SpaceGroup, RevenueSourceModel.EventRental),

        ["PENALTIES_AND_FINES"] = new(OtherGroup, RevenueSourceModel.Transactional),
        ["SLAUGHTERHOUSE"] = new(SlaughterhouseGroup, RevenueSourceModel.QuantityService),

        ["ARREARS"] = new(ReceivableGroup, RevenueSourceModel.Receivable),
    };

    /// <summary>True when the row key has an explicit decision here, rather than the default for an unknown classification.</summary>
    public static bool Knows(string rowKey) => Entries.ContainsKey(rowKey);

    /// <summary>The entry for an official row key; an unknown classification is read as an other operation.</summary>
    public static Entry For(string rowKey) =>
        Entries.TryGetValue(rowKey, out var entry) ? entry : new Entry(OtherGroup, RevenueSourceModel.Transactional);

    public static string GroupLabel(string groupKey) =>
        Groups.FirstOrDefault(g => g.Key == groupKey).Label ?? groupKey;

    /// <summary>A status that describes the source in its own terms: a paid-on-service source is never "billed".</summary>
    public static string StatusFor(string model, decimal collected, bool awaitingPlacement) =>
        awaitingPlacement && collected != 0m ? "Needs official placement"
        : model == RevenueSourceModel.Receivable ? "See Receivables"
        : collected != 0m ? "Active"
        : "Nothing recorded";
}
