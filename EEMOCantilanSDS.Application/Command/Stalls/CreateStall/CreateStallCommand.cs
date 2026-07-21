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
    string? CustomSectionName = null) : IRequest<Result<StallDto>>;
