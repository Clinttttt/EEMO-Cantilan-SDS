namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>
/// The authenticated collector's own profile for the mobile Profile page — account fields plus
/// lifetime collection stats. Resolved from the token server-side (never from the request).
/// </summary>
public sealed record MobileCollectorProfileDto(
    string FullName,
    string EmployeeId,
    string ContactNumber,
    string Email,
    decimal TotalCollected,
    int DaysActive,
    int AssignedFacilityCount);
