namespace EEMOCantilanSDS.Application.Dtos.Settings;

/// <summary>
/// Read-only view of an LGU's online-payment account status. Deliberately excludes the secret + webhook
/// secret (write-only) — it only reports what the office needs to see about its own connection.
/// </summary>
/// <param name="HasOwnAccount">This LGU has configured a PayMongo account of its own.</param>
/// <param name="PublicKey">The non-secret publishable key, when the LGU has its own account.</param>
/// <param name="CanAcceptOnlinePayments">
/// Whether online payments actually work for this LGU. True when it has its own account, or when it is the DEFAULT
/// municipality, whose account the platform's own configuration is.
///
/// <para>
/// Reported separately because "has no account of its own" and "cannot take payments" used to be conflated in the other
/// direction: every LGU was shown a working dashboard on the grounds that an unconfigured one settled to the default
/// account. It did, and that was the defect - the money would have reached another municipality.
/// </para>
/// </param>
/// <param name="HasWebhookSecret">
/// This LGU can authenticate PayMongo's notifications. Without it payments can still be TAKEN, but nothing confirms them
/// unless the payor returns to the portal or the office reconciles - PayMongo's own documentation is explicit that missed
/// events are never re-sent.
/// </param>
/// <param name="WebhookUrl">
/// The address this LGU registers with PayMongo. Composed server-side and PER LGU: the tenant-less endpoint verifies
/// against the platform configuration, which is the DEFAULT municipality's secret, so another LGU pointing at it would
/// have every notification refused. It is shown so nobody has to assemble it by hand.
/// </param>
/// <param name="Mode">"Live", "Test", or null when unknown — read from the key's own prefix, never from a stored copy.</param>
/// <param name="LastVerifiedAtUtc">When the connection was last confirmed against PayMongo. Null until it has been.</param>
public record PaymentSettingsDto(
    bool HasOwnAccount,
    string? PublicKey,
    bool CanAcceptOnlinePayments,
    bool HasWebhookSecret = false,
    string? WebhookUrl = null,
    string? Mode = null,
    DateTime? LastVerifiedAtUtc = null);
