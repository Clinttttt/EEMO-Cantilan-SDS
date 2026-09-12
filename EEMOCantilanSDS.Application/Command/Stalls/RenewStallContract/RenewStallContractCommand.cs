using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
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
/// <para>
/// <see cref="Arrangement"/> is how the renewed occupancy is HELD, not merely a label. Renewal used to write a signed
/// contract always, so an occupant whose term had lapsed and whom the office was letting stay could only be recorded by
/// adding a new vendor — which took a fresh SP- identifier and left the office with two records for one space, its real
/// stall number detached from the arrangement describing it. Renewed as an extension instead, the occupant keeps their
/// stall, the lapsed term is retained as history, the new term is open-ended so it never falls due for renewal again, and
/// the sheet prints "No contract (Extension …)" against the number the office actually uses.
/// </para>
public record RenewStallContractCommand(
    Guid StallId,
    DateOnly EffectivityDate,
    int DurationYears,
    string ActualOccupant,
    string? NameOnContract,
    decimal? MonthlyRate = null,
    double? AreaSqm = null,
    string? AreaNote = null,
    OccupancyArrangement Arrangement = OccupancyArrangement.SignedContract) : IRequest<Result<bool>>;
