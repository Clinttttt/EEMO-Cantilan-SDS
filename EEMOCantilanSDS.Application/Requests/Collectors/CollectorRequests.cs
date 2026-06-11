namespace EEMOCantilanSDS.Application.Requests.Collectors;

public record ToggleCollectorStatusRequest(bool IsActive);

public record ResetCollectorPasswordRequest(string NewPassword);
