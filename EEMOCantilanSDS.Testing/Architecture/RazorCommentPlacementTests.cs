using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EEMOCantilanSDS.Testing.Architecture;

/// <summary>
/// A Razor comment (<c>@* … *@</c>) placed INSIDE an element or component tag — among its attributes — is
/// parsed by Razor as an attribute NAME. It compiles cleanly and then throws at render time:
///
///   InvalidOperationException: Object of type 'UtilityBillModal' does not have a property matching the
///   name '@* Only the utilities this stall is registered for are billable. *@'
///
/// That shipped once and took the Public Market page down in production. Neither the build nor the test
/// suites caught it, because the fault only exists in the generated render tree — hence this guard, which
/// scans the source instead.
/// </summary>
public class RazorCommentPlacementTests
{
    [Fact]
    public void NoRazorCommentSitsInsideAnElementTag()
    {
        var offenders = new List<string>();

        foreach (var file in RazorFiles())
        {
            var insideTag = false;
            var lineNumber = 0;

            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;

                if (insideTag && line.Contains("@*"))
                    offenders.Add($"{Path.GetFileName(file)}:{lineNumber}: {line.Trim()}");

                // Crude but sufficient: a line that opens a tag without closing it leaves us "inside" the
                // attribute list until a line closes one.
                var opens = Regex.Matches(line, "<[A-Za-z]").Count;
                var closes = Regex.Matches(line, "/?>").Count;
                if (opens > 0 && closes == 0) insideTag = true;
                else if (closes > 0) insideTag = false;
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A Razor comment inside a tag is read as an attribute name and throws at render. Move it above "
            + "the tag:\n  " + string.Join("\n  ", offenders));
    }

    private static IEnumerable<string> RazorFiles()
    {
        // Walk up from the test binaries to the repository root, then scan both presentation projects.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);   // the guard is worthless if it silently scans nothing

        var roots = new[] { "EEMOCantilanSDS.Client", "EEMOCantilanSDS.Mobile" }
            .Select(p => Path.Combine(dir!.FullName, p))
            .Where(Directory.Exists);

        var files = roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        Assert.NotEmpty(files);   // proves the scan actually found the components
        return files;
    }
}
