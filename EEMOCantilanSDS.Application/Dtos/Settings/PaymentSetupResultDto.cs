namespace EEMOCantilanSDS.Application.Dtos.Settings;

/// <summary>
/// What happened when an office saved its online-payment credentials.
/// </summary>
/// <param name="Saved">The credentials were stored. This is the part that must not depend on PayMongo being reachable.</param>
/// <param name="WebhookRegistered">
/// PayMongo will now notify us about this LGU's payments, either because a webhook was registered or because one was
/// already there.
/// </param>
/// <param name="Message">
/// Said in the office's own terms, including when only half of it worked.
///
/// <para>
/// The two are reported separately on purpose. Registering a webhook needs PayMongo to answer; storing a key does not, and
/// an office must never lose the key it just pasted because a network call failed afterwards.
/// </para>
/// </param>
public record PaymentSetupResultDto(bool Saved, bool WebhookRegistered, string Message);
