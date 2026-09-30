using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities;

/// <summary>The signed-in collector's non-facility operations and whether each is collectible now. Read-only.</summary>
public sealed record GetCollectorOperationCapabilitiesQuery : IRequest<Result<CollectorOperationCapabilitiesDto>>;
