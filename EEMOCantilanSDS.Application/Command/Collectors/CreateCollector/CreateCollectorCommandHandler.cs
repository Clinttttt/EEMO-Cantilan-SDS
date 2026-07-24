using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;

public class CreateCollectorCommandHandler(
    ICollectorRepository collectorRepo,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) 
    : IRequestHandler<CreateCollectorCommand, Result<CollectorDto>>
{
    public async Task<Result<CollectorDto>> Handle(CreateCollectorCommand request, CancellationToken cancellationToken)
    {
        // Email + contact number are optional. Store blank as NULL so the per-LGU unique (MunicipalityId,
        // Email) index treats "no email" collectors as distinct (Postgres allows multiple NULLs).
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var contactNumber = string.IsNullOrWhiteSpace(request.ContactNumber) ? null : request.ContactNumber.Trim();

        var collector = CollectorUser.Create(
            request.FullName,
            request.EmployeeId,
            request.Username,
            email,
            contactNumber,
            request.Password);

        await collectorRepo.AddAsync(collector, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        await collectorRepo.AddFacilityAssignmentsAsync(collector.Id, request.AssignedFacilities, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, cancellationToken);

        var savedCollector = await collectorRepo.GetByIdAsync(collector.Id, cancellationToken);

        var assignedFacilities = savedCollector?.FacilityAssignments
            .Select(fa => fa.FacilityCode)
            .ToList() ?? new List<FacilityCode>();

        var dto = new CollectorDto(
            collector.Id,
            collector.FullName!,
            collector.EmployeeId!,
            collector.Username!,
            collector.Email ?? string.Empty,
            collector.ContactNumber ?? string.Empty,
            collector.IsActive,
            assignedFacilities);

        return Result<CollectorDto>.Success(dto);
    }
}
