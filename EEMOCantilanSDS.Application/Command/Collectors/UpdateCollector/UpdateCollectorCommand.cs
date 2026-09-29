using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;

/// <param name="OperationCodes">
/// Null (older clients) leaves the collector's operation permissions untouched; a list replaces them in the same commit
/// as the profile and facilities. The collector must keep at least one facility or operation.
/// </param>
public record UpdateCollectorCommand(
    Guid CollectorId,
    string FullName,
    string ContactNumber,
    string Email,
    List<FacilityCode> AssignedFacilities,
    string? Username = null,
    List<string>? OperationCodes = null) : IRequest<Result<bool>>;
