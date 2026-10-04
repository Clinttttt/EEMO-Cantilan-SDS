using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public sealed record MobileMenuDto(
    Guid CollectorId,
    string CollectorName,
    string EmployeeId,
    DateOnly Today,
    IReadOnlyList<MobileFacilityMenuItemDto> Facilities,
    MobileBrandingDto? Branding = null,
    /// <summary>
    /// The office's recent weekly market days, most recent first, as the office's own schedule has them. The
    /// device used to work these out itself by counting back Fridays, which was wrong for any office that does
    /// not hold its market on a Friday and could not follow an office that moved its day. Nullable so a menu
    /// cached before this field existed still deserializes; the device then falls back to its old reckoning.
    /// </summary>
    IReadOnlyList<DateOnly>? RecentMarketDates = null);

/// <summary>
/// The signed-in collector's LGU branding, carried on the menu so the mobile header + receipts show the
/// correct municipality (seal/office/name) instead of hardcoded Cantilan. Nullable so a menu cached before
/// this field existed still deserializes (the UI then falls back to the Cantilan defaults).
/// </summary>
public sealed record MobileBrandingDto(
    string MunicipalityName,
    string Province,
    string OfficeName,
    string? OfficeAcronym,
    string? SealPath);
public sealed record MobileFacilityMenuItemDto(
    FacilityCode Code,
    string Name,
    string Description,
    bool IsAssigned,
    bool IsAvailable,
    BillingArchetype Archetype = BillingArchetype.Custom,
    // IA-051/IA-062: true once the Head has enabled this facility's canonical Mobile collection (Tabo for TPM, Slaughterhouse for
    // SLH). The screen then records facts only and the server returns an SRC; no typed OR. False keeps the existing screen
    // (default, so a menu cached before this field existed still deserializes).
    bool CanonicalCollection = false);
