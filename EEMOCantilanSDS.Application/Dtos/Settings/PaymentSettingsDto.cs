namespace EEMOCantilanSDS.Application.Dtos.Settings;

/// <summary>
/// Read-only view of an LGU's online-payment account status. Deliberately excludes the secret + webhook
/// secret (write-only) — it only reports whether the LGU uses its own PayMongo account and, if so, the
/// non-secret public key.
/// </summary>
public record PaymentSettingsDto(bool HasOwnAccount, string? PublicKey);
