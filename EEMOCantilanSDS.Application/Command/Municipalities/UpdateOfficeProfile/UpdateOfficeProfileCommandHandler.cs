using System;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Municipalities.UpdateOfficeProfile
{
    public class UpdateOfficeProfileCommandHandler(
        IAppDbContext context,
        ICurrentUserService currentUser,
        IEemoCacheInvalidator cacheInvalidator,
        ITenantContext tenantContext) : IRequestHandler<UpdateOfficeProfileCommand, Result<bool>>
    {
        public async Task<Result<bool>> Handle(UpdateOfficeProfileCommand request, CancellationToken ct)
        {
            // Only edit one's own LGU — the target is the caller's municipality from the token.
            if (currentUser.MunicipalityId is not { } municipalityId || municipalityId == Guid.Empty)
                return Result<bool>.Forbidden();

            // Municipality is a global reference table (not tenant-filtered); load by the caller's id.
            var municipality = await context.Municipalities
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.Id == municipalityId, ct);
            if (municipality is null)
                return Result<bool>.NotFound();

            municipality.ApplyOnboardingProfile(
                request.OfficeName, request.Address, request.SealPath, request.OfficeAcronym, currentUser.Username ?? "Head");

            await context.SaveChangesAsync(ct);
            await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

            return Result<bool>.Success(true);
        }
    }
}
