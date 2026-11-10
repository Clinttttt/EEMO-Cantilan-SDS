using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The dark theme is one block of app.css. A colour nobody defined, a line of text nobody can read or an unbalanced
/// stylesheet would pass the build and every page test, so they are asserted against the files.
/// </summary>
public sealed class DarkThemeTests
{
    private static string ClientRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "EEMOCantilanSDS.Client");
    }

    private static string AppCssPath => Path.Combine(ClientRoot(), "wwwroot", "app.css");

    private static IEnumerable<string> Stylesheets() =>
        Directory.EnumerateFiles(Path.Combine(ClientRoot(), "Components"), "*.razor.css", SearchOption.AllDirectories).Append(AppCssPath);

    private static string WithoutComments(string path) => Regex.Replace(File.ReadAllText(path), @"/\*.*?\*/", "", RegexOptions.Singleline);

    private static Dictionary<string, string> DarkTokens()
    {
        var block = Regex.Match(File.ReadAllText(AppCssPath), @"@media screen \{\s*html\[data-st-theme=""dark""\]:not\(:has\(\.pp-shell\)\) \{(?<body>[^}]*)\}");
        Assert.True(block.Success, "The dark theme must stay screen-only, so print keeps the light values, and off the payor pages.");
        return Regex.Matches(block.Groups["body"].Value, @"(--[a-z0-9-]+):\s*([^;]+);")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim());
    }

    [Fact]
    public void EveryDarkColourInUse_IsDefined()
    {
        var defined = DarkTokens();

        var missing = Stylesheets()
            .SelectMany(path => Regex.Matches(WithoutComments(path), @"var\((--dark-[a-z0-9-]+),").Select(m => m.Groups[1].Value))
            .Distinct()
            .Where(token => !defined.ContainsKey(token))
            .ToList();

        Assert.True(missing.Count == 0, "Used but never defined: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryStylesheet_IsBraceBalanced()
    {
        var unbalanced = Stylesheets()
            .Where(path =>
            {
                var css = WithoutComments(path);
                return css.Count(c => c == '{') != css.Count(c => c == '}');
            })
            .ToList();

        Assert.True(unbalanced.Count == 0, "One bad brace breaks the whole bundle: " + string.Join(", ", unbalanced));
    }

    [Fact]
    public void Text_MeetsAA_OnEverySurfaceItSitsOn()
    {
        string[] onSurfaces =
        [
            "--text", "--navy", "--navy-2", "--navy-3", "--text-secondary", "--text-subtle", "--text-tertiary", "--text-muted",
            "--accent", "--accent-hover", "--green", "--red",
            "--dark-text", "--dark-text-secondary", "--dark-text-tertiary", "--dark-accent-text", "--dark-red-text", "--dark-green-text", "--dark-amber-text",
        ];
        string[] surfaces = ["--bg", "--canvas", "--surface", "--bg-card", "--surface-muted", "--surface-hover", "--bg-icon", "--dark-surface", "--dark-surface-muted", "--dark-surface-sunken"];
        (string Text, string Ground)[] pairs =
        [
            .. onSurfaces.SelectMany(text => surfaces.Select(ground => (text, ground))),
            ("--green", "--green-bg"), ("--red", "--red-bg"), ("--accent", "--accent-soft"), ("--accent", "--surface-blue-subtle"),
            ("--dark-green-text", "--dark-green-tint"), ("--dark-red-text", "--dark-red-tint"), ("--dark-amber-text", "--dark-amber-tint"), ("--dark-accent-text", "--dark-blue-tint"),
            ("--text", "--dark-green-tint"), ("--text", "--dark-red-tint"), ("--text", "--dark-amber-tint"), ("--text", "--dark-blue-tint"),
            ("--dark-on-fill", "--dark-navy-fill"), ("--dark-on-fill", "--dark-navy-2-fill"), ("--dark-on-fill", "--dark-navy-3-fill"),
            ("--dark-on-fill", "--dark-accent-fill"), ("--dark-on-fill", "--dark-accent-hover-fill"), ("--dark-on-fill", "--dark-green-fill"), ("--dark-on-fill", "--dark-red-fill"),
        ];
        var tokens = DarkTokens();

        var failing = pairs
            .Select(p => (p.Text, p.Ground, Ratio: Contrast(tokens[p.Text], tokens[p.Ground])))
            .Where(p => p.Ratio < 4.5)
            .Select(p => $"{p.Text} on {p.Ground}: {p.Ratio:0.00}")
            .ToList();

        Assert.True(failing.Count == 0, "Below AA: " + string.Join("; ", failing));
    }

    [Fact]
    public void TheThemeIsApplied_BeforeTheStylesheets_AndTheToggleIsHandled()
    {
        var app = File.ReadAllText(Path.Combine(ClientRoot(), "Components", "App.razor"));

        Assert.True(app.IndexOf("window.stallTrackApplyTheme();", StringComparison.Ordinal) is var applied && applied >= 0
            && applied < app.IndexOf("site.min.css", StringComparison.Ordinal), "Set after the stylesheets, a prerendered page paints the wrong theme first.");
        Assert.Contains("[data-st-theme-toggle]", app);
    }

    private static double Contrast(string first, string second)
    {
        var (a, b) = (Luminance(first), Luminance(second));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int at)
        {
            var c = int.Parse(hex.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }
}
