using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.RenewStallContract;

/// <summary>
/// Renews a stall's contract by terminating the current active term and starting a fresh one. The
/// lapsed gap has no active contract, so it is never back-billed — billing resumes from
/// <see cref="EffectivityDate"/>.
///
/// The optional figures are the office's corrections at renewal time: a term is often renewed at a rate the
/// council has since changed, or with the space re-measured. Omitted (null) means "as it stands" — the stall
/// keeps the rate, area and note it already carries, which is what "Proceed" sends.
/// </summary>
public record RenewStallContractCommand(
    Guid StallId,
    DateOnly EffectivityDate,
    int DurationYears,
    string ActualOccupant,
    string? NameOnContract,
    decimal? MonthlyRate = null,
    double? AreaSqm = null,
    string? AreaNote = null) : IRequest<Result<bool>>;
