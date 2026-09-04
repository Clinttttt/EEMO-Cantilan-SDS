namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A downloadable import template carries its column headings and nothing else.
/// </summary>
/// <remarks>
/// The payment-history template used to ship a worked row - <c>1, Kim Chui, 2025-03, 12, OR-100234</c> - and the page's parser skips
/// only the HEADER line. So an office that downloaded the template, typed its own rows beneath and uploaded it sent that row too: a
/// stall, a named payor, a period, a twelve-day count and a receipt number. A fabricated payment against a real occupant.
///
/// <para>Cantilan happened to be safe, because the server refuses a period no occupancy answers for and its terms begin after
/// 2025-03. That is accident rather than protection - an LGU whose contract ran in 2025 would have had it accepted. Both pages
/// already offer "Use sample data instead", which loads a worked example into the on-screen grid where it can be seen for what it
/// is and can never be uploaded as a file.</para>
///
/// <para>Read as text, because these templates are built inside Razor page components that no test project renders. The rule is
/// worth holding at this granularity anyway: what must never appear is a plausible NAME or RECEIPT beside a period.</para>
/// </remarks>
public class ImportTemplatesCarryNoSampleDataTests
{
    /// <summary>The two pages that offer a downloadable CSV template.</summary>
    private static readonly string[] ImportPages =
    [
        "ImportStallholders.razor",
        "ImportPaymentHistory.razor",
    ];

    /// <summary>
    /// Names and receipt numbers that were, or would be, written into a template.
    /// </summary>
    /// <remarks>
    /// Deliberately literal. A template is built by string concatenation, so the only reliable signal is the sample content itself:
    /// a person's name and an OR number are what turn a guidance line into a row the server would accept.
    /// </remarks>
    private static readonly string[] SampleGiveaways =
    [
        "OR-100234",
        "Kim Chui",
        "Joseph Villamor",
    ];

    /// <summary>
    /// The template builder emits headings only.
    /// </summary>
    /// <remarks>
    /// Checks the template property itself rather than the whole file: both pages legitimately hold sample rows for the
    /// "Use sample data instead" button, which never becomes a file the office can upload.
    /// </remarks>
    [Fact]
    public void NoTemplateBuilderWritesASampleRow()
    {
        var offenders = new List<string>();

        foreach (var (page, text) in ReadPages())
        {
            var builder = TemplateBuilderOf(text);

            if (builder is null)
            {
                offenders.Add($"{page}: no template builder found - this test can no longer see what it checks");
                continue;
            }

            foreach (var giveaway in SampleGiveaways.Where(builder.Contains))
                offenders.Add($"{page}: template contains \"{giveaway}\"");
        }

        Assert.True(offenders.Count == 0,
            "A downloadable template carries sample data. The page's parser skips only the header, so the office uploads it as a "
            + "real row:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Both pages still offer a worked example somewhere, which is what makes a bare template acceptable.
    /// </summary>
    /// <remarks>
    /// Without this, the rule above could be satisfied by deleting the office's only worked example and leaving it with a bare
    /// header and no guidance at all.
    /// </remarks>
    [Fact]
    public void BothPagesStillOfferSampleDataOnScreen()
    {
        var missing = ReadPages()
            .Where(p => !p.Text.Contains("Use sample data instead"))
            .Select(p => p.Page)
            .ToList();

        Assert.True(missing.Count == 0,
            "These pages no longer offer a worked example on screen, so a header-only template leaves the office nothing to read:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>Every page this test names must exist, or its rules silently cover nothing.</summary>
    [Fact]
    public void EveryPageNamedHereStillExists()
    {
        var missing = ImportPages
            .Where(page => !File.Exists(Path.Combine(PagesDirectory(), page)))
            .ToList();

        Assert.True(missing.Count == 0,
            "These pages are named by this test but no longer exist:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// The body of the property that builds the downloadable template, or null when it cannot be found.
    /// </summary>
    private static string? TemplateBuilderOf(string text)
    {
        foreach (var name in new[] { "TemplateDataUri", "TemplateHref" })
        {
            var start = text.IndexOf($"private string {name}", StringComparison.Ordinal);
            if (start < 0) continue;

            // As far as the return that produces the data URI, which is the end of the builder in both pages.
            var end = text.IndexOf("data:text/csv", start, StringComparison.Ordinal);
            if (end < 0) continue;

            return text[start..end];
        }

        return null;
    }

    private static IEnumerable<(string Page, string Text)> ReadPages()
    {
        var dir = PagesDirectory();

        foreach (var page in ImportPages)
        {
            var path = Path.Combine(dir, page);
            if (File.Exists(path))
                yield return (page, File.ReadAllText(path));
        }
    }

    private static string PagesDirectory() => Path.Combine(
        RepositoryRoot(), "EEMOCantilanSDS.Client", "Components", "Pages", "Menus", "Facilities");

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
