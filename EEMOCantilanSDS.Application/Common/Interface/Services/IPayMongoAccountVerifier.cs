namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Asks PayMongo whether an LGU's own account credentials actually work, using a key that has been SUPPLIED rather than
/// one already stored.
///
/// <para>
/// Separate from <see cref="IPaymentGateway"/> on purpose. The gateway transacts for the current tenant and resolves that
/// tenant's stored credentials; this is a question about a key the office is in the middle of entering, before anything is
/// saved. Mixing the two would mean a settings screen could only test what was already committed.
/// </para>
/// </summary>
public interface IPayMongoAccountVerifier
{
    /// <summary>
    /// Confirms the secret key is accepted by PayMongo.
    ///
    /// <para>
    /// Returns success only when PayMongo authenticates the key. A refusal is reported as a failure with a message the
    /// office can act on, and a network problem is reported as such rather than as a bad key - telling somebody their
    /// key is wrong when the internet is down is how a correct key gets replaced.
    /// </para>
    /// </summary>
    Task<Common.Result<bool>> VerifyAsync(string secretKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes sure PayMongo will notify us about this LGU's payments, and reports what it took.
    ///
    /// <para>
    /// Finds an existing webhook for the same address before creating one, so an office that saves its keys twice does not
    /// end up with two webhooks for the same URL - PayMongo would hold both, and nobody could tell which secret signs what.
    /// A webhook it finds disabled is switched back on, because PayMongo disables one after repeated delivery failures and
    /// that is a normal thing to come back to.
    /// </para>
    ///
    /// <para>
    /// The signing secret is only revealed when a webhook is CREATED. Finding one returns no secret, which does not mean
    /// there is none - it means the one already stored is still the right one.
    /// </para>
    /// </summary>
    Task<Common.Result<Common.Payments.PayMongoWebhookRegistration>> EnsureWebhookAsync(
        string secretKey,
        string webhookUrl,
        CancellationToken cancellationToken = default);
}
