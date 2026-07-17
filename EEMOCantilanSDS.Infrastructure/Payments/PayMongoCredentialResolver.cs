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
        // Global config = the primary/default account (Cantilan). It is the fallback for every tenant that
        // has not configured its own PayMongo account.
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
                    ? globalWebhook
                    : protector.Unprotect(municipality.PayMongoWebhookSecretEnc!);
                var publicKey = string.IsNullOrWhiteSpace(municipality.PayMongoPublicKey)
                    ? globalPublic
                    : municipality.PayMongoPublicKey;

                return new PayMongoCredentials(secret, publicKey, webhook);
            }
        }

        return new PayMongoCredentials(globalSecret, globalPublic, globalWebhook);
    }
}
