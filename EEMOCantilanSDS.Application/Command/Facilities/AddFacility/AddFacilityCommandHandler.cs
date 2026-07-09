using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Facilities.AddFacility;

public class AddFacilityCommandHandler(
    IFacilityRepository facilityRepository,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<AddFacilityCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(AddFacilityCommand request, CancellationToken ct)
    {
        if (!Enum.TryParse<FacilityCode>(request.Code, ignoreCase: true, out var code) || !Enum.IsDefined(code))
            return Result<bool>.Failure("Unknown facility type.", 400);

        // One facility per code per tenant. GetByCodeAsync is tenant-scoped, so this only blocks a duplicate
        // within the caller's own LGU (never sees another municipality's facilities).
        var existing = await facilityRepository.GetByCodeAsync(code, ct);
        if (existing is not null)
            return Result<bool>.Failure("This facility is already configured for your municipality.", 409);

        // Billing archetype is derived from the canonical code (keeps the collection/report machinery
        // correct); the Head only names it. MunicipalityId is stamped to the caller's tenant on save.
        var facility = Facility.Create(
            code,
            request.Name.Trim(),
            request.ShortName.Trim(),
            string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim());

        await facilityRepository.AddFacilityAsync(facility, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        return Result<bool>.Success(true);
    }
}
