namespace EEMOCantilanSDS.Domain.Constants;

/// <summary>
/// Stable identities for collector permissions over non-facility operations.
/// These are operation permissions, not revenue classifications or source identities.
/// </summary>
public static class CollectorOperationCodes
{
    public const string Wcf = "WCF";
    public const string VegetableFruitSpaceRental = "VEGETABLE_FRUIT_SPACE_RENTAL";
    public const string LandingBerthing = "LANDING_BERTHING";
    public const string TransferLargeCattle = "TRANSFER_LARGE_CATTLE";
    public const string MarketFees = "MARKET_FEES";
    public const string Transportation = "TRANSPORTATION";

    public static bool IsSupported(string? code) => code is
        Wcf or VegetableFruitSpaceRental or LandingBerthing or TransferLargeCattle or MarketFees or Transportation;
}
