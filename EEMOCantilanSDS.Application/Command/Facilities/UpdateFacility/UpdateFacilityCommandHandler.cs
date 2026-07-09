using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.UpdateFacility;

public class UpdateFacilityCommandHandler(
    IFacilityRepository facilityRepository,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<UpdateFacilityCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateFacilityCommand request, CancellationToken ct)
    {
        if (!Enum.TryParse<FacilityCode>(request.Code, ignoreCase: true, out var code) || !Enum.IsDefined(code))
            return Result<bool>.Failure("Unknown facility type.", 400);

        // Tenant-scoped: GetByCodeAsync only returns the caller LGU's own facility (tracked, so the edit
        // persists on save). A facility the tenant hasn't configured cannot be edited.
        var facility = await facilityRepository.GetByCodeAsync(code, ct);
        if (facility is null)
            return Result<bool>.NotFound();

        facility.UpdateProfile(request.Name, request.ShortName, request.Description, "FacilityEdit");

        await unitOfWork.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        return Result<bool>.Success(true);
    }
}
