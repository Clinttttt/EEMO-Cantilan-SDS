using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Domain.Common.Billing;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing.Application.Fees;

/// <summary>
/// The two ways an office may measure what a daily-collected market month owes.
///
/// <para>
/// Both are real ordinances and both are right for the office that stated them, so these tests state each basis in its own
/// words and, where the two differ, put them side by side. February is where they differ most and the difference is money,
/// which is why it is asserted first.
/// </para>
///
/// <para>
/// The office chose to support both knowing exactly that: on pure days a 28-day February owes ₱840 with no month-end
/// top-up, and a year collects 365 installments rather than 360. On that basis this is the rule and not a shortfall.
/// </para>
/// </summary>
public class DailyBilledMonthRuleTests
{
    private const decimal Daily = 30m;
    private const decimal Rent = 900m;

    private static IDailyBilledMonthRule RentGoal => DailyBilledMonthRules.For(NpmMonthBasis.RentGoal);
    private static IDailyBilledMonthRule PureDays => DailyBilledMonthRules.For(NpmMonthBasis.PureDays);

    // ── February, where the two bases part company ───────────────────────────────────────────────────────────────────

    [Fact]
    public void February_OnTheRentGoal_OwesTheWholeRent()
    {
        // Twenty-eight installments reach ₱840; the remaining ₱60 is carried as the month-end adjustment, so the month
        // still owes the ₱900 the space is let for.
        Assert.Equal(900m, RentGoal.Obligation(Daily, Rent, daysInMonth: 28, daysHeld: 28));
    }

    [Fact]
    public void February_OnPureDays_OwesItsTwentyEightDays()
    {
        Assert.Equal(840m, PureDays.Obligation(Daily, Rent, daysInMonth: 28, daysHeld: 28));
    }

    [Fact]
    public void OnlyTheRentGoalCarriesAMonthEndAdjustment()
    {
        // The flag the settlement service reads. On pure days a short month is simply a shorter month, so an adjustment
        // there would invent money the office's own rule does not ask for.
        Assert.True(RentGoal.AdjustsShortMonthToRent);
        Assert.False(PureDays.AdjustsShortMonthToRent);
    }

    // ── A long month ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AThirtyFirstDay_OnTheRentGoal_AddsNoInstallment()
    {
        Assert.Equal(900m, RentGoal.Obligation(Daily, Rent, daysInMonth: 31, daysHeld: 31));
    }

    [Fact]
    public void AThirtyFirstDay_OnPureDays_IsOwedLikeEveryOtherDay()
    {
        Assert.Equal(930m, PureDays.Obligation(Daily, Rent, daysInMonth: 31, daysHeld: 31));
    }

    [Fact]
    public void AThirtyDayMonthIsTheOneMonthTheTwoBasesAgreeOn()
    {
        // ₱30 × 30 = ₱900 either way, which is why an office can be on the wrong basis for a whole month of the year and
        // never notice. It is also why the basis is asked for rather than guessed.
        Assert.Equal(RentGoal.Obligation(Daily, Rent, 30, 30), PureDays.Obligation(Daily, Rent, 30, 30));
    }

    // ── Part of a month ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PartOfAMonthIsTheDaysHeld_OnEitherBasis()
    {
        // A mid-month start, a lapsed term, a space handed over. Both bases charge the days held, one installment each.
        Assert.Equal(300m, RentGoal.Obligation(Daily, Rent, daysInMonth: 31, daysHeld: 10));
        Assert.Equal(300m, PureDays.Obligation(Daily, Rent, daysInMonth: 31, daysHeld: 10));
    }

    [Fact]
    public void PureDaysNeverChargesMoreDaysThanTheMonthHas()
    {
        // A caller counting an occupancy can hand over a longer span than the calendar. A month cannot owe thirty-two fees.
        Assert.Equal(930m, PureDays.Obligation(Daily, Rent, daysInMonth: 31, daysHeld: 40));
    }

    [Fact]
    public void NothingHeldOwesNothing_OnEitherBasis()
    {
        Assert.Equal(0m, RentGoal.Obligation(Daily, Rent, 31, 0));
        Assert.Equal(0m, PureDays.Obligation(Daily, Rent, 31, 0));
        Assert.Equal(0m, PureDays.Obligation(Daily, Rent, 0, 5));
        Assert.Equal(0m, PureDays.Obligation(0m, Rent, 31, 31));
    }

    // ── A monthly amount means nothing on pure days ─────────────────────────────────────────────────────────────────

    [Fact]
    public void PureDaysIgnoresAStatedMonthlyRentEntirely()
    {
        // An office may have stated one under an earlier basis. It must not quietly start deciding what a month owes: on
        // this basis the days are the obligation, whatever figure is left in the rate table.
        Assert.Equal(930m, PureDays.Obligation(Daily, monthlyRent: 5_000m, daysInMonth: 31, daysHeld: 31));
        Assert.Equal(930m, PureDays.Obligation(Daily, monthlyRent: 0m, daysInMonth: 31, daysHeld: 31));
    }

    [Fact]
    public void OnlyTheRentGoalHasAMonthlyAmountToShow()
    {
        // What the screens read to decide whether a monthly field belongs on the form at all.
        Assert.True(RentGoal.HasMonthlyGoal);
        Assert.False(PureDays.HasMonthlyGoal);
    }

    // ── The report's coverage column ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Coverage_OnTheRentGoal_IsTheRentLessTheExcusedDays()
    {
        Assert.Equal(900m, RentGoal.Coverage(Daily, Rent, daysInMonth: 31, excusedDays: 0));
        Assert.Equal(840m, RentGoal.Coverage(Daily, Rent, daysInMonth: 31, excusedDays: 2));
    }

    [Fact]
    public void Coverage_OnPureDays_IsTheMonthsOwnDaysLessTheExcusedOnes()
    {
        Assert.Equal(930m, PureDays.Coverage(Daily, Rent, daysInMonth: 31, excusedDays: 0));
        Assert.Equal(870m, PureDays.Coverage(Daily, Rent, daysInMonth: 31, excusedDays: 2));
        Assert.Equal(840m, PureDays.Coverage(Daily, Rent, daysInMonth: 28, excusedDays: 0));
    }

    [Fact]
    public void Coverage_AWhollyExcusedMonthReferencesNothing()
    {
        Assert.Equal(0m, PureDays.Coverage(Daily, Rent, daysInMonth: 28, excusedDays: 28));
        Assert.Equal(0m, PureDays.Coverage(Daily, Rent, daysInMonth: 28, excusedDays: 40));
    }

    // ── The default, which is what keeps every existing office where it was ─────────────────────────────────────────

    [Fact]
    public void AnOfficeThatHasStatedNothingIsOnTheRentGoal()
    {
        Assert.Equal(NpmMonthBasis.RentGoal, DailyBilledMonthRules.Default.Basis);
        Assert.Equal(NpmMonthBasis.RentGoal, DailyBilledMonthRules.For(default).Basis);
    }

    [Fact]
    public void AnUnrecognisedBasisFallsBackToTheRentGoalRatherThanRepricingAMarket()
    {
        // A new member added to the enum later must not silently re-price a live market on the day it is deployed.
        Assert.Equal(NpmMonthBasis.RentGoal, DailyBilledMonthRules.For((NpmMonthBasis)99).Basis);
    }

    [Fact]
    public void EachBasisReportsItself()
    {
        Assert.Equal(NpmMonthBasis.RentGoal, RentGoal.Basis);
        Assert.Equal(NpmMonthBasis.PureDays, PureDays.Basis);
    }

    // ── The snapshot every billing path asks ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheSnapshotCarriesTheBasisItWasBuiltWith()
    {
        // The wiring, not the arithmetic. Every path that bills asks FeeRateSnapshot.MonthRule, so a snapshot that
        // discarded the basis would put every office back on the rent goal while the screens said otherwise - and no test
        // of the rules themselves would notice. Written after an injection proof did exactly that.
        var entries = Array.Empty<FeeRateEntry>();

        Assert.Equal(NpmMonthBasis.PureDays,
            new FeeRateSnapshot(entries, null, NpmMonthBasis.PureDays).MonthRule.Basis);

        Assert.Equal(NpmMonthBasis.RentGoal,
            new FeeRateSnapshot(entries, null, NpmMonthBasis.RentGoal).MonthRule.Basis);
    }

    [Fact]
    public void ASnapshotBuiltWithoutABasisIsOnTheRentGoal()
    {
        // Every older caller and every test builds one this way, which is what keeps Cantilan and each live tenant exactly
        // where they were.
        Assert.Equal(NpmMonthBasis.RentGoal, new FeeRateSnapshot(Array.Empty<FeeRateEntry>()).MonthRule.Basis);
        Assert.True(new FeeRateSnapshot(Array.Empty<FeeRateEntry>()).MonthRule.HasMonthlyGoal);
    }
}
