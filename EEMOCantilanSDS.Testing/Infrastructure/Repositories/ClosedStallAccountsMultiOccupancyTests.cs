using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A physical stall outlives its lessees. When Stall 3 is handed to a new lessee, the previous lessee's account
/// must remain on the inactive register with THEIR OWN money — not disappear because the stall is occupied again,
/// and not be credited with the new lessee's collections. These cover the multi-occupancy history that the
/// original one-row-per-stall register could not express.
/// </summary>
public class ClosedStallAccountsMultiOccupancyTests : RepositoryTestBase
{
    private static Contract Term(Guid stallId, string occupant, DateOnly from, int years, decimal rate = 1_000m) =>
        Contract.Create(stallId, occupant, occupant, from, years, rate);

    [Fact]
    public async Task AReLetStall_KeepsThePreviousLesseeOnTheRegister()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "3", 1_000m, ApplicableFees.BaseRental);

        // Wilma held it 2023–2025 and was handed over on 30 Jun 2026; Teofila took it on 1 Jul 2026.
        var outgoing = Term(stall.Id, "Wilma K. Tecson", new DateOnly(2023, 1, 1), 3);
        outgoing.Terminate("Head", new DateOnly(2026, 6, 30));
        var incoming = Term(stall.Id, "Teofila Reyes", new DateOnly(2026, 7, 1), 3);

        context.AddRange(facility, stall, outgoing, incoming);
        await context.SaveChangesAsync();

        var rows = await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None);

        // The outgoing lessee is still on the register; the sitting lessee is not (their account is current).
        var row = Assert.Single(rows);
        Assert.Equal("Wilma K. Tecson", row.Occupant);
        Assert.Equal("3", row.StallNo);
        Assert.Equal(new DateOnly(2026, 6, 30), row.OccupancyEndedOn);   // the day the handover took effect
        Assert.DoesNotContain(rows, r => r.Occupant == "Teofila Reyes");

        // And it is history only: renewing or reopening it would act on the stall Teofila now holds.
        Assert.True(row.StallReLet);

        // The row names the term it is the record of — Wilma's. Anything acting on this lessee, such as placing her
        // in a stall of her own when she returns, reads that term; the stall's latest term is Teofila's.
        Assert.Equal(outgoing.Id, row.ContractId);
    }

    [Fact]
    public async Task AVacantStallsAccount_RemainsActionable()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "7", 1_000m, ApplicableFees.BaseRental);
        var lapsed = Term(stall.Id, "Ana Reyes", new DateOnly(2020, 1, 1), 1);

        context.AddRange(facility, stall, lapsed);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.False(row.StallReLet);   // nobody holds it, so Renew / Remove still apply
    }

    [Fact]
    public async Task AClosedOpenEndedAccount_IsNotTreatedAsItsOwnSuccessor()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "4", 1_500m, ApplicableFees.BaseRental);
        // A space let WITHOUT a contract — the office's "No contract (extension)". A term of zero years expires on the
        // day it takes effect, so the occupancy's window ends today and still counts as in force.
        var today = PhilippineTime.Today;
        var openEnded = Term(stall.Id, "Bernadette Lim", today, years: 0, rate: 1_500m);
        stall.Close(today, "head");

        context.AddRange(facility, stall, openEnded);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        // The flag asks whether somebody ELSE holds the stall. Counting this account's own occupancy made a closed
        // open-ended lessee her own successor: the register decided the space had been re-let, told the office it was
        // taken by the very lessee it had just closed, and offered "Assign new stall" as the only action — no way to
        // resume the account and no way to remove it.
        Assert.False(row.StallReLet);
        Assert.Equal(InactiveAccountState.Closed, row.State);
        Assert.Equal("Bernadette Lim", row.Occupant);
    }

    [Fact]
    public async Task EachLesseesMoneyStaysOnTheirOwnAccount()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "3", 1_000m, ApplicableFees.BaseRental);

        var outgoing = Term(stall.Id, "Wilma K. Tecson", new DateOnly(2026, 1, 1), 1);
        outgoing.Terminate("Head", new DateOnly(2026, 3, 31));
        var incoming = Term(stall.Id, "Teofila Reyes", new DateOnly(2026, 4, 1), 3);

        // January (Wilma's) paid in full; April (Teofila's) paid in full — the same stall, different lessees.
        var jan = PaymentRecord.Create(stall.Id, 2026, 1, 1_000m); jan.UpdateStatus(PaymentStatus.Paid);
        var apr = PaymentRecord.Create(stall.Id, 2026, 4, 1_000m); apr.UpdateStatus(PaymentStatus.Paid);

        context.AddRange(facility, stall, outgoing, incoming, jan, apr);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.Equal("Wilma K. Tecson", row.Occupant);
        // Only January's ₱1,000 — April belongs to the incoming lessee and must not appear here.
        Assert.Equal(1_000m, row.LifetimeCollected);
    }

    [Fact]
    public async Task AnArrearSettledAfterTheHandover_StillBelongsToTheOutgoingLessee()
    {
        // The attribution rule: a payment belongs to the BILLING period it was raised for, never to the day the
        // money arrived. February's arrear paid in August is still February's lessee's payment.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "3", 1_000m, ApplicableFees.BaseRental);

        var outgoing = Term(stall.Id, "Wilma K. Tecson", new DateOnly(2026, 1, 1), 1);
        outgoing.Terminate("Head", new DateOnly(2026, 3, 31));
        var incoming = Term(stall.Id, "Teofila Reyes", new DateOnly(2026, 4, 1), 3);

        var feb = PaymentRecord.Create(stall.Id, 2026, 2, 1_000m);
        feb.UpdateStatus(PaymentStatus.Paid);           // settled in August, months after the handover

        context.AddRange(facility, stall, outgoing, incoming, feb);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.Equal("Wilma K. Tecson", row.Occupant);
        Assert.Equal(1_000m, row.LifetimeCollected);     // credited to February's lessee
    }

    [Fact]
    public async Task ADailyStall_AttributesCollectionsByTheirBusinessDate()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "3", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);

        // Mid-month handover: Wilma to 15 Jun, Teofila from 16 Jun.
        var outgoing = Term(stall.Id, "Wilma K. Tecson", new DateOnly(2026, 6, 1), 1, 900m);
        outgoing.Terminate("Head", new DateOnly(2026, 6, 15));
        var incoming = Term(stall.Id, "Teofila Reyes", new DateOnly(2026, 6, 16), 3, 900m);

        var wilmaDay = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 10));
        wilmaDay.MarkPaid(string.Empty, collectorId: null);
        var teofilaDay = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 20));
        teofilaDay.MarkPaid(string.Empty, collectorId: null);

        context.AddRange(facility, stall, outgoing, incoming, wilmaDay, teofilaDay);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.Equal("Wilma K. Tecson", row.Occupant);
        Assert.Equal(FeeRates.NpmDailyFee, row.LifetimeCollected);   // only the 10th — the 20th is the new lessee's
    }

    [Fact]
    public async Task ACurrentlyVacantStall_StillReportsItsLastLessee()
    {
        // The original behaviour, which must not regress: a stall nobody holds keeps its last account on the list.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "9", 1_000m, ApplicableFees.BaseRental);
        var lapsed = Term(stall.Id, "Ana Reyes", new DateOnly(2020, 1, 1), 1);   // ran out years ago

        context.AddRange(facility, stall, lapsed);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.Equal("Ana Reyes", row.Occupant);
        // A vacant stall whose last term simply lapsed: still collected, so it reads Lapsed rather than finished.
        Assert.Equal(InactiveAccountState.Lapsed, row.State);
    }

    [Fact]
    public async Task AHandedOverOccupancy_ReadsAsSuperseded_NotLapsed()
    {
        // The distinction decides money: a superseded account is finished, so the Financial Reports state its
        // balance under closed accounts and the arrears list leaves its months to the register. A lapsed account is
        // still being collected, so the reverse holds. Reporting both as "Expired" is what let the same debt be
        // counted twice — once as ₱1,905,300 of closed balances and once inside the follow-up total.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "23", 900m, ApplicableFees.BaseRental, section: MarketSection.VegetableArea);

        var handedOverOn = new DateOnly(2026, 6, 30);
        var outgoing = Contract.Create(stall.Id, "Vincent E. Doloriel", "Vincent E. Doloriel", new DateOnly(2023, 6, 7), 3, 900m);
        outgoing.Terminate("Head", handedOverOn);
        var sitting = Contract.Create(stall.Id, "Teofila Reyes", "Teofila Reyes", handedOverOn.AddDays(1), 3, 900m);

        context.AddRange(facility, stall, outgoing, sitting);
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        Assert.Equal("Vincent E. Doloriel", row.Occupant);
        Assert.Equal(InactiveAccountState.Superseded, row.State);
    }

    [Fact]
    public async Task AStallWithOnlyACurrentLessee_IsNotOnTheRegisterAtAll()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "11", 1_000m, ApplicableFees.BaseRental);
        var live = Term(stall.Id, "Pedro Gallardo", PhilippineTime.Today.AddYears(-1), 5);

        context.AddRange(facility, stall, live);
        await context.SaveChangesAsync();

        Assert.Empty(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AMarketClosureDay_IsNotChargedAsArrears()
    {
        // The market was shut, so nobody owes anything for that day. The Record-payment dialog has always skipped
        // closures; charging them here made this register state a larger debt than the dialog could collect — two
        // screens disagreeing about one account.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "4", 900m, ApplicableFees.BaseRental, section: MarketSection.VegetableArea);

        var term = Term(stall.Id, "Dennis S. Doloriel", new DateOnly(2026, 1, 1), 1);
        term.Terminate("Head", new DateOnly(2026, 1, 10));      // a ten-day occupancy: 1–10 January

        context.AddRange(facility, stall, term);
        context.Add(NpmMarketClosure.Create(new DateOnly(2026, 1, 5), remarks: "Fiesta"));
        await context.SaveChangesAsync();

        var row = Assert.Single(await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None));

        // Nine chargeable days, not ten.
        Assert.Equal(9 * FeeRates.NpmDailyFee, row.Uncollected);
    }
}
