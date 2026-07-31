using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.AssignPastOccupantStall;

/// <summary>
/// Places a payor whose occupancy has ended into a stall of their own, because the stall they held is now let to
/// somebody else and cannot be renewed in place.
///
/// The previous stall and its account are NOT touched. Any balance stays where it was incurred — under that
/// contract, against the receipts and dates that produced it — and remains collectable. Nothing is transferred.
/// </summary>
/// <param name="PreviousStallId">The stall the payor used to hold; read for its facility, section and terms.</param>
/// <param name="ContractId">
/// The term being continued elsewhere. A re-let stall carries several, so this — not the stall — decides whose
/// details are read; without it the sitting lessee would be the one placed.
/// </param>
/// <param name="StallNo">The number for the new stall. The Head may override the suggestion.</param>
/// <param name="ContractDate">When the new term starts.</param>
/// <param name="ContractYears">How long the new term runs.</param>
/// <param name="MonthlyRate">The rate for the new term; defaults to the previous one on the form.</param>
/// <param name="NameOnContract">The name the contract is written in, if it differs from the occupant's.</param>
public record AssignPastOccupantStallCommand(
    Guid PreviousStallId,
    string StallNo,
    DateTime? ContractDate,
    int ContractYears,
    decimal MonthlyRate,
    string? NameOnContract,
    Guid? ContractId = null) : IRequest<Result<StallDto>>;
