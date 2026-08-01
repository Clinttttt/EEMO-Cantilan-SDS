using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Whose name appears against money already collected. A stall outlives its lessees, so reading the payor from the
/// stall's CURRENT contract puts a former lessee's receipts and collections under the sitting lessee's name — on the
/// missing-receipt queue and on the stall's own collection history, which are exactly the lists the office reads back
/// when reconciling. Every row must be named after whoever was answerable for that day or that billing month.
/// </summary>
public class OccupantAttributionTests : RepositoryTestBase
{
    private const decimal Daily = FeeRates.NpmDailyFee;

    private static Contract Term(Guid stallId, string occupant, DateOnly from, int years, decimal rate = 1_000m) =>
        Contract.Create(stallId, occupant, occupant, from, years, rate);

    /// <summary>A stall Wilma held until 30 June 2026, and Teofila has held since 1 July 2026.</summary>
    private static (Stall stall, Contract outgoing, Contract incoming) ReLet(Facility facility, string stallNo = "3")
    {
        var stall = Stall.Create(facility.Id, stallNo, 1_000m, ApplicableFees.BaseRental);
        var outgoing = Term(stall.Id, "Wilma K. Tecson", new DateOnly(2024, 1, 1), 3);
        outgoing.Terminate("Head", new DateOnly(2026, 6, 30));
        var incoming = Term(stall.Id, "Teofila Reyes", new DateOnly(2026, 7, 1), 3);
        return (stall, outgoing, incoming);
    }

    [Fact]
    public async Task AReceiptlessPayment_IsListedUnderTheLesseeOfItsBillingMonth()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var (stall, outgoing, incoming) = ReLet(facility);

        // A May 2026 rental, paid, with no receipt number yet — squarely inside Wilma's occupancy.
        var record = PaymentRecord.Create(stall.Id, 2026, 5, 1_000m);
        record.RecordPayment(orNumber: string.Empty, collectorId: Guid.NewGuid(), status: PaymentStatus.Paid);

        context.AddRange(facility, stall, outgoing, incoming, record);
        await context.SaveChangesAsync();

        var rows = await new PaymentRepository(context).GetUnreceiptedCashPaymentsAsync(2026, 5, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("Wilma K. Tecson", row.Occupant);      // was: "Teofila Reyes", who was not there yet
    }

    [Fact]
    public async Task TheWholeYearReceiptList_NamesEachMonthsOwnLessee()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var (stall, outgoing, incoming) = ReLet(facility);

        var hers = PaymentRecord.Create(stall.Id, 2026, 5, 1_000m);
        hers.RecordPayment(orNumber: string.Empty, collectorId: Guid.NewGuid(), status: PaymentStatus.Paid);
        var his = PaymentRecord.Create(stall.Id, 2026, 8, 1_000m);
        his.RecordPayment(orNumber: string.Empty, collectorId: Guid.NewGuid(), status: PaymentStatus.Paid);

        context.AddRange(facility, stall, outgoing, incoming, hers, his);
        await context.SaveChangesAsync();

        var rows = await new PaymentRepository(context).GetUnreceiptedCashPaymentsForYearAsync(2026, CancellationToken.None);

        Assert.Equal("Wilma K. Tecson", Assert.Single(rows, r => r.Month == 5).Occupant);
        Assert.Equal("Teofila Reyes", Assert.Single(rows, r => r.Month == 8).Occupant);
    }

    [Fact]
    public async Task AStallsCollectionHistory_NamesEachDayAfterTheLesseeOfThatDay()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.BaseRental, section: MarketSection.FishSection, dailyRate: Daily);
        var outgoing = Term(stall.Id, "Wilma K. Tecson", new DateOnly(2024, 1, 1), 3, 900m);
        outgoing.Terminate("Head", new DateOnly(2026, 6, 30));
        var incoming = Term(stall.Id, "Teofila Reyes", new DateOnly(2026, 7, 1), 3, 900m);

        var hers = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 20), "Head", Daily);
        hers.MarkPaid("OR-1", Guid.NewGuid());
        var his = DailyCollection.Create(stall.Id, new DateOnly(2026, 7, 20), "Head", Daily);
        his.MarkPaid("OR-2", Guid.NewGuid());

        context.AddRange(facility, stall, outgoing, incoming, hers, his);
        await context.SaveChangesAsync();

        var page = await new PaymentRepository(context)
            .GetStallCollectionHistoryAsync(stall.Id, cursor: null, pageSize: 20, CancellationToken.None);

        Assert.Equal("Wilma K. Tecson", Assert.Single(page.Items, r => r.ORNumber == "OR-1").PayorName);
        Assert.Equal("Teofila Reyes", Assert.Single(page.Items, r => r.ORNumber == "OR-2").PayorName);
    }

    [Fact]
    public async Task AStallWithOneLessee_IsUnaffected()
    {
        // The ordinary case must read exactly as before: one occupancy, one name on every row.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "9", 1_000m, ApplicableFees.BaseRental);
        var only = Term(stall.Id, "Ana Reyes", new DateOnly(2025, 1, 1), 5);

        var record = PaymentRecord.Create(stall.Id, 2026, 5, 1_000m);
        record.RecordPayment(orNumber: string.Empty, collectorId: Guid.NewGuid(), status: PaymentStatus.Paid);

        context.AddRange(facility, stall, only, record);
        await context.SaveChangesAsync();

        var rows = await new PaymentRepository(context).GetUnreceiptedCashPaymentsAsync(2026, 5, CancellationToken.None);

        Assert.Equal("Ana Reyes", Assert.Single(rows).Occupant);
    }

    [Fact]
    public async Task TheLedgerSummary_CreditsOnlyTheSittingLesseesOwnDays()
    {
        // A handover mid-month: the ledger panel is the sitting lessee's account, so the previous occupant's paid
        // days in that same month must not be counted as theirs.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.BaseRental, section: MarketSection.MeatSection, dailyRate: Daily);

        var today = PhilippineTime.Today;
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var handover = thisMonth.AddDays(9);                     // they took over on the 10th

        var outgoing = Term(stall.Id, "Wilma K. Tecson", thisMonth.AddYears(-1), 3, 900m);
        outgoing.Terminate("Head", handover.AddDays(-1));
        var incoming = Term(stall.Id, "Teofila Reyes", handover, 3, 900m);

        // One day paid by each, both inside the current calendar month.
        var hers = DailyCollection.Create(stall.Id, thisMonth, "Head", Daily);
        hers.MarkPaid("OR-A", Guid.NewGuid());
        var his = DailyCollection.Create(stall.Id, handover, "Head", Daily);
        his.MarkPaid("OR-B", Guid.NewGuid());

        context.AddRange(facility, stall, outgoing, incoming, hers, his);
        await context.SaveChangesAsync();

        var summary = await new PaymentRepository(context).GetStallLedgerSummaryAsync(stall.Id, CancellationToken.None);

        // Only the sitting lessee's own day is credited — not both.
        Assert.Equal(Daily, summary.TotalCollected);
    }
}
