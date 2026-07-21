using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.RemoveNpmCustomSection;

public class RemoveNpmCustomSectionCommandHandler(
    IFacilityRepository facilityRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<RemoveNpmCustomSectionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RemoveNpmCustomSectionCommand request, CancellationToken ct)
    {
        var name = (request.Name ?? string.Empty).Trim();

        // Guard: a custom section can only be removed when NO stall (active or closed) still uses it —
        // otherwise removing the registry name would orphan those stalls' section.
        var sections = await facilityRepo.GetNpmCustomSectionsAsync(ct);
        var target = sections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (target is { StallCount: > 0 })
            return Result<bool>.Failure(
                $"Cannot remove \"{target.Name}\" — {target.StallCount} stall(s) still use it. Reassign or remove them first.");

        var npm = await facilityRepo.GetByCodeAsync(FacilityCode.NPM, ct);
        if (npm is null) return Result<bool>.NotFound();

        npm.RemoveCustomSection(name, currentUser.Username ?? "Admin");
        await uow.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);
        return Result<bool>.Success(true);
    }
}
