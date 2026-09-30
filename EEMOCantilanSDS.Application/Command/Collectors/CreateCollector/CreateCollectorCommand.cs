using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using System.Collections.Generic;

namespace EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;

/// <param name="OperationCodes">
/// Optional non-facility operation permissions (<c>CollectorOperationCodes</c>) staged in the same commit as the account.
/// Omitted by older clients. A collector needs at least one facility or one operation; an operation-only collector is
/// never given a placeholder facility.
/// </param>
public record CreateCollectorCommand(
    string FullName,
    string EmployeeId,
    string ContactNumber,
    string Email,
    string Username,
    string Password,
    List<FacilityCode> AssignedFacilities,
    List<string>? OperationCodes = null) : IRequest<Result<CollectorDto>>;
