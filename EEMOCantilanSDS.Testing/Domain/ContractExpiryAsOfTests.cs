using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A contract's expiry, asked OF A DATE.
///
/// <para>
/// These were properties reading the static Philippine clock until 2026-08-15, which made them impossible to state honestly:
/// whether a contract still accrues is a question the office asks of past months as well as of today — a register for June
/// cannot be answered with August's opinion — and a test could only ever assert whatever day it happened to run on.
/// </para>
///
/// <para>
/// The boundary is the one the office's own paper takes: a term effective the 7th runs THROUGH the 7th N years on. Expiring a
/// day early would put a lessee in the inactive register while still under contract, and a day late would keep collecting.
/// </para>
/// </summary>
public class ContractExpiryAsOfTests
{
    private static readonly DateOnly Start = new(2023, 6, 7);

    private static Contract ThreeYearTerm() => Contract.Create(
        Guid.NewGuid(), "Rosa Magbanua", nameOnContract: "Rosa Magbanua",
        Start, durationYears: 3, monthlyRate: 900m, arrangement: OccupancyArrangement.SignedContract);

    private static Contract OpenEnded() => Contract.Create(
        Guid.NewGuid(), "Joy Ruaza", nameOnContract: null,
        Start, durationYears: 0, monthlyRate: 900m, arrangement: OccupancyArrangement.SpaceOnly);

    [Theory]
    [InlineData(2024, 6, 7, false)]    // a year in: plainly live
    [InlineData(2026, 6, 6, false)]    // the day before the anniversary
    [InlineData(2026, 6, 7, false)]    // the anniversary itself is still INSIDE the term
    [InlineData(2026, 6, 8, true)]     // the day after it ends
    [InlineData(2030, 1, 1, true)]     // long past
    public void ExpiryIsDecidedByTheDateItIsAskedOf(int year, int month, int day, bool expected)
        => Assert.Equal(expected, ThreeYearTerm().IsExpiredOn(new DateOnly(year, month, day)));

    [Fact]
    public void TheSameContractAnswersDifferentlyForDifferentDates()
    {
        // The whole point of taking the date: one contract, two answers, neither of them "whenever the server was asked".
        var contract = ThreeYearTerm();

        Assert.False(contract.IsExpiredOn(new DateOnly(2025, 1, 1)));
        Assert.True(contract.IsExpiredOn(new DateOnly(2027, 1, 1)));
    }

    [Theory]
    // Three months is the office's renewal window (DomainRules.ExpiringSoonMonths).
    [InlineData(2026, 3, 6, false)]    // more than three months out: not yet the office's concern
    [InlineData(2026, 3, 7, true)]     // exactly three months before expiry
    [InlineData(2026, 6, 7, true)]     // the last day of the term
    [InlineData(2026, 6, 8, false)]    // already EXPIRED, so no longer "expiring soon"
    public void ExpiringSoonIsTheRenewalWindowAndStopsAtExpiry(int year, int month, int day, bool expected)
        => Assert.Equal(expected, ThreeYearTerm().IsExpiringSoonOn(new DateOnly(year, month, day)));

    [Fact]
    public void AnExpiredTermIsNeverAlsoExpiringSoon()
    {
        // They must not both be true, or an account would be listed twice - once to chase, once to renew.
        var contract = ThreeYearTerm();
        var wellPast = new DateOnly(2027, 1, 1);

        Assert.True(contract.IsExpiredOn(wellPast));
        Assert.False(contract.IsExpiringSoonOn(wellPast));
    }

    // ── The office's ruling of 2026-08-16: a signed contract of nought years is invalid. ──────────────────────────────────
    //
    // The two ways the platform expressed expiry used to disagree about exactly that row: the entity computed it from
    // ExpiryDate and called the term expired the day after it began, while DomainRules.TermHasExpired said a term stating no
    // years had not run out. Both readings were defensible, which is why the office was asked. Now the state is refused at
    // entry, and the entity delegates to the shared rule so there is only one answer to disagree about.

    [Fact]
    public void ASignedContractOfNoughtYearsIsRefused()
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(() => Contract.Create(
            Guid.NewGuid(), "Merlita A. Abuso", "Merlita A. Abuso",
            Start, durationYears: 0, monthlyRate: 900m));

        // The message has to tell the office what to do instead, because there IS a right way to record this.
        Assert.Contains("space-only", refused.Message);
    }

    [Fact]
    public void ASignedContractCannotBeCORRECTEDToNoughtYearsEither()
    {
        // The door this actually came through. The edit form's validator allowed nought while every other path required at
        // least one year, and UpdateTerms refused only negatives — so an existing, valid contract could be edited into the
        // invalid state even though none could be created in it.
        var contract = ThreeYearTerm();

        Assert.Throws<ArgumentOutOfRangeException>(() => contract.UpdateTerms(Start, 0, "Admin"));
        Assert.Equal(3, contract.DurationYears);        // and the refusal left the contract untouched
    }

    [Fact]
    public void CorrectingASpaceOnlyOccupancysDateKeepsItOpenEnded()
    {
        // Nought years is legitimate HERE — it means no term was stated — so this correction must not be refused, and the
        // occupancy must not come back with a nought-year term either. It keeps the open-ended sentinel, which is what keeps
        // it out of expiry and renewal work.
        var contract = OpenEnded();
        var corrected = Start.AddDays(-30);

        contract.UpdateTerms(corrected, 0, "Admin");

        Assert.Equal(corrected, contract.EffectivityDate);
        Assert.Equal(DomainRules.OpenEndedTermYears, contract.DurationYears);
        Assert.False(contract.IsExpiredOn(Start.AddYears(50)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void TheEntityAndTheSharedRuleGiveTHESAMEAnswer(int years)
    {
        // Pinned so the two expressions cannot drift apart again: whatever the entity says about expiry, DomainRules must say
        // too, on both sides of the boundary and on the boundary itself.
        var contract = Contract.Create(
            Guid.NewGuid(), "Merlita A. Abuso", "Merlita A. Abuso",
            Start, durationYears: years, monthlyRate: 900m);

        foreach (var asOf in new[]
                 {
                     Start, Start.AddDays(1), contract.ExpiryDate.AddDays(-1),
                     contract.ExpiryDate, contract.ExpiryDate.AddDays(1), contract.ExpiryDate.AddYears(5)
                 })
        {
            Assert.Equal(
                DomainRules.TermHasExpired(contract.EffectivityDate, contract.DurationYears, asOf),
                contract.IsExpiredOn(asOf));
        }
    }

    [Fact]
    public void AStoredRowWithNoStatedTerm_GetsTHESAMEAnswerFromBoth()
    {
        // The one row the two rules actually disagreed about, and the only way it can still arrive: written before the
        // invariant existed and read straight back out by EF, which never consults the factory. The entity said "expired the
        // day after it began"; the shared rule said a term stating no years has not run out.
        //
        // They now agree, and they agree on NOT expired — the safe reading for a row the office may still hold. A space with no
        // stated term owes nothing (ContractBillingMonthsTests) and must not be reported as an expired contract needing
        // renewal, because there is no term to have run out and none to renew.
        var storedRow = StoredRowWithNoStatedTerm(Start);

        Assert.Equal(0, storedRow.DurationYears);

        foreach (var asOf in new[] { Start, Start.AddDays(1), Start.AddYears(1), Start.AddYears(20) })
        {
            Assert.Equal(
                DomainRules.TermHasExpired(storedRow.EffectivityDate, storedRow.DurationYears, asOf),
                storedRow.IsExpiredOn(asOf));

            Assert.False(storedRow.IsExpiredOn(asOf));
        }
    }

    /// <summary>
    /// A contract as the database hands one back: materialised, not constructed. Deliberately does NOT go through
    /// <c>Contract.Create</c>, whose invariants such a row predates.
    /// </summary>
    private static Contract StoredRowWithNoStatedTerm(DateOnly start)
    {
        var row = (Contract)Activator.CreateInstance(typeof(Contract), nonPublic: true)!;
        Set(nameof(Contract.EffectivityDate), start);
        Set(nameof(Contract.DurationYears), 0);
        return row;

        void Set(string property, object value) =>
            typeof(Contract).GetProperty(property)!.SetValue(row, value);
    }

    [Fact]
    public void AnOpenEndedOccupancyNeverExpiresAndNeverFallsDueForRenewal()
    {
        // A space let without a signed contract carries the open-ended sentinel. It must not quietly become "expiring soon"
        // decades later, which is what a real expiry date would eventually do.
        var contract = OpenEnded();

        foreach (var asOf in new[] { new DateOnly(2023, 6, 7), new DateOnly(2050, 12, 31), new DateOnly(2099, 1, 1) })
        {
            Assert.False(contract.IsExpiredOn(asOf));
            Assert.False(contract.IsExpiringSoonOn(asOf));
        }
    }

    [Fact]
    public void AStallIsExpiredOnlyWhenEveryActiveTermHasLapsed()
    {
        // The stall-level rule, which the roster, the inactive register and the remove-stall guard all read. It takes the same
        // date, so all three can answer for the same day.
        var stall = Stall.Create(Guid.NewGuid(), "1", 900m, ApplicableFees.BaseRental, section: MarketSection.VegetableArea);
        stall.Contracts.Add(ThreeYearTerm());

        Assert.False(stall.IsContractExpired(new DateOnly(2026, 6, 7)));
        Assert.True(stall.IsContractExpired(new DateOnly(2026, 6, 8)));
    }

    [Fact]
    public void AStallWithNoContractIsNotExpired()
    {
        // Vacant is not expired: a space nobody holds has no term to have run out.
        var stall = Stall.Create(Guid.NewGuid(), "2", 900m, ApplicableFees.BaseRental, section: MarketSection.VegetableArea);

        Assert.False(stall.IsContractExpired(new DateOnly(2030, 1, 1)));
    }
}
