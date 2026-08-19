namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Builds the payor-portal return URLs the gateway redirects to after checkout. Kept server-side
/// (configured) rather than accepted from the client to avoid open-redirect risk.
/// </summary>
public interface IOnlinePaymentUrlBuilder
{
    string BuildSuccessUrl(string reference);
    string BuildCancelUrl(string reference);

    /// <summary>
    /// The absolute address PayMongo should send an LGU's notifications to, for that LGU alone.
    ///
    /// <para>
    /// PER TENANT, and that is the whole point: the tenant-less webhook endpoint verifies against the platform
    /// configuration, which is the DEFAULT municipality's signing secret, so any other LGU pointed at it would have every
    /// notification refused. Composed here rather than by a caller so there is one place that knows the shape.
    /// </para>
    /// </summary>
    string BuildWebhookUrl(string tenantCode);
}
