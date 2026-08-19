namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// The outcome of registering an LGU's webhook with PayMongo.
/// </summary>
/// <param name="WebhookId">The webhook's own id (<c>hook_…</c>), so the same one is reused instead of a second being made.</param>
/// <param name="SigningSecret">
/// The signing secret, when PayMongo revealed one. Null when the webhook already existed: PayMongo shows a secret as the
/// webhook is created, and a caller must not treat its absence as "there is no secret" - the one already stored still works.
/// </param>
/// <param name="AlreadyExisted">The webhook was found rather than created, so nothing new was registered.</param>
/// <param name="WasReEnabled">
/// It existed but was disabled and has been switched back on. PayMongo disables a webhook after repeated delivery failures,
/// so this is a normal thing to find rather than an error.
/// </param>
public record PayMongoWebhookRegistration(
    string WebhookId,
    string? SigningSecret,
    bool AlreadyExisted,
    bool WasReEnabled);
