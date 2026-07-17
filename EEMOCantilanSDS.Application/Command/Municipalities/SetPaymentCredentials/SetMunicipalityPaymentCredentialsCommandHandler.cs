using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Municipalities.SetPaymentCredentials;

public class SetMunicipalityPaymentCredentialsCommandHandler(
    IAppDbContext context,
    ICurrentUserService currentUser,
    ICredentialProtector protector,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<SetMunicipalityPaymentCredentialsCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetMunicipalityPaymentCredentialsCommand request, CancellationToken ct)
    {
        // A Head may only configure their OWN LGU's account — the target is the caller's municipality.
        if (currentUser.MunicipalityId is not { } municipalityId || municipalityId == Guid.Empty)
            return Result<bool>.Forbidden();

        // Municipality is a global reference table (not tenant-filtered); load by the caller's id.
        var municipality = await context.Municipalities
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == municipalityId, ct);
        if (municipality is null)
            return Result<bool>.NotFound();

        var actor = currentUser.Username ?? "Head";

        if (string.IsNullOrWhiteSpace(request.SecretKey))
        {
            // Empty secret => revert this LGU to the platform default account.
            municipality.ClearPayMongoCredentials(actor);
        }
        else
        {
            var secretEnc = protector.Protect(request.SecretKey.Trim());
            var webhookEnc = string.IsNullOrWhiteSpace(request.WebhookSecret)
                ? null
                : protector.Protect(request.WebhookSecret.Trim());
            var publicKey = string.IsNullOrWhiteSpace(request.PublicKey) ? null : request.PublicKey.Trim();
            municipality.SetPayMongoCredentials(secretEnc, publicKey, webhookEnc, actor);
        }

        await context.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);
        return Result<bool>.Success(true);
    }
}
