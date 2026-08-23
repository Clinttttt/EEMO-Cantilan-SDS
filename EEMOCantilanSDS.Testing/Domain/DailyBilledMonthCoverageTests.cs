using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The full-month reference a daily-collected space is measured against on a report — the "coverage" column, and what an
/// amount collected is compared with.
///
/// Written when the three report handlers were routed onto it (phase 2c, 2026-08-23). Each of them had computed thirty
/// daily fees of the MARKET's rate, which meant two things went wrong at once: an office pricing the areas of its market
/// apart was measured at the wrong fee, and an office whose ordinance states a month directly — say ₱1,000 while
/// collecting ₱35 a day — was shown ₱1,050 on its reports beside ₱1,000 on its roster, because only the roster asked
/// Stall.ResolveMonthlyRent. One rule now answers for both.
/// </summary>
public class DailyBilledMonthCoverageTests
{
    [Fact]
    public void WithNoStatedMonth_AMonthIsThirtyInstallmentsOfThisSpacesFee()
    {
        Assert.Equal(900m, DomainRules.DailyBilledMonthCoverage(dailyFee: 30m, statedMonthlyRent: 0m, excusedDays: 0));
        Assert.Equal(1_050m, DomainRules.DailyBilledMonthCoverage(dailyFee: 35m, statedMonthlyRent: 0m, excusedDays: 0));
    }

    [Fact]
    public void AStatedMonthIsTheOfficesOrdinanceAndWins()
    {
        // ₱1,000 a month while collecting ₱35 a day: thirty installments would be ₱1,050, and the office did not say so.
        Assert.Equal(1_000m, DomainRules.DailyBilledMonthCoverage(dailyFee: 35m, statedMonthlyRent: 1_000m, excusedDays: 0));
    }

    [Fact]
    public void AnExcusedDayLowersTheReferenceByOneInstallmentOfTHISSpacesFee()
    {
        // Cantilan: ₱900 less five excused days at ₱30 = ₱750, the figure its reports have always shown.
        Assert.Equal(750m, DomainRules.DailyBilledMonthCoverage(30m, 0m, 5));

        // A vegetable area priced at ₱35 deducts ₱35 a day, not the market's ₱30.
        Assert.Equal(875m, DomainRules.DailyBilledMonthCoverage(35m, 0m, 5));   // 1,050 − 175

        // And against a stated month, the deduction is still this space's own installment.
        Assert.Equal(825m, DomainRules.DailyBilledMonthCoverage(35m, 1_000m, 5));   // 1,000 − 175
    }

    [Fact]
    public void AWhollyExcusedMonthReferencesNothing_AndNeverLessThanNothing()
    {
        Assert.Equal(0m, DomainRules.DailyBilledMonthCoverage(30m, 0m, 30));
        Assert.Equal(0m, DomainRules.DailyBilledMonthCoverage(30m, 0m, 45));
        Assert.Equal(0m, DomainRules.DailyBilledMonthCoverage(30m, 900m, 100));
    }

    [Fact]
    public void AnOfficeThatStatesNoFeeIsMeasuredAgainstNothing()
    {
        // Not a constant, and not a guess: an office that has stated no daily fee and no month owes nothing under this
        // head, and every path that would charge refuses before reaching here.
        Assert.Equal(0m, DomainRules.DailyBilledMonthCoverage(0m, 0m, 0));
    }
}
