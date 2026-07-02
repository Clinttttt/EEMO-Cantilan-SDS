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

/// <summary>Outcome for a single imported row.</summary>
public record BulkImportRowResult(int RowNumber, string StallNo, string Occupant, bool Created, string? Error);

/// <summary>Summary of a bulk import: valid rows are created, invalid rows are reported per-row.</summary>
public record BulkImportResultDto(
    int TotalRows,
    int CreatedCount,
    int FailedCount,
    IReadOnlyList<BulkImportRowResult> Results);
