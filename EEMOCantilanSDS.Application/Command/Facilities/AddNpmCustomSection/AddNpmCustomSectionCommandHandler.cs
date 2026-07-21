using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.AddNpmCustomSection;

public class AddNpmCustomSectionCommandHandler(
    IFacilityRepository facilityRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<AddNpmCustomSectionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(AddNpmCustomSectionCommand request, CancellationToken ct)
    {
        var npm = await facilityRepo.GetByCodeAsync(FacilityCode.NPM, ct);
        if (npm is null) return Result<bool>.NotFound();

        // Idempotent: AddCustomSection is a no-op if the (trimmed, case-insensitive) name already exists.
        npm.AddCustomSection(request.Name, currentUser.Username ?? "Admin");
        await uow.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);
        return Result<bool>.Success(true);
    }
}
