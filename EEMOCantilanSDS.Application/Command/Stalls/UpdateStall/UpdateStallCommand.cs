using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;

public record UpdateStallCommand(
    Guid StallId,
    decimal MonthlyRate,
    /// <summary>
    /// Which charges apply to the space. Null means "not supplied — leave them alone", so a screen that does
    /// not edit the charges cannot strip a meter off the record; a screen that does (the vendor form's utility
    /// charges) sends the whole set it is showing.
    /// </summary>
    ApplicableFees? Fees,
    double? AreaSqm,
    string? AreaNote,
    /// <summary>
    /// The stall's own daily rate. Null means "not supplied — leave the stored rate alone", so a screen
    /// that does not edit the daily rate cannot silently change what a custom-section stall is billed.
    /// </summary>
    decimal? DailyRate,
    string ActualOccupant,
    string? NameOnContract,
    string? Remarks,
    DateTime? ContractDate = null,
    int? ContractYears = null) : IRequest<Result<StallDto>>;
