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
    bool ReuseVacatedStall = false,
    /// <summary>
    /// How the space is held. A barbecue stand or an ice-plant space is let without a signed contract at all, and
    /// some commercial-centre spaces are occupied on an extension of a lapsed one. Rent is assessed exactly the
    /// same; what is absent is the leasee name, the term and the contract rate, and the official sheets print
    /// "No contract" for those rows.
    /// </summary>
    OccupancyArrangement Arrangement = OccupancyArrangement.SignedContract) : IRequest<Result<StallDto>>;
