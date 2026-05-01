using EEMOCantilanSDS.Domain.Enums;
using System;

namespace EEMOCantilanSDS.Application.Dtos.Stalls;

public record StallDto(
    Guid Id,
    string StallNo,
    StallStatus Status,
    string? ActualOccupant,
    string? NameOnContract,
    double? AreaSqm,
    DateTime? ContractDate,
    decimal MonthlyRate,
    string? ORNumber,
    MarketSection? Section,
    NccAreaLocation? AreaLocation,
    string? AreaNote,
    string? Remarks
);
