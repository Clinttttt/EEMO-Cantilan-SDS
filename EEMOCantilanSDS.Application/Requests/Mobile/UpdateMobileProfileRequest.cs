namespace EEMOCantilanSDS.Application.Requests.Mobile;

/// <summary>Editable account fields a collector can update on the mobile Profile page.</summary>
public sealed record UpdateMobileProfileRequest(
    string FullName,
    string ContactNumber,
    string Email);
