using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;

public class CreateCollectorCommandHandler(ICollectorRepository collectorRepo, IUnitOfWork uow) 
    : IRequestHandler<CreateCollectorCommand, Result<CollectorDto>>
{
    public async Task<Result<CollectorDto>> Handle(CreateCollectorCommand request, CancellationToken cancellationToken)
    {
        var collector = CollectorUser.Create(
            request.FullName,
            request.EmployeeId,
            request.Username,
            request.Email,
            request.ContactNumber,
            request.Password);

        await collectorRepo.AddAsync(collector, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        await collectorRepo.AddFacilityAssignmentsAsync(collector.Id, request.AssignedFacilities, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);

        var savedCollector = await collectorRepo.GetByIdAsync(collector.Id, cancellationToken);

        var assignedFacilities = savedCollector?.FacilityAssignments
            .Select(fa => fa.FacilityCode)
            .ToList() ?? new List<FacilityCode>();

        var dto = new CollectorDto(
            collector.Id,
            collector.FullName!,
            collector.EmployeeId!,
            collector.Username!,
            collector.Email!,
            collector.ContactNumber!,
            collector.IsActive,
            assignedFacilities);

        return Result<CollectorDto>.Success(dto);
    }
}
