namespace EEMOCantilanSDS.Application.Requests.Notifications;

/// <summary>Admin request to send a push notification to a collector's devices.</summary>
public record SendCollectorNotificationRequest(string Title, string Body);
