namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Builds the payor-portal return URLs the gateway redirects to after checkout. Kept server-side
/// (configured) rather than accepted from the client to avoid open-redirect risk.
/// </summary>
public interface IOnlinePaymentUrlBuilder
{
    string BuildSuccessUrl(string reference);
    string BuildCancelUrl(string reference);
}
