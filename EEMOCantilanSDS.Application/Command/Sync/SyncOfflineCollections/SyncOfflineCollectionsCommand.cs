using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;

/// <summary>
/// Replays a batch of queued offline collections for the authenticated collector. Each operation is
/// idempotent (by its client operation id) and is dispatched through the existing validated command
/// for its facility, using the offline business date and OR (OR-on-sync). Returns a per-item outcome.
/// </summary>
public sealed record SyncOfflineCollectionsCommand(IReadOnlyList<SyncOfflineOperationDto> Operations)
    : IRequest<Result<SyncOfflineCollectionsResultDto>>;
