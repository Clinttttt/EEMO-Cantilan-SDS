using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// One line of the reconciliation between the Monthly Collection Report's account-level detail (the facility
/// aggregation) and the official source projection for the same period.
/// </summary>
/// <param name="Label">The source the line describes.</param>
/// <param name="Detail">What the account-level detail states for it.</param>
/// <param name="Official">What the official projection states for the same source or sources.</param>
/// <param name="Scope">Why the two are comparable, or the documented reason they cover different money.</param>
/// <param name="GroupKey">The report group the line reads under, so it narrows with the rest of the view.</param>
/// <param name="SourceKeys">The official rows the line is compared with.</param>
public sealed record MonthlyReconciliationRowDto(
    string Label, decimal Detail, decimal Official, string Scope, string GroupKey, IReadOnlyList<string> SourceKeys)
{
    public decimal Difference => Detail - Official;
    public bool Agrees => Math.Abs(Difference) < 0.005m;
}

/// <summary>
/// The reconciliation as a whole. <see cref="OfficialTotal"/> is the official all-source collection for the period;
/// <see cref="WithoutDetail"/> is the part of it that belongs to sources which hold no account-level detail (direct and
/// event services), so every peso of the headline figure is accounted for as either itemised or source-total-only.
/// </summary>
public sealed record MonthlyReconciliationDto(
    IReadOnlyList<MonthlyReconciliationRowDto> Rows, decimal OfficialTotal, decimal OfficialWithDetail, decimal WithoutDetail)
{
    public bool AllAgree => Rows.All(r => r.Agrees);
}

/// <summary>
/// The Monthly Collection Report's organization and reconciliation rules, kept apart from any page so they can be tested.
/// The report is organized by the office's revenue groups, with the official source projection as the single source of
/// truth for money; the facility aggregation supplies only account-level evidence. Nothing here adds a second collection
/// calculation, and nothing re-sums facility subtotals into a total.
/// </summary>
/// <remarks>
/// Group placement follows the office's own Monthly Income sheet, which is how the office reads this report: Fines are printed
/// under Space Rental and recovered Arrears under Rent Income, so they read there rather than under Others. The analytic source
/// catalog (<see cref="RevenueSourceCatalog"/>) groups them differently, and it is left as it is — placement here is a
/// presentation rule of this report only, the rows and amounts are identical, so every total reconciles.
/// </remarks>
public static class MonthlyCollectionReport
{
    public const string All = "all";

    /// <summary>The report's groups, in the order the office reads them.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Groups =
    [
        (RevenueSourceCatalog.MarketGroup, "Income From Market"),
        (RevenueSourceCatalog.RentGroup, "Rent Income — Stall Rental"),
        (RevenueSourceCatalog.SpaceGroup, "Space Rental"),
        (RevenueSourceCatalog.TerminalGroup, "Income From Terminal"),
        (RevenueSourceCatalog.SlaughterhouseGroup, "Income From Slaughterhouse"),
        (RevenueSourceCatalog.OtherGroup, "Others"),
    ];

    /// <summary>The row keys the office's Monthly Income sheet prints outside the group the source catalog gives them.</summary>
    public const string FinesKey = "PENALTIES_AND_FINES";
    public const string ArrearsKey = "ARREARS";

    /// <summary>
    /// The report group a catalog group is shown under. A receivable that is not named by the office's sheet still reads under
    /// Others, so nothing is dropped.
    /// </summary>
    public static string DisplayGroup(string catalogGroupKey) =>
        catalogGroupKey == RevenueSourceCatalog.ReceivableGroup ? RevenueSourceCatalog.OtherGroup : catalogGroupKey;

    /// <summary>
    /// The report group a source reads under: the office's own placement first — Fines under Space Rental, recovered Arrears
    /// under Rent Income — then the source's catalog group.
    /// </summary>
    public static string DisplayGroup(RevenueSourcePerformanceRowDto row) => row.Key switch
    {
        FinesKey => RevenueSourceCatalog.SpaceGroup,
        ArrearsKey => RevenueSourceCatalog.RentGroup,
        _ => DisplayGroup(row.GroupKey),
    };

    public static string GroupLabel(string key) => Groups.FirstOrDefault(g => g.Key == key).Label ?? key;

    /// <summary>
    /// Whether a source belongs on the report. Arrears is a receivable: it is listed only when it holds actual recovered
    /// money, never as an empty line that could be read as an outstanding balance.
    /// </summary>
    public static bool IsListed(RevenueSourcePerformanceRowDto row) =>
        row.Model != RevenueSourceModel.Receivable || row.Collected != 0m;

    public static IEnumerable<RevenueSourcePerformanceRowDto> ListedRows(RevenueSourcePerformanceDto official) =>
        official.Rows.Where(IsListed);

    private static readonly FacilityCode[] CustomFacilities =
        [FacilityCode.Custom1, FacilityCode.Custom2, FacilityCode.Custom3, FacilityCode.Custom4, FacilityCode.Custom5];

    public static bool IsCustom(FacilityCode code) => CustomFacilities.Contains(code);

    /// <summary>The report group an account-detail facility reads under.</summary>
    public static string GroupOf(FacilityCode code) => code switch
    {
        FacilityCode.ICE or FacilityCode.TPM => RevenueSourceCatalog.MarketGroup,
        FacilityCode.SLH => RevenueSourceCatalog.SlaughterhouseGroup,
        FacilityCode.TRM => RevenueSourceCatalog.TerminalGroup,
        _ => RevenueSourceCatalog.RentGroup,
    };

    /// <summary>The official row keys an account-detail facility corresponds to.</summary>
    public static IReadOnlyList<string> RowKeysOf(FacilityCode code) => code switch
    {
        FacilityCode.NPM => ["RENT_NPM"],
        FacilityCode.TCC => ["RENT_TCC"],
        FacilityCode.NCC => ["RENT_NCC"],
        FacilityCode.BBQ => ["RENT_BBQ"],
        FacilityCode.ICE => ["ICE_PLANT"],
        FacilityCode.TPM => ["TABO"],
        FacilityCode.SLH => ["SLAUGHTERHOUSE"],
        FacilityCode.TRM => ["TERMINAL_COMFORT_ROOM", "TERMINAL_PULL_PUL_VANS_CARGO_VANS", "TERMINAL_TRICYCAD"],
        _ => ["RENT_OTHER"],
    };

    /// <summary>True when the facility's account detail is the detail of the selected official source.</summary>
    public static bool DetailBelongsTo(FacilityCode code, string sourceKey) => RowKeysOf(code).Contains(sourceKey);

    /// <summary>
    /// Compares each account-level detail figure with the official projection for the same period. Where they differ, the
    /// difference is stated, never absorbed: it is evidence that the two read different money (for example fees the facility
    /// aggregation includes but the statement classifies separately) and needs the office's attention.
    /// </summary>
    public static MonthlyReconciliationDto Reconcile(MonthEndReportDto detail, RevenueSourcePerformanceDto official)
    {
        decimal Official(IEnumerable<string> keys, bool legacyOnly = false) =>
            official.Rows.Where(r => keys.Contains(r.Key)).Sum(r => legacyOnly ? r.LegacyCollected : r.Collected);

        var rows = new List<MonthlyReconciliationRowDto>();

        foreach (var facility in detail.Facilities.Where(f => !IsCustom(f.Code)).OrderBy(f => f.Code))
        {
            var keys = RowKeysOf(facility.Code);
            if (facility.Code == FacilityCode.TRM)
                rows.Add(new(facility.Name + " — legacy trip records", facility.Collected, Official(keys, legacyOnly: true),
                    "Compared with the legacy part of the official Terminal rows. Terminal cash recorded since cutover is itemised in the source rows, not here.",
                    GroupOf(facility.Code), keys));
            else
                rows.Add(new(facility.Name, facility.Collected, Official(keys), "Account detail compared with the official source row.",
                    GroupOf(facility.Code), keys));
        }

        var custom = detail.Facilities.Where(f => IsCustom(f.Code)).ToList();
        if (custom.Count > 0)
            rows.Add(new("Other rental facilities", custom.Sum(f => f.Collected), Official(["RENT_OTHER"]),
                "Configured rental facilities compared with the official row for other rental facilities.",
                RevenueSourceCatalog.RentGroup, ["RENT_OTHER"]));

        // The official money the account detail itemises is exactly what each comparison measured against (for legacy Terminal
        // trips, only the legacy part). Everything else — direct and event sources, and Terminal cash recorded since cutover —
        // is stated by its source total alone, so the headline figure is fully accounted for in two parts.
        var withDetail = rows.Sum(r => r.Official);
        return new MonthlyReconciliationDto(rows, official.TotalCollected, withDetail, official.TotalCollected - withDetail);
    }
}
