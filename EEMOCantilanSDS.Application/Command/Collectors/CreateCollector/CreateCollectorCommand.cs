using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using System.Collections.Generic;

namespace EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;

public record CreateCollectorCommand(
    string FullName,
    string EmployeeId,
    string ContactNumber,
    string Email,
    string Username,
    string Password,
    List<FacilityCode> AssignedFacilities) : IRequest<Result<CollectorDto>>;
