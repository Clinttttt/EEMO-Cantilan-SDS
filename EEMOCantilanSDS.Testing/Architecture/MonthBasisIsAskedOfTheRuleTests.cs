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
    /// Turning a daily fee into a month by hand: the thirty-day convention, named or written out.
    /// </summary>
    /// <remarks>
    /// Policed after an audit found the Public Market Report doing exactly this - <c>DailyRate * 30m</c> written out in the
    /// page - which stated ₱900 on the register of a market that owes ₱930 in a long month. The test above could not see it,
    /// because the arithmetic never went near a <c>DomainRules</c> helper. Thirty is only a month where the office bills a
    /// monthly GOAL, so every use of it is a decision and belongs in the list below with its reason.
    /// </remarks>
    private static readonly string[] MonthFromDailyByHand =
    {
        "DailyBilledMonthDays",
        "* 30m",
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

    /// <summary>
    /// The files allowed to turn a daily fee into a month by hand, each with the reason.
    /// </summary>
    private static readonly Dictionary<string, string> AllowedMonthFromDaily = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FeeRates.cs"] =
            "Where the convention is DEFINED, and where the rent-goal arithmetic that uses it lives.",

        ["Stall.cs"] =
            "ResolveMonthlyRent: the rent a space is LET for, which is thirty installments where the office states no "
            + "month. The rule ignores it on the days basis, so it decides nothing there.",

        ["FacilityCode.cs"] = "A comment recording the convention beside the facility it applies to.",

        ["GetSystemSettingsQueryHandler.cs"] =
            "States the market's rule in words. Gated on the rule itself, so an office billed by the days its months have "
            + "is never told it owes a monthly amount - which it WAS until the audit of 2026-08-31.",

        ["GetNpmRatesQueryHandler.cs"] =
            "MonthlyRentInUse, the figure the setup confirmation offers an office to confirm as its own month. Asked only "
            + "of an office whose month IS a rent.",

        ["NpmReports.razor"] =
            "The register's monthly column, gated on the rule after the audit found it stating a month nobody owes.",

        ["StallHoldersList.razor"] = "A comment pointing at where the roster's figures come from.",

        ["MarketRentReminder.razor"] = "States the convention to an office being asked to confirm its own month.",

        ["FacilityConfiguration.razor"] =
            "States what a month comes to under each basis, in the office's own figures, so it chooses on numbers.",

        ["DailyFeeFromMonthlyRent.cs"] =
            "Runs the convention BACKWARDS - a monthly rent divided into a daily fee - which is the office's own arithmetic "
            + "and the whole purpose of that class.",
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

    private static List<string> FilesCallingTheBasisLessArithmetic() => FilesContaining(BasisLessCalls);

    private static List<string> FilesTurningADailyFeeIntoAMonth() => FilesContaining(MonthFromDailyByHand);

    private static List<string> FilesContaining(string[] needles)
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
                if (needles.Any(c => text.Contains(c, StringComparison.Ordinal)))
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
    public void TurningADailyFeeIntoAMonthByHandIsANamedDecision()
    {
        // Thirty is only a month where the office bills a monthly GOAL. The audit of 2026-08-31 found the Public Market
        // Report multiplying a daily rate by thirty in the page, which stated ₱900 on the register of a market that owes
        // ₱930 in a long month and ₱840 in February - and the test above could not see it, because the arithmetic never
        // went near a DomainRules helper. Every use of the convention is now a listed decision.
        var offenders = FilesTurningADailyFeeIntoAMonth()
            .Where(f => !AllowedMonthFromDaily.ContainsKey(f))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(offenders.Count == 0,
            "These files turn a daily fee into a month by hand: " + string.Join(", ", offenders)
            + ". Thirty days is a month only where the office bills a monthly goal; where it bills the days a month has, "
            + "that product is a figure nobody is charged. Ask FeeRateSnapshot.MonthRule, or add the file to "
            + "`AllowedMonthFromDaily` with the reason the convention is right there.");
    }

    [Fact]
    public void TheMonthFromDailyAllowanceHasNoDeadEntries()
    {
        var naming = FilesTurningADailyFeeIntoAMonth();

        var stale = AllowedMonthFromDaily.Keys
            .Where(f => !naming.Contains(f, StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.True(stale.Count == 0,
            "These files no longer turn a daily fee into a month, so they should leave `AllowedMonthFromDaily`: "
            + string.Join(", ", stale));
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
