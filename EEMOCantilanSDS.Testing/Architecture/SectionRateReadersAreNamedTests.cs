using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace EEMOCantilanSDS.Testing.Architecture;

/// <summary>
/// Who may read a market section's own rate, and how a stall's daily fee is arrived at.
///
/// <para>
/// An office's own market section carries its own daily fee, stored in <c>FacilitySectionRates</c> and effective-dated so
/// a rate stated today never re-prices an elapsed day. The fee a stall is billed is settled in ONE place —
/// <c>NpmDailyFee</c> — which puts the stall's own rate first, then its section's, then its area's, then the market's.
/// </para>
///
/// <para>
/// The danger this rule exists for is a second reader. Anything that bills, settles, imports or reports a daily fee by
/// querying the rate table itself would be answering the same question by a different rule, and would silently miss the
/// order above: a section priced at ₱25 would go on being collected at the market's ₱30, with both figures defensible on
/// their own screen and neither reconcilable with the other. Every borrowed-rate defect this platform has had was that
/// shape, and each was found by an office reading its own collections rather than by the code.
/// </para>
///
/// <para>
/// So the table has a named readership, and it is deliberately short: the resolver that loads it into the fee snapshot,
/// and the office's own configuration screen, which states back what the office itself entered. Anything else asks
/// <c>NpmDailyFee</c>.
/// </para>
/// </summary>
public class SectionRateReadersAreNamedTests
{
    /// <summary>What mentioning the rate table looks like.</summary>
    private const string TheRateTable = "FacilitySectionRates";

    /// <summary>
    /// What mentioning the metering default looks like. DORMANT since 2026-08-30: nothing reads it and nothing writes it.
    /// </summary>
    /// <remarks>
    /// The office asked for the control to go, and it was right. Every new market stall's form already opens with both
    /// meters ticked, and the section's default could only ever ADD one, so it could add nothing that was not there; its
    /// only reachable effect was to re-tick a meter a clerk had just unticked, if they then changed the section.
    /// The TABLE stays because production applies migrations at startup and this platform is additive only, so dropping one
    /// would break a running instance mid-deploy. It keeps its entity, its mapping and its place in an office's backup, so
    /// rows an office already has are still carried and still wiped with the tenant. Nothing else may touch it.
    /// </remarks>
    private const string TheUtilitiesTable = "FacilitySectionUtilities";

    /// <summary>
    /// Files that may name either table, each with the reason. A file added here is a decision; a file that no longer
    /// belongs is caught by the second test.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IAppDbContext.cs"] = "The seam itself: it declares the sets.",
        ["AppDbContext.cs"] = "The context itself.",
        // FacilitySectionRate.cs is not here: the entity's own file never names the SET, so the rule does not reach it.
        ["FacilitySectionUtilities.cs"] = "The entity, whose type name is the table's. Dormant, and kept only so its rows survive.",
        ["FacilitySectionRateConfiguration.cs"] = "Its mapping.",
        ["FacilitySectionUtilitiesConfiguration.cs"] = "Its mapping.",

        ["FeeRateResolver.cs"] =
            "The one reader that matters: it loads the rows into the fee snapshot, which is what NpmDailyFee asks. "
            + "Everything that bills goes through here.",

        ["FacilityRepository.cs"] =
            "The office's own configuration screen, stating back the fee it entered, section by section. It bills nothing: "
            + "it answers what the office SAID, not what a stall is charged.",

        ["SetNpmSectionRateCommandHandler.cs"] = "Writes the one effective-dated rate row.",
        ["AddNpmCustomSectionCommandHandler.cs"] = "Writes that same rate row when the office prices a section as it creates it.",

        ["TenantDataTables.cs"] = "Names the tables an office's export and restore must carry.",
        ["TenantExportRepository.cs"] = "Assembles that export, table by table.",
    };

    /// <summary>
    /// The only files that may name the dormant metering table: it is storage nothing acts on, and the point of removing
    /// the control was that it did nothing. A new reader would be reviving a feature the office asked to be rid of.
    /// </summary>
    private static readonly string[] MeteringTableKeepers =
    {
        "IAppDbContext.cs",
        "AppDbContext.cs",
        "FacilitySectionUtilities.cs",
        "FacilitySectionUtilitiesConfiguration.cs",
        "TenantDataTables.cs",
        "TenantExportRepository.cs",
    };

    /// <summary>Where production code lives. Tests and migrations are not the rule's business.</summary>
    private static readonly string[] Projects =
    {
        "EEMOCantilanSDS.Domain",
        "EEMOCantilanSDS.Application",
        "EEMOCantilanSDS.Infrastructure",
        "EEMOCantilanSDS.Api",
        "EEMOCantilanSDS.Client",
        "EEMOCantilanSDS.HttpClients",
        "EEMOCantilanSDS.Mobile",
        "EEMOCantilanSDS.Mobile.Core",
    };

    private static List<string> FilesNaming(params string[] needles)
    {
        var root = RepositoryRoot();
        var found = new List<string>();

        foreach (var project in Projects)
        {
            var dir = Path.Combine(root, project);
            if (!Directory.Exists(dir)) continue;

            foreach (var path in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(path);
                if (extension is not (".cs" or ".razor")) continue;

                // Generated output and migrations are nobody's decision: a migration names the table by definition.
                if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
                    continue;

                var text = File.ReadAllText(path, Encoding.UTF8);
                if (needles.Any(n => text.Contains(n, StringComparison.Ordinal)))
                    found.Add(Path.GetFileName(path));
            }
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [Fact]
    public void OnlyNamedFilesReadASectionsOwnRateOrItsMeteringDefault()
    {
        var offenders = FilesNaming(TheRateTable, TheUtilitiesTable)
            .Where(f => !Allowed.ContainsKey(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files read a market section's own rate or its metering default, and are not in the named readership: "
            + string.Join(", ", offenders)
            + ". A daily fee is settled in NpmDailyFee, which puts the stall's own rate first, then its section's, then "
            + "its area's, then the market's. A second reader answers the same question by a different rule and misses "
            + "that order. Ask NpmDailyFee, or add the file to `Allowed` in this test with the reason it is not billing.");
    }

    [Fact]
    public void TheMeteringDefaultStaysDormant()
    {
        // The control was removed on 2026-08-30 because it could not do anything: a new stall's form already opens with both
        // meters, and the section's default could only ADD. The table remains only so an office's existing rows are still
        // exported and still wiped with the tenant. Anything else naming it is reviving the feature.
        var naming = FilesNaming(TheUtilitiesTable)
            .Where(f => !MeteringTableKeepers.Contains(f, StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(naming.Count == 0,
            "These files touch the dormant section metering table: " + string.Join(", ", naming)
            + ". It is storage nothing acts on. A stall's meters belong to the stall's own form, which already offers both, "
            + "and a section default could only ever add one that was already there.");
    }

    [Fact]
    public void TheNamedReadershipHasNoDeadEntries()
    {
        // A name left here after the file stopped reading the table reads as a decision nobody took.
        var naming = FilesNaming(TheRateTable, TheUtilitiesTable);

        var stale = Allowed.Keys
            .Where(f => !naming.Contains(f, StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(stale.Count == 0,
            "These files no longer read a section's rate or metering default, so they should be removed from `Allowed`: "
            + string.Join(", ", stale));
    }

    [Fact]
    public void EveryEntryStatesItsReason()
    {
        var unexplained = Allowed
            .Where(e => string.IsNullOrWhiteSpace(e.Value))
            .Select(e => e.Key)
            .ToList();

        Assert.True(unexplained.Count == 0,
            "An entry without a reason is an exemption nobody can review: " + string.Join(", ", unexplained));
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
