using System.Text;
using Xunit;

namespace EEMOCantilanSDS.Testing.Architecture;

/// <summary>
/// A market area is named by the office that collects in it, and this holds the portal to that.
///
/// <para>
/// The same defect was reported four times, on four screens, over three days: Export Data's filter and printed
/// sheet, the Revenue by Market Section caption, the Section Performance Mix chart, the Add Vendor dropdown, and
/// both import section choosers each showed "Vegetable Area / Fish Area / Meat Area" to an office that calls those
/// areas Gulayan, Isda and Karne. Every instance was the same omission: the canonical name is the KEY the data is
/// grouped and filed under, and it was rendered directly instead of being handed to
/// <c>FacilityState.SectionLabelOf</c> first.
/// </para>
///
/// <para>
/// Fixing them one report at a time does not stop the next one. So the rule is stated here: a file in the portal
/// that mentions a canonical section name at all must also ask the office's record what to call it. That is
/// deliberately a file-level rule rather than an attempt to parse markup — it is cheap, it has no false negatives
/// worth the name, and where a file legitimately has no display to make, it is listed below WITH THE REASON.
/// </para>
/// </summary>
public class SectionNamesComeFromTheOfficeTests
{
    /// <summary>
    /// How the platform names the three collection areas when an office has named none. Mentioning any of these is
    /// what puts a file under this rule.
    /// </summary>
    private static readonly string[] CanonicalSectionNames =
    [
        "\"Vegetable Area\"",
        "\"Fish Area\"",
        "\"Meat Area\"",
        "\"Fish Section\"",
        "\"Meat Section\"",
        "MarketSection.VegetableArea",
        "MarketSection.FishSection",
        "MarketSection.MeatSection",
    ];

    /// <summary>What asking the office's record looks like.</summary>
    private const string AsksTheOffice = "SectionLabelOf";

    /// <summary>
    /// Files that mention a canonical name and correctly never ask for the office's own, each with the reason.
    /// A file added here is a decision; a file that no longer belongs is caught by the second test.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        // FacilityConfiguration.razor was excused here while its only mention of a canonical area was the field labels
        // it names them by — the form could not be labelled with the very answer it asks for. It now also REGISTERS the
        // office's own sections, and refuses a name the market already uses, which it settles by asking the office's
        // record what it calls its three areas. So it asks, and the excuse is no longer its due.

        ["StallHoldersList.razor"] =
            "Mentions the canonical name only in comments, explaining that the label arrives from the server "
            + "already resolved and must be used as it is. No name is rendered from a literal here.",
    };

    /// <summary>Where the office's screens live. The API and Domain hold the keys, and are not display.</summary>
    private const string Portal = "EEMOCantilanSDS.Client";

    private static List<string> FilesMentioningACanonicalName(out Dictionary<string, bool> asksTheOffice)
    {
        var mentions = new List<string>();
        asksTheOffice = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        var root = Path.Combine(RepositoryRoot(), Portal);
        Assert.True(Directory.Exists(root), $"the portal was not found at {root}");

        foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path);
            if (extension is not (".razor" or ".cs")) continue;

            // Generated output is nobody's decision.
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var text = File.ReadAllText(path, Encoding.UTF8);
            if (!CanonicalSectionNames.Any(name => text.Contains(name, StringComparison.Ordinal)))
                continue;

            var file = Path.GetFileName(path);
            mentions.Add(file);
            asksTheOffice[file] = text.Contains(AsksTheOffice, StringComparison.Ordinal);
        }

        return mentions;
    }

    [Fact]
    public void EveryScreenThatNamesAMarketAreaAsksTheOfficeWhatToCallIt()
    {
        var files = FilesMentioningACanonicalName(out var asksTheOffice);

        var offenders = files
            .Where(f => !asksTheOffice[f])
            .Where(f => !Allowed.ContainsKey(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files name a market area from the platform's own wording and never ask the office's record what "
            + "it calls that area, so an LGU is shown this platform's words for its own market. Render the label "
            + "through FacilityState.SectionLabelOf, keeping the canonical value as the key the data is filed "
            + "under, or add the file to `Allowed` in this test with the reason it has no display to make: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void TheAllowedSetHasNoDeadEntries()
    {
        var files = FilesMentioningACanonicalName(out var asksTheOffice);

        // An entry is dead once the file stops mentioning a canonical name at all, or starts asking properly:
        // leaving it listed would quietly excuse a future display in that file.
        var dead = Allowed.Keys
            .Where(a => !files.Contains(a, StringComparer.OrdinalIgnoreCase) || asksTheOffice[a])
            .OrderBy(a => a, StringComparer.Ordinal)
            .ToList();

        Assert.True(dead.Count == 0,
            "These files no longer need to be excused from naming areas as the office does, so they should be "
            + "removed from `Allowed`: " + string.Join(", ", dead));
    }

    [Fact]
    public void TheScanItselfIsNotBrokenOrVacuous()
    {
        // A scan that finds nothing passes for the wrong reason. The portal renders these areas on many screens,
        // so if this ever drops to nothing the scan has stopped reading the tree rather than the tree having
        // stopped mentioning them.
        var files = FilesMentioningACanonicalName(out var asksTheOffice);

        Assert.True(files.Count >= 8,
            $"only {files.Count} portal files mention a market area by name, which means this scan is no longer "
            + "reading the portal. The rule it enforces is only as good as the files it sees.");
        Assert.Contains(true, asksTheOffice.Values);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);        // the tests above are meaningless if the tree cannot be found
        return dir!.FullName;
    }

    // ── The second rule: canonical wording written straight into rendered text ───────────────────────────────

    /// <summary>
    /// The wording itself, as it would read on a screen. Matched case-insensitively, so "Fish area" is caught with
    /// "Fish Area", and without the enum names, which are keys and never rendered.
    /// </summary>
    private static readonly string[] CanonicalWording =
    [
        "Vegetable Area", "Fish Area", "Meat Area", "Fish Section", "Meat Section",
        "Vegetable stalls", "Fish stalls", "Meat stalls",
    ];

    /// <summary>
    /// Razor files allowed to print the canonical wording, with the reason. Distinct from <see cref="Allowed"/>:
    /// this is about wording rendered on a screen, not about a file's use of the key.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedToPrintCanonicalWording = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FacilityConfiguration.razor"] =
            "The screen where the office names its areas. Its field labels and placeholders must say which area is "
            + "being named, in the platform's own words, or the form would be labelled with the answer it asks for.",
    };

    /// <summary>
    /// Canonical wording sitting in a RENDERED text position: between a closing '>' and the next '<', or in a
    /// placeholder or title attribute. Deliberately narrow. A comparison against the canonical KEY —
    /// <c>ActiveSection == "Fish Area"</c> — is not a rendering and is not flagged, which is why this looks for the
    /// wording after markup rather than anywhere in the line.
    /// </summary>
    private static List<string> CanonicalWordingInRenderedText()
    {
        var found = new List<string>();
        var root = Path.Combine(RepositoryRoot(), Portal);

        foreach (var path in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var file = Path.GetFileName(path);
            if (AllowedToPrintCanonicalWording.ContainsKey(file)) continue;

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            var inCodeBlock = false;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();

                // Everything from @code onward is C#, not markup. It renders nothing, and its generic types read
                // like tags to a line scanner: IReadOnlyList<string> would otherwise be taken for an element.
                if (trimmed.StartsWith("@code", StringComparison.Ordinal)) inCodeBlock = true;
                if (inCodeBlock) continue;

                // Comments state the canonical wording to explain the rule; they render nothing.
                if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("@*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("*", StringComparison.Ordinal) ||
                    trimmed.StartsWith("<!--", StringComparison.Ordinal))
                    continue;

                foreach (var wording in CanonicalWording)
                {
                    if (!RendersWording(line, wording)) continue;
                    found.Add($"{file}:{i + 1}");
                    break;
                }
            }
        }

        return found;
    }

    private static bool RendersWording(string line, string wording)
    {
        foreach (var attribute in new[] { "placeholder=\"", "title=\"" })
        {
            var at = line.IndexOf(attribute, StringComparison.OrdinalIgnoreCase);
            if (at < 0) continue;
            var close = line.IndexOf('"', at + attribute.Length);
            if (close < 0) close = line.Length;
            if (line[(at + attribute.Length)..close].Contains(wording, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Text nodes: after a '>' and before the next '<'.
        var cursor = 0;
        while (true)
        {
            var open = line.IndexOf('>', cursor);
            if (open < 0) return false;

            // Not every '>' closes a tag. A switch arm or lambda — MarketSection.FishSection => "Fish Area" — has
            // one, and reading past it turns a mapping to the canonical KEY into a false report of a rendering.
            if (open > 0 && (line[open - 1] == '=' || line[open - 1] == '-'))
            {
                cursor = open + 1;
                continue;
            }

            var end = line.IndexOf('<', open + 1);
            var text = end < 0 ? line[(open + 1)..] : line[(open + 1)..end];

            // A razor expression is not a literal: the wording inside @SectionDisplay("Fish Area") is the argument
            // naming which area to ask about, which is the correct call rather than a rendering of the words.
            if (!text.Contains('@') && text.Contains(wording, StringComparison.OrdinalIgnoreCase)) return true;

            if (end < 0) return false;
            cursor = end + 1;
        }
    }

    [Fact]
    public void NoScreenPrintsThePlatformsWordingForAnAreaTheOfficeHasNamed()
    {
        var offenders = CanonicalWordingInRenderedText();

        Assert.True(offenders.Count == 0,
            "These lines print the platform's own wording for a market area straight into the page, so an office "
            + "reads this platform's words for its own market however it has named that area. Render it through "
            + "FacilityState.SectionLabelOf, or add the file to `AllowedToPrintCanonicalWording` with the reason: "
            + string.Join(", ", offenders));
    }

    [Fact]
    public void TheRenderedTextScanCanStillSeeTheWordingItLooksFor()
    {
        // Guards the narrow matcher above: if it stopped recognising a rendered text node, the test that uses it
        // would pass on every tree, including a broken one.
        Assert.True(RendersWording("<h2>Fish Area — Kilo Tracking</h2>", "Fish Area"));
        Assert.True(RendersWording("<input placeholder=\"Vegetable Area\" />", "Vegetable Area"));
        Assert.True(RendersWording("<span class=\"x\">Meat area</span>", "Meat Area"));

        // And that it does not flag a comparison against the canonical KEY, which is not a rendering.
        Assert.False(RendersWording("var isFish = ActiveSection == \"Fish Area\";", "Fish Area"));
        Assert.False(RendersWording("var canonical = new[] { \"Fish Area\", \"Meat Area\" };", "Fish Area"));

        // Nor a switch arm mapping a section to its canonical key: the '>' belongs to the arrow, not to a tag.
        Assert.False(RendersWording("MarketSection.FishSection => \"Fish Area\",", "Fish Area"));
        Assert.False(RendersWording("\"Fish\" => \"Fish Area\",", "Fish Area"));

        // Nor the wording used as the ARGUMENT naming which area to ask the office about.
        Assert.False(RendersWording("<h2>@SectionDisplay(\"Fish Area\") — Kilo Tracking</h2>", "Fish Area"));
    }
}
