using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Constants;
using Moq;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// The office is the Municipal Economic Enterprises Development Office (MEEDO). The name and acronym live in the tenant's
/// branding record; these tests pin the fallbacks that are used before branding loads, and guard the current UI source
/// against the former office wording. Technical identifiers (EEMOCantilanSDS.* namespaces, cache/CSS names) are not touched.
/// </summary>
public class MeedoBrandingTests
{
    private const string FullName = "Municipal Economic Enterprises Development Office";

    [Fact]
    public void BeforeBrandingLoadsTheDefaultOfficeIsMeedo()
    {
        var state = new BrandingState(Mock.Of<IMunicipalitiesApiClient>());

        Assert.Equal(FullName, state.OfficeName);
        Assert.Equal("MEEDO", state.OfficeAcronym);
    }

    [Fact]
    public void TheOfficeProfileConstantsUseTheCurrentOffice()
    {
        Assert.Equal(FullName, OfficeProfile.Office);
        Assert.StartsWith("MEEDO", OfficeProfile.ReceiptsIssuedBy);
    }

    [Fact]
    public void CurrentUiSourceDoesNotUseTheFormerOfficeWording()
    {
        // Narrow on purpose: only user-facing phrases, never the EEMOCantilanSDS technical names.
        string[] former =
        {
            "EEMO Office", "EEMO Admin", "EEMO Head", "\"EEMO\"",
            "Economic Enterprise & Management Office", "Economic Enterprise &amp; Management Office",
            "Economic Enterprise and Management Office", "ECONOMIC ENTERPRISE & MANAGEMENT OFFICE",
        };

        var root = RepoRoot();
        var roots = new[]
        {
            Path.Combine(root, "EEMOCantilanSDS.Client", "Components"),
            Path.Combine(root, "EEMOCantilanSDS.Client", "Services"),
            Path.Combine(root, "EEMOCantilanSDS.Mobile", "Components"),
            Path.Combine(root, "EEMOCantilanSDS.Mobile", "Services"),
            Path.Combine(root, "EEMOCantilanSDS.Mobile.Core"),
            Path.Combine(root, "EEMOCantilanSDS.Domain", "Constants"),
            Path.Combine(root, "EEMOCantilanSDS.Infrastructure", "Persistence", "Seeders"),
        };

        var hits = roots.Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.*", SearchOption.AllDirectories))
            .Where(f => f.EndsWith(".razor") || f.EndsWith(".cs"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (f, n: i + 1, line)))
            .Where(x => !x.line.TrimStart().StartsWith("//") && !x.line.TrimStart().StartsWith("*") && !x.line.TrimStart().StartsWith("@*"))
            .Where(x => former.Any(p => x.line.Contains(p, StringComparison.Ordinal)))
            .Select(x => $"{Path.GetRelativePath(root, x.f)}:{x.n}")
            .ToList();

        Assert.True(hits.Count == 0, "Former office wording in current UI source: " + string.Join(", ", hits));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
