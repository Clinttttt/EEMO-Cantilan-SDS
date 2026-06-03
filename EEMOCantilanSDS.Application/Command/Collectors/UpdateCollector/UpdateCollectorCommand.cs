using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;

public record UpdateCollectorCommand(
    Guid CollectorId,
    string FullName,
    string ContactNumber,
    string Email,
    List<FacilityCode> AssignedFacilities) : IRequest<Result<bool>>;
