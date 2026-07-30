using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.CreateStall;

public record CreateStallCommand(
    FacilityCode FacilityCode,
    string StallNo,
    decimal MonthlyRate,
    ApplicableFees Fees,
    MarketSection? Section,
    NccAreaLocation? AreaLocation,
    double? AreaSqm,
    string? AreaNote,
    decimal? DailyRate,
    string ActualOccupant,
    string? NameOnContract,
    DateTime? ContractDate,
    int ContractYears,
    string? CustomSectionName = null,
    /// <summary>
    /// True when the office has confirmed that this number's existing stall — vacated by closure or by a lapsed
    /// contract — should take the new occupant. The stall is then reused: its number, its section and its whole
    /// payment and contract history stay as they are, and a new contract term begins on it. A stall that is
    /// still occupied is never reused, whatever this flag says.
    /// </summary>
    bool ReuseVacatedStall = false) : IRequest<Result<StallDto>>;
