using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;

public record UpdateStallCommand(
    Guid StallId,
    decimal MonthlyRate,
    ApplicableFees Fees,
    double? AreaSqm,
    string? AreaNote,
    decimal? DailyRate,
    string ActualOccupant,
    string? NameOnContract,
    string? Remarks,
    DateTime? ContractDate = null,
    int? ContractYears = null) : IRequest<Result<StallDto>>;
