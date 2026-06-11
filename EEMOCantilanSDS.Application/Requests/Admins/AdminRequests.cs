namespace EEMOCantilanSDS.Application.Requests.Admins;

public record ToggleAdminStatusRequest(bool IsActive);

public record ResetPasswordRequest(string NewPassword);
