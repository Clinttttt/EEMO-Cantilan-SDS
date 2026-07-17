namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// The PayMongo credentials to use for a given tenant's online payments. Resolved per-request: a tenant
/// with its own configured account uses its keys; every other tenant (incl. the default LGU, Cantilan)
/// falls back to the global PayMongo configuration so the primary account keeps working unchanged.
/// </summary>
public sealed record PayMongoCredentials(string SecretKey, string? PublicKey, string? WebhookSecret);
