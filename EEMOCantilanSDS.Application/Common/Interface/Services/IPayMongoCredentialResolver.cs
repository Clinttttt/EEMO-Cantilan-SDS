using EEMOCantilanSDS.Application.Common.Payments;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

/// <summary>
/// Resolves the PayMongo credentials to use for the CURRENT tenant's online payments.
///
/// <para>
/// An LGU that has configured its own PayMongo account uses its own keys, so its revenue settles to its own account.
/// The DEFAULT municipality uses the global PayMongo configuration, because that configuration is its account. Every
/// other LGU that has not configured one resolves to <see cref="PayMongoCredentials.None"/>, and online payments stay
/// shut for it.
/// </para>
///
/// <para>
/// That last case used to fall back to the global configuration as though it were a platform default. It is one LGU's
/// merchant account, so a freshly activated municipality appeared to have working online payments and its vendors'
/// money would have settled into the default LGU's account.
/// </para>
/// </summary>
public interface IPayMongoCredentialResolver
{
    Task<PayMongoCredentials> ResolveAsync(CancellationToken cancellationToken = default);
}
