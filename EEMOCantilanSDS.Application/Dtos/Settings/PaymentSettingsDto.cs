namespace EEMOCantilanSDS.Application.Dtos.Settings;

/// <summary>
/// Read-only view of an LGU's online-payment account status. Deliberately excludes the secret + webhook
/// secret (write-only) — it only reports whether the LGU uses its own PayMongo account and, if so, the
/// non-secret public key.
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
public record PaymentSettingsDto(bool HasOwnAccount, string? PublicKey, bool CanAcceptOnlinePayments);
