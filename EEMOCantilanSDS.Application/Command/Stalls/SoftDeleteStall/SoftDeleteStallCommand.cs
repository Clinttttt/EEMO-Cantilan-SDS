using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.SoftDeleteStall;

/// <summary>
/// Removes an INACTIVE stall account (closed or expired) from the active roster. Soft-delete only:
/// the row and its history are retained but hidden, and the stall NUMBER is freed for reuse. Guarded
/// so a currently-active/covered stall can never be removed here. Used to clear closed/expired
/// accounts (e.g. a bad test import) so a facility/section can start fresh.
/// </summary>
public record SoftDeleteStallCommand(Guid StallId) : IRequest<Result<bool>>;
