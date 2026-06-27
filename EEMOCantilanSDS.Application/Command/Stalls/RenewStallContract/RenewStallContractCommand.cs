using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.RenewStallContract;

/// <summary>
/// Renews a stall's contract by terminating the current active term and starting a fresh one. The
/// lapsed gap has no active contract, so it is never back-billed — billing resumes from
/// <see cref="EffectivityDate"/>.
/// </summary>
public record RenewStallContractCommand(
    Guid StallId,
    DateOnly EffectivityDate,
    int DurationYears,
    string ActualOccupant,
    string? NameOnContract) : IRequest<Result<bool>>;
