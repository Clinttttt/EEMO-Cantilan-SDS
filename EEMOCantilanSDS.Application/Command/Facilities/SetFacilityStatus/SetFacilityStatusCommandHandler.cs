using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.SetFacilityStatus;

public class SetFacilityStatusCommandHandler(
    IFacilityRepository facilityRepository,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<SetFacilityStatusCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SetFacilityStatusCommand request, CancellationToken ct)
    {
        if (!Enum.TryParse<FacilityCode>(request.Code, ignoreCase: true, out var code) || !Enum.IsDefined(code))
            return Result<bool>.Failure("Unknown facility type.", 400);

        var facility = await facilityRepository.GetByCodeAsync(code, ct);
        if (facility is null)
            return Result<bool>.NotFound();

        if (request.Active) facility.Activate();
        else facility.Deactivate();

        await unitOfWork.SaveChangesAsync(ct);
        // Reference data (sidebar/dashboard facility lists) changes — refresh the tenant's cached views.
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        return Result<bool>.Success(true);
    }
}
