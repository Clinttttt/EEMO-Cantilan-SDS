namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The Total row of a facility's Collection History must add its money columns up, not pick the biggest month.
/// </summary>
/// <remarks>
/// Reported from use on the New Public Market history: August showed ₱1,140.00 outstanding and September ₱840.00, and the Total row
/// said ₱1,140.00 — the LARGER of the two rather than the ₱1,980.00 the office is still owed. All six facility report pages totalled
/// Outstanding with <c>Max</c> while totalling Collected beside it with <c>Sum</c>, so the row contradicted itself on the same line.
///
/// <para><c>FacilityReportsHistoryTests.History_MonthlyOutstanding_AddsUpToTheYearsOwnFigure</c> proves the arithmetic: each monthly
/// row is that month alone, so the months add up to the year. This test guards the PRESENTATION, and exists because the markup is
/// duplicated across six pages — the original fault was in all six, and a fix applied to the one page somebody happened to be looking
/// at would leave five reports quietly understating arrears.</para>
///
/// <para>Read as text, because these are Razor pages that no test project renders.</para>
/// </remarks>
public class FacilityHistoryTotalsTests
{
    /// <summary>Every facility report page carrying a Collection History table.</summary>
    private static readonly string[] ReportPages =
    [
        "NpmReports.razor",
        "BbqReports.razor",
        "IceReports.razor",
        "NccReports.razor",
        "TccReports.razor",
        "CustomReports.razor",
    ];

    [Fact]
    public void NoPageTotalsAMoneyColumnByTakingTheLargestMonth()
    {
        var offenders = ReadPages()
            .Where(p => p.Text.Contains("HistoryMonthlyRows.Max"))
            .Select(p => p.Page)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These pages total a Collection History column with Max, which reports the worst single month as though it were the "
            + "year and understates what the office is owed:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void EveryPageTotalsOutstandingBySummingIt()
    {
        var offenders = ReadPages()
            .Where(p => !p.Text.Contains("Sum(r => r.Outstanding)"))
            .Select(p => p.Page)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These pages do not sum the Outstanding column in their Total row:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Follow-up is a count of PEOPLE and is deliberately not totalled.
    /// </summary>
    /// <remarks>
    /// A payor unpaid in two months is one person, not two, so summing overstates; and the page holds only the monthly counts, so no
    /// true figure can be formed there. It is dashed, exactly as Total Stalls beside it already is. Stated here so that the next
    /// reader who notices the dash does not "fix" it into a sum.
    /// </remarks>
    [Fact]
    public void NoPageTotalsTheFollowUpPayorCount()
    {
        var offenders = ReadPages()
            .Where(p => p.Text.Contains("Sum(r => r.FollowUp)"))
            .Select(p => p.Page)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These pages total the For Follow-up column. It counts people, not pesos: the same payor unpaid in several months would "
            + "be counted once per month:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Every page named here must exist, or the rules above cover nothing.</summary>
    [Fact]
    public void EveryPageNamedHereStillExists()
    {
        var missing = ReportPages.Where(page => Locate(page) is null).ToList();

        Assert.True(missing.Count == 0,
            "These pages are named by this test but no longer exist:\n  " + string.Join("\n  ", missing));
    }

    private static IEnumerable<(string Page, string Text)> ReadPages()
    {
        foreach (var page in ReportPages)
        {
            var path = Locate(page);
            if (path is not null)
                yield return (page, File.ReadAllText(path));
        }
    }

    private static string? Locate(string page)
    {
        var root = Path.Combine(RepositoryRoot(), "EEMOCantilanSDS.Client", "Components", "Pages", "Reports");

        return Directory.Exists(root)
            ? Directory.EnumerateFiles(root, page, SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    /// <summary>Walks up from the test binaries to the solution directory.</summary>
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
