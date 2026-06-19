using EEMOCantilanSDS.Application.Dtos.TaboanMarket;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>
/// The current market-day collection for Tabo-an Public Market (TPM). The market runs only on
/// Fridays at ₱100/vendor; this resolves to today when it is a Friday, otherwise the most recent
/// Friday (view-only). <see cref="IsMarketDay"/> tells the client whether recording is allowed today.
/// </summary>
public sealed record MobileTpmCollectionDto(
    DateOnly MarketDate,
    bool IsMarketDay,
    decimal VendorFee,
    int VendorCount,
    decimal CollectedAmount,
    IReadOnlyList<TpmVendorAttendanceDto> Attendances,
    IReadOnlyList<string> KnownGoods,
    IReadOnlyList<MobileTpmKnownVendorDto> KnownVendors);

/// <summary>A previously-registered Tabo-an vendor, for the mobile "Vendor name" picker. Selecting
/// one reuses the vendor (the Add handler matches by name) and prefills their usual goods.</summary>
public sealed record MobileTpmKnownVendorDto(string Name, string Goods);
