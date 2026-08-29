using EEMOCantilanSDS.Domain.Enums;
using System;

namespace EEMOCantilanSDS.Application.Dtos.Stalls;

/// <param name="DailyRate">
/// The rate this space was LET at, as recorded on the stall itself, and null where none was recorded. It is not
/// necessarily what the stall is billed: a market stall in one of the three areas, or in a section the office has priced,
/// is billed that area's or section's rate. Kept as the stored figure because the forms that EDIT a stall must show what
/// was recorded against it — pre-filling a resolved figure there would stamp it as the stall's own rate, which outranks
/// its section's for ever.
/// </param>
/// <param name="ResolvedDailyFee">
/// What this stall IS billed for a day, as of now: the stall's own rate, then its section's, then its area's, then the
/// market's, settled by <c>NpmDailyFee</c>. Null for a facility that is not billed by the day. Screens that STATE a
/// stall's fee use this; screens that edit the stall use <paramref name="DailyRate"/>.
/// </param>
public record StallDto(
    Guid Id,
    string StallNo,
    StallStatus Status,
    string? ActualOccupant,
    string? NameOnContract,
    double? AreaSqm,
    DateTime? ContractDate,
    decimal MonthlyRate,
    decimal? DailyRate,
    string? ORNumber,
    MarketSection? Section,
    NccAreaLocation? AreaLocation,
    string? AreaNote,
    string? Remarks,
    int ContractYears = 0,
    string? CustomSectionName = null,
    bool HasElectricity = false,
    bool HasWater = false,
    decimal? ResolvedDailyFee = null
);
