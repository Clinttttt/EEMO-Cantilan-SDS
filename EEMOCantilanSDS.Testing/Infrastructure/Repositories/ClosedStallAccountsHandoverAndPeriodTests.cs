using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Three things the inactive-account register must get right about money, each of which the office reconciles
/// against by hand:
///   • a month is charged and credited ONCE — a mid-month handover does not bill both lessees for it;
///   • an ended occupancy is charged the rent IT was let at, not the rate the space carries now;
///   • a period-scoped reading states what that period owed, and omits occupancies that did not exist in it.
/// </summary>
public class ClosedStallAccountsHandoverAndPeriodTests : RepositoryTestBase
{
    private static Contract Term(Guid stallId, string occupant, DateOnly from, int years, decimal rate = 1_000m) =>
        Contract.Create(stallId, occupant, occupant, from, years, rate);

    // Wilma held Stall 3 from 1 Jan 2026 and handed it to Teofila mid-June; the space is Teofila's now.
    private static (Facility Facility, Stall Stall, Contract Outgoing, Contract Incoming) MidJuneHandover(decimal rate = 1_000m)
    {
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "3", rate, ApplicableFees.BaseRental);

        var outgoing = Term(stall.Id, "Wilma K. Tecson", new DateOnly(2026, 1, 1), 1, rate);
        outgoing.Terminate("Head", new DateOnly(2026, 6, 15));
        var incoming = Term(stall.Id, "Teofila Reyes", new DateOnly(2026, 6, 16), 3, rate);

        return (facility, stall, outgoing, incoming);
    }

    [Fact]
    public async Task AHandoverMonth_IsChargedOnce_ToTheLesseeWhoBeganLatestWithinIt()
    {
        var context = NewContext();
        var (facility, stall, outgoing, incoming) = MidJuneHandover();
        context.AddRange(facility, stall, outgoing, incoming);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.Equal("Wilma K. Tecson", row.Occupant);
        // January to May: five months. June is the handover month and belongs to Teofila, who holds the stall —
        // charging it here as well billed one month's rent twice over the same space.
        Assert.Equal(5_000m, row.Uncollected);
    }

    [Fact]
    public async Task AHandoverMonthsPayment_IsCreditedOnce()
    {
        var context = NewContext();
        var (facility, stall, outgoing, incoming) = MidJuneHandover();

        var june = PaymentRecord.Create(stall.Id, 2026, 6, 1_000m);
        june.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, stall, outgoing, incoming, june);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        // June's money is the sitting lessee's, so the departed lessee's account is credited nothing …
        Assert.Equal(0m, row.LifetimeCollected);
        // … and still owes only her own five months.
        Assert.Equal(5_000m, row.Uncollected);
    }

    [Fact]
    public async Task AnEndedOccupancy_IsChargedTheRentItWasLetAt_NotTheSpacesCurrentRate()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "3", 900m, ApplicableFees.BaseRental);

        // Wilma was let the space at ₱900 and left at the end of March …
        var outgoing = Term(stall.Id, "Wilma K. Tecson", new DateOnly(2026, 1, 1), 1, 900m);
        outgoing.Terminate("Head", new DateOnly(2026, 3, 31));
        // … and the space was re-let to Teofila at ₱1,500, which rewrote the rate the STALL carries.
        var incoming = Term(stall.Id, "Teofila Reyes", new DateOnly(2026, 4, 1), 3, 1_500m);
        stall.UpdateRates(1_500m, null, "Head");

        context.AddRange(facility, stall, outgoing, incoming);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.Equal("Wilma K. Tecson", row.Occupant);
        Assert.Equal(900m, row.MonthlyRate);            // her own rent, stated as the record has it
        Assert.Equal(2_700m, row.Uncollected);          // January to March at ₱900 — not at Teofila's ₱1,500
    }

    // A lapsed account the register has always shown in full: let 1 Jun 2023 for three years, nothing ever
    // collected, nobody has taken the space since.
    private static (Facility Facility, Stall Stall, Contract Term) LapsedThreeYearTerm()
    {
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "3", 1_000m, ApplicableFees.BaseRental);
        var term = Term(stall.Id, "Ramil C. Orjeles", new DateOnly(2023, 6, 1), 3);
        return (facility, stall, term);
    }

    [Fact]
    public async Task ThePeriodScopedRegister_StatesOnlyThatPeriodsCharges()
    {
        var context = NewContext();
        var (facility, stall, term) = LapsedThreeYearTerm();
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);

        // In full: June 2023 to June 2026 inclusive — thirty-seven months.
        var lifetime = Assert.Single(await repo.GetClosedStallAccountsAsync(CancellationToken.None));
        Assert.Equal(37_000m, lifetime.Uncollected);

        // 2026 owes only January to June, the months of that year the term ran.
        var year2026 = Assert.Single(await repo.GetClosedStallAccountsForPeriodAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None));
        Assert.Equal(6_000m, year2026.Uncollected);

        // 2023 owes June to December — the term had not begun in January.
        var year2023 = Assert.Single(await repo.GetClosedStallAccountsForPeriodAsync(
            new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31), CancellationToken.None));
        Assert.Equal(7_000m, year2023.Uncollected);

        // A single month owes one month.
        var march2025 = Assert.Single(await repo.GetClosedStallAccountsForPeriodAsync(
            new DateOnly(2025, 3, 1), new DateOnly(2025, 3, 31), CancellationToken.None));
        Assert.Equal(1_000m, march2025.Uncollected);

        // The occupancy's own dates stay facts on every reading — only the FIGURES are scoped.
        Assert.Equal(new DateOnly(2023, 6, 1), year2026.EffectivityDate);
        Assert.Equal(new DateOnly(2026, 6, 1), year2026.OccupancyEndedOn);
    }

    [Fact]
    public async Task AnOccupancyThatDidNotExistInThePeriod_IsNotListed()
    {
        var context = NewContext();
        var (facility, stall, term) = LapsedThreeYearTerm();
        context.AddRange(facility, stall, term);
        await context.SaveChangesAsync();

        // 2021: nobody held this space, so listing the account — with any figure at all — would state a debt for a
        // year in which it could not have been owed.
        Assert.Empty(await new StallRepository(context).GetClosedStallAccountsForPeriodAsync(
            new DateOnly(2021, 1, 1), new DateOnly(2021, 12, 31), CancellationToken.None));
    }

    [Fact]
    public async Task ThePeriodScopedRegister_CreditsOnlyThatPeriodsMoney()
    {
        var context = NewContext();
        var (facility, stall, term) = LapsedThreeYearTerm();

        var paid2024 = PaymentRecord.Create(stall.Id, 2024, 5, 1_000m);
        paid2024.UpdateStatus(PaymentStatus.Paid);
        var paid2026 = PaymentRecord.Create(stall.Id, 2026, 2, 1_000m);
        paid2026.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, stall, term, paid2024, paid2026);
        await context.SaveChangesAsync();

        var repo = new StallRepository(context);

        var year2026 = Assert.Single(await repo.GetClosedStallAccountsForPeriodAsync(
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), CancellationToken.None));
        Assert.Equal(1_000m, year2026.LifetimeCollected);   // February 2026 only
        Assert.Equal(5_000m, year2026.Uncollected);         // the year's other five months

        var lifetime = Assert.Single(await repo.GetClosedStallAccountsAsync(CancellationToken.None));
        Assert.Equal(2_000m, lifetime.LifetimeCollected);   // both payments, on the cumulative reading
        Assert.Equal(35_000m, lifetime.Uncollected);
    }
}
