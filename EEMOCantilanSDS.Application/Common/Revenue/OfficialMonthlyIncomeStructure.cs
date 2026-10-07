using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// The office Monthly Income statement's rows and groups (IA-050, Rulebook sections 9-13). The hierarchy follows the office
/// statement, never the facilities: a facility only decides the stall-rent row, because the sheet prints NPM, NCC and TCC
/// rent separately. BBQ belongs to Rent and Slaughterhouse has its own section. Other unconfirmed rental facilities
/// are listed under their own heading instead of being placed by guess.
/// </summary>
public static class OfficialMonthlyIncomeStructure
{
    public sealed record Group(string Key, string Label);
    public sealed record Row(string Key, string Label, string GroupKey, string? ClassificationCode, FacilityCode? Facility = null);

    public const string Market = "MARKET";
    public const string Rent = "RENT";
    public const string Space = "SPACE";
    public const string Pending = "PENDING";
    public const string Slaughterhouse = "SLAUGHTERHOUSE";
    public const string Terminal = "TERMINAL";

    public static readonly IReadOnlyList<Group> Groups =
    [
        new(Market, "Income From Market"),
        new(Rent, "Rent Income (Stall Rental)"),
        new(Space, "Space Rental"),
        new(Terminal, "Income From Terminal"),
        new(Slaughterhouse, "Income from Slaughterhouse"),
        new(Pending, "Awaiting an approved official grouping"),
    ];

    public static readonly IReadOnlyList<Row> Rows =
    [
        new("MARKET_FEES", "Market Fees", Market, RevenueClassificationCodes.MarketFees),
        new("ECF", "General Distribution / ECF", Market, RevenueClassificationCodes.Ecf),
        new("WCF", "Water Consumption Fees / WCF", Market, RevenueClassificationCodes.Wcf),
        new("TABO", "Tabo", Market, RevenueClassificationCodes.Tabo),
        new("FISH_MEAT_VENDOR_FEE", "Fish / Meat Vendor Fees", Market, RevenueClassificationCodes.FishMeatVendorFee),
        new("LANDING_BERTHING", "Landing / Berthing", Market, RevenueClassificationCodes.LandingBerthing),
        new("TRANSPORTATION_PARKING", "Transportation / Parking", Market, RevenueClassificationCodes.TransportationParking),
        new("WEIGHT_AND_MEASURE", "Weight & Measure", Market, RevenueClassificationCodes.WeightAndMeasure),
        new("TRANSFER_LARGE_CATTLE", "Transfer Large Cattle", Market, RevenueClassificationCodes.TransferLargeCattle),
        new("ICE_PLANT", "Ice Plant", Market, RevenueClassificationCodes.IcePlant),

        new("RENT_NPM", "New Public Market (NPM)", Rent, RevenueClassificationCodes.PermanentStallRent, FacilityCode.NPM),
        new("RENT_NCC", "New Commercial Center (NCC)", Rent, RevenueClassificationCodes.PermanentStallRent, FacilityCode.NCC),
        new("RENT_TCC", "Tampak Commercial Center (TCC)", Rent, RevenueClassificationCodes.PermanentStallRent, FacilityCode.TCC),
        new("RENT_BBQ", "Barbecue stands (BBQ) rent", Rent, RevenueClassificationCodes.PermanentStallRent, FacilityCode.BBQ),
        new("ARREARS", "Arrears", Rent, RevenueClassificationCodes.Arrears),

        new("VEGETABLE_FRUIT_SPACE_RENTAL", "Vegetable / Fruits", Space, RevenueClassificationCodes.VegetableFruitSpaceRental),
        new("KANMANGGAY_SPACE_RENTAL", "Kanmanggay", Space, RevenueClassificationCodes.KanmanggaySpaceRental),
        new("FIESTA_ARAW_LOT_RENTAL", "Lot Rental - Fiesta / Araw", Space, RevenueClassificationCodes.FiestaArawLotRental),
        new("PENALTIES_AND_FINES", "Fines", Space, RevenueClassificationCodes.PenaltiesAndFines),

        new(RevenueClassificationCodes.TerminalComfortRoom, "COMFORT ROOM", Terminal, RevenueClassificationCodes.TerminalComfortRoom),
        new(RevenueClassificationCodes.TerminalPullPulVansCargoVans, "PULL PUL VANS, CARGO VANS", Terminal, RevenueClassificationCodes.TerminalPullPulVansCargoVans),
        new(RevenueClassificationCodes.TerminalTricycad, "TRICYCAD", Terminal, RevenueClassificationCodes.TerminalTricycad),
        new("SLAUGHTERHOUSE", "Income from Slaughterhouse", Slaughterhouse, RevenueClassificationCodes.Slaughterhouse),
        new("RENT_OTHER", "Rent - other rental facilities", Pending, RevenueClassificationCodes.PermanentStallRent),
    ];

    /// <summary>
    /// The row a piece of cash belongs to. Stall rent is placed by facility (NPM, NCC, TCC, BBQ, other); every other
    /// classification by its own code; a classification the statement does not know is kept under its own pending row so no
    /// money is ever dropped or folded into another line.
    /// </summary>
    public static string RowKeyFor(string classificationCode, FacilityCode? facility)
    {
        if (classificationCode == RevenueClassificationCodes.PermanentStallRent)
            return facility switch
            {
                FacilityCode.NPM => "RENT_NPM",
                FacilityCode.NCC => "RENT_NCC",
                FacilityCode.TCC => "RENT_TCC",
                FacilityCode.BBQ => "RENT_BBQ",
                _ => "RENT_OTHER"
            };
        var known = Rows.FirstOrDefault(x => x.ClassificationCode == classificationCode && x.Facility is null && x.Key != "RENT_OTHER");
        return known?.Key ?? $"OTHER_{classificationCode}";
    }
}
