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
    ICredentialProtector protector,
    IOnlinePaymentUrlBuilder urlBuilder) : IRequestHandler<GetMunicipalityPaymentSettingsQuery, Result<PaymentSettingsDto>>
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

                // The SAME builder that registers the webhook composes the address shown here, so what the office is told
                // to paste and what this system would register can never differ. It used to be assembled from a path by the
                // controller, which is how an http address reached the screen.
                WebhookUrl: WebhookUrlOrNull(municipality.TenantCode),

                Mode: ModeOf(municipality.PayMongoSecretKeyEnc)));
    }

    /// <summary>
    /// This LGU's webhook address, or null when the server cannot say what its own public address is.
    ///
    /// <para>
    /// Null rather than a throw: an unconfigured public address is a deployment matter, and it must not be the reason a Head
    /// cannot open the payment settings screen at all.
    /// </para>
    /// </summary>
    private string? WebhookUrlOrNull(string tenantCode)
    {
        try { return urlBuilder.BuildWebhookUrl(tenantCode); }
        catch { return null; }
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
