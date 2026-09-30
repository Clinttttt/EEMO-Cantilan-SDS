namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>
/// One cell of the official Monthly Income: the legacy-authoritative cash and the canonical-authoritative cash of one row
/// and month, kept apart so a mixed period can be audited. They are never two views of the same money: a source row that
/// has gone canonical is excluded from the legacy figure, so the total counts every real collection exactly once.
/// </summary>
public sealed record MonthlyIncomeCellDto(decimal Legacy, decimal Canonical)
{
    public decimal Total => Legacy + Canonical;
}

public sealed record OfficialMonthlyIncomeRowDto(
    string Key,
    string Label,
    string? ClassificationCode,
    IReadOnlyList<MonthlyIncomeCellDto> Months,
    MonthlyIncomeCellDto Total,
    // Legacy, Canonical, Mixed or None across the year, so the office sees which rows have crossed the cutover.
    string Authority,
    // Null unless an approved annual target exists for the row. No target is ever invented.
    decimal? AnnualTarget,
    // YTD actual divided by the annual target. Null without a target; it is never a collection-efficiency figure.
    decimal? Attainment);

public sealed record OfficialMonthlyIncomeGroupDto(
    string Key,
    string Label,
    IReadOnlyList<OfficialMonthlyIncomeRowDto> Rows,
    IReadOnlyList<MonthlyIncomeCellDto> MonthTotals,
    MonthlyIncomeCellDto Total);

/// <summary>
/// The official Monthly Income for a year: authoritative legacy pre-cutover cash plus authoritative canonical post-cutover
/// cash, exactly once (IA-051). Rows follow the office statement, not the facilities; an operation whose official grouping
/// is not yet approved is listed apart rather than placed by guess.
/// </summary>
public sealed record OfficialMonthlyIncomeDto(
    int Year,
    int? Month,
    IReadOnlyList<OfficialMonthlyIncomeGroupDto> Groups,
    IReadOnlyList<MonthlyIncomeCellDto> MonthTotals,
    MonthlyIncomeCellDto GrandTotal,
    bool TargetsConfigured,
    IReadOnlyList<string> Notes,
    DateTime GeneratedAtUtc);
