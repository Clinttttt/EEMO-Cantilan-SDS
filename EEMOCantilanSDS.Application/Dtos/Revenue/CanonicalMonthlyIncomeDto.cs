namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>
/// The correction-knowledge basis of a canonical cash report (ADR-005 / IA-043). Both attribute a correction to the
/// original Collection's period; they differ only in which recorded events are known. Neither is the Office's official
/// cross-period RCD treatment, which remains unresolved.
/// </summary>
public enum CanonicalReportingBasis
{
    /// <summary>Only Collections and corrections durably recorded on or before the stated cutoff.</summary>
    AsOf = 1,

    /// <summary>Every correction recorded so far, cut off at the instant the query ran.</summary>
    LatestCorrected = 2
}

/// <summary>
/// Read-only classified cash from canonical Collection / CollectionLine / correction evidence only. It is a backend
/// foundation for Monthly Income, not the production report: legacy PaymentRecord, DailyCollection, UtilityBill, TPM,
/// TRM, slaughter and online-provider money is excluded, never merged, and never inferred.
/// </summary>
public sealed record CanonicalMonthlyIncomeDto(
    Guid MunicipalityId,
    int Year,
    int? Month,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    CanonicalReportingBasis Basis,
    DateTime KnowledgeCutoffUtc,
    string DateBasis,
    string SourceCoverage,
    bool LegacySourcesIncluded,
    string CoverageNote,
    string DisplayNameBasis,
    IReadOnlyList<CanonicalMonthlyIncomeRowDto> Rows,
    IReadOnlyList<CanonicalMonthlyIncomeCellDto> MonthTotals,
    CanonicalMonthlyIncomeCellDto GrandTotal,
    IReadOnlyList<CanonicalMonthlyIncomeSourceDto> Sources,
    int CollectionCount,
    int CorrectionCount);

/// <summary>One revenue classification (stable identity, not display text) with canonical evidence in the period.</summary>
public sealed record CanonicalMonthlyIncomeRowDto(
    Guid RevenueClassificationId,
    string SemanticCode,
    string DisplayName,
    bool ClassificationActive,
    IReadOnlyList<CanonicalMonthlyIncomeCellDto> Months,
    CanonicalMonthlyIncomeCellDto Total);

/// <summary>
/// Gross is the original posted line money; the correction effect is the signed sum of linked correction lines known at
/// the cutoff; net = gross + effect. <see cref="CorrectionEffectDatedOutsidePeriod"/> is the part of that effect whose
/// correction effective date lies outside this cell's month (or, for a total, outside the reporting period), kept visible
/// so the pending official cross-period treatment can be decided without re-deriving evidence.
/// </summary>
public sealed record CanonicalMonthlyIncomeCellDto(
    int? Month,
    decimal GrossOriginalCollected,
    decimal CorrectionEffect,
    decimal NetCollected,
    decimal CorrectionEffectDatedOutsidePeriod,
    int LineCount,
    int CorrectionLineCount);

/// <summary>Contribution by canonical source identity, so coverage can be reconciled per source/part.</summary>
public sealed record CanonicalMonthlyIncomeSourceDto(
    string? SourceKind,
    string? SourcePart,
    decimal GrossOriginalCollected,
    decimal CorrectionEffect,
    decimal NetCollected);
