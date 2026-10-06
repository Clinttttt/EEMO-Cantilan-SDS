namespace EEMOCantilanSDS.Domain.Constants;

/// <summary>
/// Stable identities for collector permissions over non-facility operations.
/// These are operation permissions, not revenue classifications or source identities.
/// </summary>
public static class CollectorOperationCodes
{
    public const string Wcf = "WCF";
    public const string KanmanggaySpaceRental = "KANMANGGAY_SPACE_RENTAL";
    public const string FiestaArawLotRental = "FIESTA_ARAW_LOT_RENTAL";
    public const string VegetableFruitSpaceRental = "VEGETABLE_FRUIT_SPACE_RENTAL";
    public const string LandingBerthing = "LANDING_BERTHING";
    public const string TransferLargeCattle = "TRANSFER_LARGE_CATTLE";
    public const string MarketFees = "MARKET_FEES";
    public const string Transportation = "TRANSPORTATION";
    public const string Tabo = "TABO";
    public const string Slaughterhouse = "SLAUGHTERHOUSE";
    public const string NpmDaily = "NPM_DAILY";

    /// <summary>
    /// Tabo, Slaughterhouse and the NPM daily stall fee are facility-assigned (TPM / SLH / NPM), not operation-assigned, so they are deliberately NOT in
    /// <see cref="IsSupported"/> (that gates collector operation assignments). They are governed services only for their
    /// prospective canonical-collection switch.
    /// </summary>
    public static bool IsFeeSchedule(string? code) => code is Tabo or Slaughterhouse or NpmDaily;

    public static bool IsSupported(string? code) => code is
        Wcf or VegetableFruitSpaceRental or LandingBerthing or TransferLargeCattle or MarketFees or Transportation or
        KanmanggaySpaceRental or FiestaArawLotRental;

    public static bool IsSpaceObligation(string? code) => code is KanmanggaySpaceRental or FiestaArawLotRental;
}
