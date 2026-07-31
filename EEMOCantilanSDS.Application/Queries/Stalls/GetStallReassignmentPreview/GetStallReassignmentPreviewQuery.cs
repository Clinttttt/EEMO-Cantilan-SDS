using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallReassignmentPreview;

/// <summary>
/// Prepares the placement of a payor whose occupancy has ended into a stall of their own — used when the stall
/// they held has since been let to somebody else, so it cannot be renewed in place.
/// </summary>
/// <param name="PreviousStallId">The stall they used to hold. Read only; nothing about it is changed.</param>
/// <param name="ContractId">
/// The term the placement is for. A re-let stall carries several terms, so this — not the stall — decides whose
/// details are read; without it the sitting lessee's would be picked up. Omitted for a stall with a single history.
/// </param>
public record GetStallReassignmentPreviewQuery(Guid PreviousStallId, Guid? ContractId = null) : IRequest<Result<StallReassignmentPreviewDto>>;
