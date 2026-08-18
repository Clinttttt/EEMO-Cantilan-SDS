using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using Microsoft.Extensions.Configuration;

namespace EEMOCantilanSDS.Infrastructure.Payments;

/// <inheritdoc cref="IPayMongoCredentialResolver"/>
public sealed class PayMongoCredentialResolver(
    IMunicipalityRepository municipalityRepository,
    ICurrentMunicipalityAccessor municipalityAccessor,
    ICredentialProtector protector,
    IConfiguration configuration) : IPayMongoCredentialResolver
{
    public async Task<PayMongoCredentials> ResolveAsync(CancellationToken cancellationToken = default)
    {
        // The global configuration is not "the platform's account". It is ONE LGU's merchant account - the default
        // municipality's - and it is only theirs to settle into.
        var globalSecret = configuration["PayMongo:SecretKey"] ?? string.Empty;
        var globalPublic = configuration["PayMongo:PublicKey"];
        var globalWebhook = configuration["PayMongo:WebhookSecret"];

        var municipalityId = municipalityAccessor.MunicipalityId;
        if (municipalityId != Guid.Empty)
        {
            var municipality = await municipalityRepository.GetByIdAsync(municipalityId, cancellationToken);

            if (municipality is { HasOwnPayMongoAccount: true })
            {
                var secret = protector.Unprotect(municipality.PayMongoSecretKeyEnc!);
                var webhook = string.IsNullOrWhiteSpace(municipality.PayMongoWebhookSecretEnc)
                    ? null
                    : protector.Unprotect(municipality.PayMongoWebhookSecretEnc!);
                var publicKey = string.IsNullOrWhiteSpace(municipality.PayMongoPublicKey)
                    ? null
                    : municipality.PayMongoPublicKey;

                return new PayMongoCredentials(secret, publicKey, webhook);
            }

            // An LGU that has not configured an account of its own gets NOTHING, and online payments stay shut for it.
            //
            // This used to fall back to the global configuration for every tenant, which read as a harmless default and
            // was not one: a freshly activated LGU appeared to have working online payments, and its vendors' money
            // would have settled into the DEFAULT LGU's merchant account. Wrong money, wrong municipality, no trace on
            // the LGU that thought it had collected it.
            //
            // The default municipality is the exception, because the global configuration IS its account.
            if (municipality is not null && !municipality.IsDefault)
                return PayMongoCredentials.None;
        }

        // Token-less callers (activation, startup, the webhook before it pins its transaction's LGU) resolve to the
        // default municipality, whose account this is.
        return new PayMongoCredentials(globalSecret, globalPublic, globalWebhook);
    }
}
