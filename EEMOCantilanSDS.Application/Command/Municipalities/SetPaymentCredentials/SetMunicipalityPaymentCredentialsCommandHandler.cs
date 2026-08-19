using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials;

/// <summary>
/// Stores an LGU's own PayMongo credentials and, where it can, registers the webhook for them.
///
/// <para>
/// The order matters and is deliberate: the key is SAVED FIRST, and provisioning is attempted afterwards. An office must
/// never lose the key it just pasted because PayMongo could not be reached a moment later - so a failure there is reported,
/// not rolled back.
/// </para>
/// </summary>
public class SetMunicipalityPaymentCredentialsCommandHandler(
    IAppDbContext context,
    ICurrentUserService currentUser,
    ICredentialProtector protector,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext,
    IPayMongoAccountVerifier accountVerifier,
    IOnlinePaymentUrlBuilder urlBuilder,
    IClock clock) : IRequestHandler<SetMunicipalityPaymentCredentialsCommand, Result<PaymentSetupResultDto>>
{
    public async Task<Result<PaymentSetupResultDto>> Handle(SetMunicipalityPaymentCredentialsCommand request, CancellationToken ct)
    {
        // A Head may only configure their OWN LGU's account — the target is the caller's municipality.
        if (currentUser.MunicipalityId is not { } municipalityId || municipalityId == Guid.Empty)
            return Result<PaymentSetupResultDto>.Forbidden();

        // Municipality is a global reference table (not tenant-filtered); load by the caller's id.
        var municipality = await context.Municipalities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == municipalityId, ct);
        if (municipality is null)
            return Result<PaymentSetupResultDto>.NotFound();

        var actor = currentUser.Username ?? "Head";

        if (string.IsNullOrWhiteSpace(request.SecretKey))
        {
            // Empty secret => this LGU keeps no account of its own. Only the DEFAULT municipality can still take payments
            // afterwards, because the platform configuration is its own account.
            municipality.ClearPayMongoCredentials(actor);

            await context.SaveChangesAsync(ct);
            await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

            return Result<PaymentSetupResultDto>.Success(new PaymentSetupResultDto(
                Saved: true,
                WebhookRegistered: false,
                Message: "This office no longer keeps its own PayMongo account."));
        }

        var secretPlain = request.SecretKey.Trim();
        var secretEnc = protector.Protect(secretPlain);

        // Whether this is the SAME PayMongo account as before decides what happens to the signing secret already stored.
        var accountChanged = !string.Equals(StoredSecret(municipality.PayMongoSecretKeyEnc), secretPlain, StringComparison.Ordinal);

        // A webhook secret the office typed itself is theirs, and provisioning must not overwrite it further down.
        var suppliedWebhook = !string.IsNullOrWhiteSpace(request.WebhookSecret);

        // The signing secret is PRESERVED when the office leaves the field blank and the account has not changed.
        //
        // This field is optional now, because provisioning fills it in - so a Head who re-saves their key without retyping
        // it would otherwise have wiped a working signing secret and had no idea why notifications stopped being believed.
        // When the account HAS changed the old secret is dropped, because it belongs to the other account's webhook and
        // keeping it would let the screen claim a connection that cannot be authenticated.
        var webhookEnc = suppliedWebhook
            ? protector.Protect(request.WebhookSecret!.Trim())
            : accountChanged ? null : municipality.PayMongoWebhookSecretEnc;

        var publicKey = string.IsNullOrWhiteSpace(request.PublicKey) ? null : request.PublicKey.Trim();

        municipality.SetPayMongoCredentials(secretEnc, publicKey, webhookEnc, actor);

        // The registered webhook and the verification stamp describe the PREVIOUS account, so they go with it.
        if (accountChanged)
            municipality.ForgetPayMongoWebhookRegistration(actor);

        await context.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        // Saved. Everything from here is a bonus, and every failure below leaves the key in place.
        var registration = await TryRegisterWebhookAsync(municipality, secretPlain, suppliedWebhook, actor, ct);

        return Result<PaymentSetupResultDto>.Success(registration);
    }

    /// <summary>
    /// The stored secret key in plain form, or null when there is none or it cannot be read.
    ///
    /// <para>
    /// An unreadable stored key is treated as "different", which is the safe direction: the webhook secret beside it is
    /// dropped rather than kept on the assumption that it still matches something nobody can decrypt.
    /// </para>
    /// </summary>
    private string? StoredSecret(string? enc)
    {
        if (string.IsNullOrWhiteSpace(enc)) return null;
        try { return protector.Unprotect(enc!); } catch { return null; }
    }

    /// <summary>
    /// Registers this LGU's webhook so its payments confirm themselves, and records what came back.
    ///
    /// <para>
    /// Every failure is reported as "saved, but" rather than as an error: the office's key is already stored and usable, and
    /// a webhook can be added by hand from the address shown on the same screen.
    /// </para>
    /// </summary>
    private async Task<PaymentSetupResultDto> TryRegisterWebhookAsync(
        Domain.Entities.Tenancy.Municipality municipality,
        string secretPlain,
        bool officeSuppliedItsOwnWebhookSecret,
        string actor,
        CancellationToken ct)
    {
        string webhookUrl;
        try
        {
            webhookUrl = urlBuilder.BuildWebhookUrl(municipality.TenantCode);
        }
        catch (Exception)
        {
            // A misconfigured public address must not look like a bad key, and must not lose the key either.
            return new PaymentSetupResultDto(true, false,
                "Saved. The webhook could not be registered automatically because this server's public address is not " +
                "configured, so add the webhook in PayMongo using the address shown above.");
        }

        var result = await accountVerifier.EnsureWebhookAsync(secretPlain, webhookUrl, ct);

        if (!result.IsSuccess || result.Value is null)
        {
            return new PaymentSetupResultDto(true, false,
                result.Error ?? "Saved. The webhook could not be registered automatically; add it in PayMongo using the address shown above.");
        }

        var registration = result.Value;

        // The signing secret is only stored when PayMongo actually revealed one - and never over a secret the office typed
        // itself. SetPayMongoWebhook refuses to overwrite with a blank for the same reason: replacing a working secret with
        // nothing would quietly stop every notification from being believed.
        var secretToStore = !officeSuppliedItsOwnWebhookSecret && !string.IsNullOrWhiteSpace(registration.SigningSecret)
            ? protector.Protect(registration.SigningSecret!)
            : null;

        municipality.SetPayMongoWebhook(registration.WebhookId, secretToStore, clock.UtcNow, actor);
        await context.SaveChangesAsync(ct);

        // Whether notifications can actually be authenticated: a webhook exists either way, but without a signing secret
        // nothing that arrives through it can be believed.
        var canAuthenticate = municipality.HasPayMongoWebhookSecret;

        var message = canAuthenticate
            ? registration.AlreadyExisted
                ? registration.WasReEnabled
                    ? "Saved. This office's webhook was already registered but switched off, and has been re-enabled."
                    : "Saved. This office's webhook was already registered with PayMongo."
                : "Saved. Online payments now settle to this office's own PayMongo account, and its webhook is registered."
            : "Saved, and the webhook is registered - but PayMongo did not reveal its signing secret, which it only shows " +
              "when a webhook is first created. Copy it from PayMongo into the signing secret field so notifications can " +
              "be authenticated.";

        return new PaymentSetupResultDto(true, canAuthenticate, message);
    }
}
