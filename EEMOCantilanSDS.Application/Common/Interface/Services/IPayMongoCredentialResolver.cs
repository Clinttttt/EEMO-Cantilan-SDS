using EEMOCantilanSDS.Application.Common.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Resolves the PayMongo credentials to use for the CURRENT tenant's online payments. A tenant that has
/// configured its own PayMongo account uses its own keys (so its revenue settles to its own account); every
/// other tenant — including the default LGU (Cantilan) — falls back to the global PayMongo configuration,
/// keeping the primary account's behaviour byte-for-byte unchanged.
/// </summary>
public interface IPayMongoCredentialResolver
{
    Task<PayMongoCredentials> ResolveAsync(CancellationToken cancellationToken = default);
}
