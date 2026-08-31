using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace EEMOCantilanSDS.Testing.Architecture;

/// <summary>
/// A daily-billed month is measured in ONE place: the rule the office's basis names.
///
/// <para>
/// An office states whether a market month owes its RENT, collected in installments, or the DAYS it actually has. Both
/// arithmetics are real and both are right for the office that chose them, which makes the dangerous state not a wrong
/// figure but two figures: a month measured one way on the stall profile and the other way on the report. This platform has
/// already been bitten by exactly that - <c>DomainRules.EarnedThrough</c> exists because six paths disagreed about the month
/// in progress, and "one stall carried two different balances depending on which screen the office opened".
/// </para>
///
/// <para>
/// So the basis-less arithmetic is not for production to call. <c>DomainRules.DailyBilledMonthObligation</c> and
/// <c>DailyBilledMonthCoverage</c> are the rent-goal arithmetic, and only <c>RentGoalMonthRule</c> may name them; everything
/// else asks <c>FeeRateSnapshot.MonthRule</c>, which is resolved once per request from the office's own facility row. This
/// test is what makes partial adoption a build failure rather than a bug an office finds in a report.
/// </para>
/// </summary>
public class MonthBasisIsAskedOfTheRuleTests
{
    /// <summary>The two functions that answer a month without knowing which basis is in force.</summary>
    private static readonly string[] BasisLessCalls =
    {
        "DomainRules.DailyBilledMonthObligation(",
        "DomainRules.DailyBilledMonthCoverage(",
    };

    /// <summary>
    /// The only files allowed to name them, each with the reason.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DailyBilledMonthRule.cs"] =
            "The rent-goal rule IS this arithmetic, and the pure-days rule is the other one. Both live here so a reader "
            + "comparing the two bases reads them side by side.",
    };

    /// <summary>Production only. A test may state either arithmetic directly: that is how each basis is pinned.</summary>
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

    private static List<string> FilesCallingTheBasisLessArithmetic()
    {
        var root = RepositoryRoot();
        var found = new List<string>();

        foreach (var project in Projects)
        {
            var dir = Path.Combine(root, project);
            if (!Directory.Exists(dir)) continue;

            foreach (var path in Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(path) is not (".cs" or ".razor")) continue;
                if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                    path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
                    continue;

                var text = File.ReadAllText(path, Encoding.UTF8);
                if (BasisLessCalls.Any(c => text.Contains(c, StringComparison.Ordinal)))
                    found.Add(Path.GetFileName(path));
            }
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [Fact]
    public void OnlyTheRuleItselfMeasuresAMonthWithoutABasis()
    {
        var offenders = FilesCallingTheBasisLessArithmetic()
            .Where(f => !Allowed.ContainsKey(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files measure a daily-billed month without asking which basis the office is on: "
            + string.Join(", ", offenders)
            + ". An office may bill a month as its RENT or as the DAYS it has, and a path that assumes one of them puts a "
            + "figure on one screen that another screen contradicts. Ask FeeRateSnapshot.MonthRule, which is resolved from "
            + "the office's own facility row.");
    }

    [Fact]
    public void TheAllowanceHasNoDeadEntries()
    {
        var naming = FilesCallingTheBasisLessArithmetic();

        var stale = Allowed.Keys
            .Where(f => !naming.Contains(f, StringComparer.OrdinalIgnoreCase))
            .ToList();

        Assert.True(stale.Count == 0,
            "These files no longer call the basis-less arithmetic, so they should leave `Allowed`: " + string.Join(", ", stale));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EEMOCantilanSDS.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
