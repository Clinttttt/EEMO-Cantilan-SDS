namespace EEMOCantilanSDS.Application.Requests.Mobile;

/// <summary>Mobile request to register this device's FCM push token for the signed-in collector.</summary>
public record RegisterDeviceTokenRequest(string Token, string Platform);
