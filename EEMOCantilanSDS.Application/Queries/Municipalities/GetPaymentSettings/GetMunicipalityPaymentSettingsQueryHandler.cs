using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Municipalities.GetPaymentSettings;

public class GetMunicipalityPaymentSettingsQueryHandler(
    IAppDbContext context,
    ICurrentUserService currentUser,
    ICredentialProtector protector) : IRequestHandler<GetMunicipalityPaymentSettingsQuery, Result<PaymentSettingsDto>>
{
    public async Task<Result<PaymentSettingsDto>> Handle(GetMunicipalityPaymentSettingsQuery request, CancellationToken ct)
    {
        if (currentUser.MunicipalityId is not { } municipalityId || municipalityId == Guid.Empty)
            return Result<PaymentSettingsDto>.Forbidden();

        var municipality = await context.Municipalities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == municipalityId, ct);
        if (municipality is null)
            return Result<PaymentSettingsDto>.NotFound();

        return Result<PaymentSettingsDto>.Success(
            new PaymentSettingsDto(
                municipality.HasOwnPayMongoAccount,
                municipality.PayMongoPublicKey,
                // The global configuration is the DEFAULT municipality's own merchant account, so only they can take
                // payments without configuring one. Every other LGU has to bring its own, or online payments stay shut.
                CanAcceptOnlinePayments: municipality.HasOwnPayMongoAccount || municipality.IsDefault,
                HasWebhookSecret: municipality.HasPayMongoWebhookSecret,

                // The path only. The API composes the absolute address, because only it knows the origin PayMongo must
                // call. PER LGU by its tenant code: the tenant-less endpoint verifies against the platform configuration,
                // which is the default municipality's secret, so another LGU pointed at it would have every notification
                // refused - the same fault as sharing a webhook secret, arriving by a different route.
                WebhookUrl: $"/api/onlinepayments/webhook/{municipality.TenantCode}",

                Mode: ModeOf(municipality.PayMongoSecretKeyEnc)));
    }

    /// <summary>
    /// Whether the configured key is a live or a test one, read from its own prefix.
    ///
    /// <para>
    /// The key is decrypted only to look at its first characters and is never returned; the caller receives the word
    /// "Live" or "Test". Nothing is stored for this, so no column has to be kept in step with the key it describes - and
    /// an office that pasted a test key into a live portal can see that it did.
    /// </para>
    ///
    /// <para>
    /// Any failure to read it yields null rather than an error: this is a label on a settings screen, and it must not be
    /// the reason the screen cannot load.
    /// </para>
    /// </summary>
    private string? ModeOf(string? secretKeyEnc)
    {
        if (string.IsNullOrWhiteSpace(secretKeyEnc)) return null;

        try
        {
            var key = protector.Unprotect(secretKeyEnc!);
            if (key.StartsWith("sk_live", StringComparison.OrdinalIgnoreCase)) return "Live";
            if (key.StartsWith("sk_test", StringComparison.OrdinalIgnoreCase)) return "Test";
            return null;
        }
        catch
        {
            return null;
        }
    }
}
