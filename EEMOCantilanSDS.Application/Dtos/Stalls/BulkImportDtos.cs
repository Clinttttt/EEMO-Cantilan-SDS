using System;
using System.Collections.Generic;

namespace EEMOCantilanSDS.Application.Dtos.Stalls;

/// <summary>One stallholder row submitted from the import preview table.</summary>
public record ImportStallRow(
    int RowNumber,
    string ActualOccupant,
    string? NameOnContract,
    string StallNo,
    DateTime? EffectivityDate,
    int ContractYears,
    double? AreaSqm,
    decimal MonthlyRate,
    decimal? ActualMonthlyRental,
    string? AreaLocation);

/// <summary>
/// Outcome for a single imported row. Exactly one of <see cref="Created"/> (new stall) or
/// <see cref="Renewed"/> (reused an existing expired/closed stall) is true on success; on skip/failure
/// both are false and <see cref="Error"/> explains why (e.g. duplicate live payor, occupied active stall).
/// </summary>
public record BulkImportRowResult(int RowNumber, string StallNo, string Occupant, bool Created, bool Renewed, string? Error);

/// <summary>Summary of a bulk import: new stalls created, expired/closed stalls renewed, rest reported per-row.</summary>
public record BulkImportResultDto(
    int TotalRows,
    int CreatedCount,
    int RenewedCount,
    int FailedCount,
    IReadOnlyList<BulkImportRowResult> Results);
