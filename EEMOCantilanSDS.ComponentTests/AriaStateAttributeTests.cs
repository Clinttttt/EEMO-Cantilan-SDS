using System.Text.RegularExpressions;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// An ARIA state attribute must render the literal "true" or "false", never a bare C# bool.
///
/// <para>
/// Blazor MINIMISES a boolean attribute: bound to <c>true</c> it renders <c>aria-checked</c> with an empty value, and bound
/// to <c>false</c> the attribute is dropped altogether. Neither conveys a state. WAI-ARIA requires "true", "false" or
/// "mixed", so a screen reader was told nothing about which basis of occupancy, which report period or which section the
/// office had selected. It was found on the Add Vendor radios and proved to be in twenty places.
/// </para>
///
/// <para>
/// Scanned from source rather than asserted per component because the fault is a habit, not a bug in one page: two places
/// already used the explicit form while thirty others did not, and a render test on one of them would not have noticed.
/// </para>
/// </summary>
public class AriaStateAttributeTests
{
    /// <summary>Attributes whose value must be a literal ARIA state rather than whatever a bool stringifies to.</summary>
    private const string StateAttributes = "checked|selected|expanded|pressed|current";

    private static readonly Regex BoundState =
        new($@"aria-(?:{StateAttributes})\s*=\s*""@\((?<expr>[^)]*(?:\([^)]*\)[^)]*)*)\)""", RegexOptions.Compiled);

    /// <summary>Walks up from the test assembly to the repository root, found by its solution file.</summary>
    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("EEMOCantilanSDS.slnx").Any())
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    [Fact]
    public void NoAriaStateIsBoundStraightToABool()
    {
        var client = new DirectoryInfo(Path.Combine(RepositoryRoot().FullName, "EEMOCantilanSDS.Client"));
        Assert.True(client.Exists, $"expected the console project at {client.FullName}");

        var offenders = new List<string>();

        foreach (var file in client.EnumerateFiles("*.razor", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file.FullName);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var match in BoundState.Matches(lines[i]).Cast<Match>())
                {
                    var expr = match.Groups["expr"].Value;

                    // The two acceptable shapes: an explicit ternary, or a bool stringified and lowered on purpose.
                    if (expr.Contains("\"true\"") && expr.Contains("\"false\"")) continue;
                    if (expr.Contains("ToLowerInvariant")) continue;

                    offenders.Add($"{file.Name}:{i + 1}  {match.Value}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "An ARIA state must render \"true\" or \"false\" explicitly — Blazor minimises a bool attribute, so the state is "
            + "lost to a screen reader. Write aria-pressed=\"@(cond ? \"true\" : \"false\")\". Offenders:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheScannerActuallyFindsTheFaultItIsLookingFor()
    {
        // Proof the guard above is not vacuous: a passing suite must mean the codebase is clean, not that the pattern never
        // matches anything. These are the exact shapes that were in the console before they were corrected.
        Assert.Matches(BoundState, @"aria-pressed=""@(ActivePeriod == period)""");
        Assert.Matches(BoundState, @"aria-checked=""@(Form.Arrangement == option.Value)""");
        Assert.Matches(BoundState, @"aria-selected=""@(_selMonth == 0)""");

        // ...and that the corrected shape is what it accepts.
        var fixedForm = @"aria-pressed=""@(ActivePeriod == period ? ""true"" : ""false"")""";
        var m = BoundState.Match(fixedForm);
        Assert.True(m.Success);
        Assert.Contains("\"true\"", m.Groups["expr"].Value);
    }
}
