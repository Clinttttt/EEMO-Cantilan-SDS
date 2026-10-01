namespace EEMOCantilanSDS.Domain.Constants;

/// <summary>
/// Stable internal semantic identities. These are not official government accounting codes.
/// Tenant-visible names and instrument policy live in effective-dated policy rows.
/// </summary>
public static class RevenueClassificationCodes
{
    public const string PermanentStallRent = "PERMANENT_STALL_RENT";
    public const string Ecf = "ECF";
    public const string FishMeatVendorFee = "FISH_MEAT_VENDOR_FEE";
    public const string WeightAndMeasure = "WEIGHT_AND_MEASURE";
    public const string PenaltiesAndFines = "PENALTIES_AND_FINES";
    public const string Slaughterhouse = "SLAUGHTERHOUSE";
    public const string MarketFees = "MARKET_FEES";
    public const string Tabo = "TABO";
    public const string TransportationParking = "TRANSPORTATION_PARKING";
    public const string VegetableFruitSpaceRental = "VEGETABLE_FRUIT_SPACE_RENTAL";
    public const string Wcf = "WCF";
    public const string LandingBerthing = "LANDING_BERTHING";
    public const string Arrears = "ARREARS";
    public const string TransferLargeCattle = "TRANSFER_LARGE_CATTLE";
    public const string IcePlant = "ICE_PLANT";

    /// <summary>The classification a monthly-rental facility collects under: Ice Plant has its own line, all others are stall rent.</summary>
    public static string ForMonthlyRental(Enums.FacilityCode facility) => facility == Enums.FacilityCode.ICE ? IcePlant : PermanentStallRent;
    public const string KanmanggaySpaceRental = "KANMANGGAY_SPACE_RENTAL";
    public const string FiestaArawLotRental = "FIESTA_ARAW_LOT_RENTAL";
}
