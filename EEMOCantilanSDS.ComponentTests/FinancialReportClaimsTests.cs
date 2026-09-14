using System.Text.RegularExpressions;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// Two things the Financial Report says about itself that must stay true of what it does.
///
/// <para>
/// Checked against the source rather than by rendering the page. Both are statements the markup makes about behaviour
/// defined elsewhere — in the print stylesheet, and in a domain constant — so what matters is that the two agree, which a
/// render of either one alone cannot show.
/// </para>
/// </summary>
public class FinancialReportClaimsTests
{
    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("EEMOCantilanSDS.slnx").Any())
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static string ReadReport(string extension) =>
        File.ReadAllText(Path.Combine(
            RepositoryRoot().FullName,
            "EEMOCantilanSDS.Client", "Components", "Pages", "Menus", $"Report.razor{extension}"));

    [Fact]
    public void TheExportButtonSaysItPrintsASummary_BecauseTwoSectionsAreLeftOut()
    {
        var markup = ReadReport(string.Empty);
        var css = ReadReport(".css");

        // The print stylesheet keeps only the cards marked pdf-include …
        Assert.Contains(".content-area > .section-card:not(.pdf-include)", css);

        // … and neither the Attention section nor Recent Collection Records is marked, so neither is printed. If either
        // ever gains pdf-include, the button is exporting more than a summary and this should be revisited.
        // Matched on the whole opening tag rather than assuming class comes first: adding an id attribute ahead of it
        // silently stopped an earlier version of this assertion from finding the section at all.
        var attention = Regex.Match(markup, @"<div[^>]*rpt-attention-wrap[^>]*>");
        Assert.True(attention.Success, "expected the Attention section to still carry rpt-attention-wrap");
        Assert.DoesNotContain("pdf-include", attention.Value);

        Assert.Contains("Export Summary PDF", markup);
        Assert.DoesNotContain(">Export PDF<", markup);
    }

    [Fact]
    public void TheAttentionNotesStateTheOfficesThreshold_RatherThanANumberTypedIntoTheMarkup()
    {
        var markup = ReadReport(string.Empty);

        // "3 or more unpaid months" and "1–2 unpaid months" were typed literally, so changing the office's threshold left
        // the note contradicting the figure beside it. Both now read DomainRules.DelinquentThresholdMonths.
        Assert.Contains("@DomainRules.DelinquentThresholdMonths or more unpaid months", markup);
        Assert.Contains("1–@(DomainRules.DelinquentThresholdMonths - 1) unpaid months", markup);

        Assert.DoesNotContain(">3 or more unpaid months<", markup);
        Assert.DoesNotContain(">1–2 unpaid months<", markup);
    }

    [Fact]
    public void EverySectionJumpLinkPointsAtASectionThatExists()
    {
        // The links fill the gap in the control row on a report whose sections mostly start below the fold. An anchor that
        // names an id nothing carries fails silently — the page simply does not move — so the two are checked together.
        var markup = ReadReport(string.Empty);

        var targets = Regex.Matches(markup, @"class=""rfp-jump-links"">(?<body>.*?)</div>", RegexOptions.Singleline)
            .Cast<Match>()
            .SelectMany(m => Regex.Matches(m.Groups["body"].Value, @"href=""#(?<id>[a-z-]+)""").Cast<Match>())
            .Select(m => m.Groups["id"].Value)
            .ToList();

        Assert.Equal(5, targets.Count);

        foreach (var id in targets)
            Assert.Contains($@"id=""{id}""", markup);
    }

    [Fact]
    public void TheDelinquentEmptyState_SaysWhereTheMoneyIs_RatherThanJustThatThereIsNone()
    {
        // "No delinquent accounts" read as "nothing is badly behind" while former occupancies owed ₱11,370 in the block
        // below. The lists count only accounts still being billed, and a debt must not be stated twice on one page, so the
        // empty state discloses the other figure instead of absorbing it.
        var markup = ReadReport(string.Empty);

        Assert.Contains("is owed by former occupancies, stated below.", markup);
        Assert.Contains("Model.ClosedWithBalanceOutstanding", markup);
    }
}
