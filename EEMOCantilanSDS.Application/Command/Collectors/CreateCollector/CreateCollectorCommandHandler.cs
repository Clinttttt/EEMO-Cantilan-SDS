using EEMOCantilanSDS.Application.Common.Interface.Security;
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
    ITenantContext tenantContext,
    IPasswordHasher passwordHasher)
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
            passwordHasher.Hash(request.Password));

        await collectorRepo.AddAsync(collector, cancellationToken);

        await collectorRepo.AddFacilityAssignmentsAsync(collector.Id, request.AssignedFacilities, cancellationToken);

        // ONE commit for the account and the facilities it is assigned to. Saving the account first left a window in
        // which a failure produced a collector who could sign in but was assigned nowhere — their app would open with no
        // facility to collect for, which reads as a broken account rather than an incomplete one. The assignment lookup
        // reads FACILITIES, not the new collector, so it does not need the account to be persisted first.
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
