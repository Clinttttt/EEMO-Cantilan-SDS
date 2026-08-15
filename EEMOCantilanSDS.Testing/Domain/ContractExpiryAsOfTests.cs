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
