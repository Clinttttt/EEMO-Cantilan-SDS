using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The market's metered utilities, per payor, for the sheet the office files.
///
/// <para>
/// Electricity and water are billed per reading and settled on their own receipt, so the month-end sheet states them
/// as their own table. These tests hold two things: that a row says what that space was charged and what it paid, and
/// that the per-payor rows agree with the facility-wide totals the same repository reports. The two are computed
/// through one shared rule for exactly that reason — an office reading a total of ₱119 must find ₱119 in the rows.
/// </para>
/// </summary>
public class FacilityReportsUtilityRowsTests : RepositoryTestBase
{
    private static (Facility f, Stall s, Contract c) Space(string stallNo, string occupant)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, stallNo, 900m, ApplicableFees.BaseRental, MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, occupant, occupant, new DateOnly(2026, 1, 1), 3, 900m);
        return (facility, stall, contract);
    }

    private static UtilityBill Bill(Guid stallId, decimal elecFrom, decimal elecTo, decimal elecRate,
                                    decimal waterFrom, decimal waterTo, decimal waterRate) =>
        UtilityBill.Create(stallId, 2026, 6, elecFrom, elecTo, elecRate, waterFrom, waterTo, waterRate, "seed");

    [Fact]
    public async Task ARowStatesWhatTheSpaceWasChargedAndWhatItPaid()
    {
        var context = NewContext();
        var (facility, stall, contract) = Space("1", "Karmilita Log");
        context.AddRange(facility, stall, contract);

        // 20 kWh at ₱11 = ₱220 for electricity, settled in full. 3 m³ at ₱25 = ₱75 for water, nothing paid.
        var bill = Bill(stall.Id, 100m, 120m, 11m, 10m, 13m, 25m);
        bill.RecordPayment("OR-E1", null, null, PaymentStatus.Paid, 0m, PaymentStatus.Unpaid, 0m, null, "seed");
        context.Add(bill);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var rows = await repo.GetNpmUtilityRowsAsync(2026, 6, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("1", row.StallNo);
        Assert.Equal("Karmilita Log", row.Payor);
        Assert.Equal(220m, row.ElecCharge);
        Assert.Equal(220m, row.ElecPaid);
        Assert.Equal(75m, row.WaterCharge);
        Assert.Equal(0m, row.WaterPaid);
        Assert.Equal(295m, row.Charged);
        Assert.Equal(220m, row.Collected);
        Assert.Equal(75m, row.Balance);
        Assert.Equal("OR-E1", row.ORNumber);
    }

    [Fact]
    public async Task TheRowsAgreeWithTheFacilityWideTotals()
    {
        // The reconciliation that matters: whatever the office is shown as the market's utility position, it must be
        // able to find in the rows beneath it.
        var context = NewContext();
        var (facility, one, contractOne) = Space("1", "Kim Chui");
        var two = Stall.Create(facility.Id, "2", 900m, ApplicableFees.BaseRental, MarketSection.MeatSection);
        var contractTwo = Contract.Create(two.Id, "Justin Bieber", "Justin Bieber", new DateOnly(2026, 1, 1), 3, 900m);
        context.AddRange(facility, one, contractOne, two, contractTwo);

        var billOne = Bill(one.Id, 0m, 10m, 10m, 0m, 2m, 20m);      // ₱100 elec, ₱40 water
        billOne.RecordPayment("OR-1", null, null, PaymentStatus.Partial, 60m, PaymentStatus.Paid, 0m, null, "seed");
        var billTwo = Bill(two.Id, 0m, 5m, 10m, 0m, 1m, 20m);        // ₱50 elec, ₱20 water
        billTwo.RecordPayment(null, null, null, PaymentStatus.Unpaid, 0m, PaymentStatus.Unpaid, 0m, null, "seed");
        context.AddRange(billOne, billTwo);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var rows = await repo.GetNpmUtilityRowsAsync(2026, 6, CancellationToken.None);
        var totals = await repo.GetNpmUtilityTotalsAsync(2026, 6, CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Equal(totals.ElecCollected, rows.Sum(r => r.ElecPaid));
        Assert.Equal(totals.WaterCollected, rows.Sum(r => r.WaterPaid));
        Assert.Equal(totals.Outstanding, rows.Sum(r => r.Balance));
    }

    [Fact]
    public async Task AnotherMonthsBill_StaysOutOfThisSheet()
    {
        var context = NewContext();
        var (facility, stall, contract) = Space("1", "Rosa Magbanua");
        context.AddRange(facility, stall, contract);

        var may = UtilityBill.Create(stall.Id, 2026, 5, 0m, 50m, 10m, 0m, 5m, 20m, "seed");
        context.Add(may);
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var rows = await repo.GetNpmUtilityRowsAsync(2026, 6, CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task ABillWithNothingCharged_IsNotALineOnTheSheet()
    {
        // A bill raised with no consumption yet, or with a rate the office has not stated, charges nothing. A row of
        // zeros on a filed sheet invites a question that has no answer.
        var context = NewContext();
        var (facility, stall, contract) = Space("1", "Pedro Santos");
        context.AddRange(facility, stall, contract);
        context.Add(Bill(stall.Id, 100m, 100m, 11m, 10m, 10m, 25m));
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var rows = await repo.GetNpmUtilityRowsAsync(2026, 6, CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task AReadingThatWentBackwards_ChargesNothingRatherThanANegative()
    {
        var context = NewContext();
        var (facility, stall, contract) = Space("1", "Maria Velasco");
        context.AddRange(facility, stall, contract);
        // Electricity reads lower than last month (a replaced meter), water is ordinary.
        context.Add(Bill(stall.Id, 500m, 20m, 11m, 0m, 4m, 25m));
        await context.SaveChangesAsync();

        var repo = new FacilityReportsRepository(context);
        var rows = await repo.GetNpmUtilityRowsAsync(2026, 6, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(0m, row.ElecCharge);
        Assert.Equal(100m, row.WaterCharge);
    }
}
