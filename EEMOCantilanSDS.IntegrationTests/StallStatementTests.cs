using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The statement of account agrees with the two other places the same money is stated: Follow-up, and the payor's own portal.
/// Against a real database because the figures are queries. The repository reads the real clock, so the tests anchor on it too.
/// </summary>
[Collection(PostgresCollection.Name)]
public class StallStatementTests(PostgresFixture db)
{
    private const decimal Rate = 1_000m;
    private static readonly DateOnly Today = PhilippineTime.Today;
    private static readonly DateOnly ThisMonth = new(Today.Year, Today.Month, 1);

    /// <summary>Let eighteen months ago and never paid: long enough to cross a year boundary and pass the ledger's rolling twelve months.</summary>
    private async Task<(Guid MunicipalityId, Guid StallId)> SeedBehindStallAsync(string code)
    {
        var municipality = Municipality.Create(code, $"Municipality {code}", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC", municipalityId: municipality.Id);
        var stall = Stall.Create(facility.Id, "7", Rate, ApplicableFees.BaseRental, municipalityId: municipality.Id);
        await using var tenant = db.CreateContext(municipality.Id);
        tenant.AddRange(facility, stall, Contract.Create(stall.Id, "Maria Santos", "Maria Santos", ThisMonth.AddMonths(-18), 5, Rate));
        await tenant.SaveChangesAsync();
        return (municipality.Id, stall.Id);
    }

    [SkippableFact]
    public async Task ThePreviousBalance_IsTheMoneyFollowUpStates()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var seeded = await SeedBehindStallAsync("STM-A");

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var reports = new FacilityReportsRepository(read);
        var statement = await reports.GetStallStatementAsync(seeded.StallId, CancellationToken.None);

        // The arguments GetFollowUpQueueQueryHandler passes, through the composer that writes its rows.
        var delinquency = await reports.GetDelinquentStallsAsync(null, Today.Year, Today.Month, includeClosed: true, wholeAccount: true, CancellationToken.None);
        var queue = FollowUpComposer.Compose(Today.Year, Today.Month, Today, delinquency, new Dictionary<FacilityCode, FacilityReportsDto>(), [], [], [], [], [], [], []);
        var followUp = queue.Items.Where(i => i.StallId == seeded.StallId).ToList();

        Assert.NotNull(statement);
        Assert.Equal(followUp.Where(i => i.ReasonKind == "arrears").Sum(i => i.Amount), statement.ArrearsAmount);
        Assert.Equal(followUp.Where(i => i.ReasonKind == "delinquent").Sum(i => i.Amount), statement.DelinquentAmount);
        Assert.Equal(18 * Rate, statement.PreviousBalance);
        Assert.Equal(18, statement.UnpaidMonths.Count);
        Assert.Equal(Rate, statement.CurrentCharges);
        Assert.Equal(19 * Rate, statement.TotalDue);
    }

    [SkippableFact]
    public async Task APartPayment_LowersOnlyTheMonthItWasFor()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var seeded = await SeedBehindStallAsync("STM-B");

        var lastMonth = ThisMonth.AddMonths(-1);
        var earlier = PaymentRecord.Create(seeded.StallId, lastMonth.Year, lastMonth.Month, Rate, "test");
        earlier.MarkPartiallyPaidOnline(400m, "test");
        var current = PaymentRecord.Create(seeded.StallId, ThisMonth.Year, ThisMonth.Month, Rate, "test");
        current.MarkPartiallyPaidOnline(300m, "test");
        await using (var write = db.CreateContext(seeded.MunicipalityId))
        {
            write.AddRange(earlier, current);
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var statement = await new FacilityReportsRepository(read).GetStallStatementAsync(seeded.StallId, CancellationToken.None);

        Assert.NotNull(statement);
        Assert.Equal(18 * Rate - 400m, statement.PreviousBalance);
        Assert.Equal((Rate, 300m, 700m), (statement.CurrentCharges, statement.Payments, statement.CurrentBalance));
        Assert.Equal(18 * Rate - 400m + 700m, statement.TotalDue);
    }

    [SkippableFact]
    public async Task ThePayorsPortal_StatesTheSameTotal()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var seeded = await SeedBehindStallAsync("STM-C");

        var payor = PayorUser.Create("Maria Santos", "09170000099", new HashedPassword("hash"));
        await using (var write = db.CreateContext(seeded.MunicipalityId))
        {
            write.Add(payor);
            write.Add(PayorStallLink.Create(payor.Id, seeded.StallId));
            await write.SaveChangesAsync();
        }

        await CountFromAsync(seeded.MunicipalityId, ThisMonth.AddMonths(-18));
        await using var read = db.CreateContext(seeded.MunicipalityId);
        var statement = await new FacilityReportsRepository(read).GetStallStatementAsync(seeded.StallId, CancellationToken.None);
        var balance = Assert.Single(await new PayorRepository(read, null!).GetBalancesAsync(payor.Id));   // a monthly stall never asks the settlement service

        Assert.Equal(statement!.TotalDue, balance.OutstandingBalance);
        Assert.Equal(0m, statement.BalanceBeforeRules);
    }

    [SkippableFact]
    public async Task TheOfficesTotalExceedsThePortals_ByExactlyWhatTheStatementSaysIsFromBeforeTheRules()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var seeded = await SeedBehindStallAsync("STM-R");
        var payor = PayorUser.Create("Maria Santos", "09170000097", new HashedPassword("hash"));
        await using (var write = db.CreateContext(seeded.MunicipalityId))
        {
            write.Add(payor);
            write.Add(PayorStallLink.Create(payor.Id, seeded.StallId));
            await write.SaveChangesAsync();
        }
        await CountFromAsync(seeded.MunicipalityId, ThisMonth.AddMonths(-2));

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var statement = await new FacilityReportsRepository(read).GetStallStatementAsync(seeded.StallId, CancellationToken.None);
        var balance = Assert.Single(await new PayorRepository(read, null!).GetBalancesAsync(payor.Id));

        Assert.Equal(19 * Rate, statement!.TotalDue);          // the office's figure is not cut
        Assert.Equal(16 * Rate, statement.BalanceBeforeRules);
        Assert.Equal(3 * Rate, balance.OutstandingBalance);    // two counted months and this one
    }

    private async Task CountFromAsync(Guid municipalityId, DateOnly month)
    {
        await using var write = db.CreateContext(municipalityId);
        write.Add(BillingPolicyVersion.Create(municipalityId, Today, Today, BillingPolicy.StallTrackDefaults with { CountsMonthsFrom = month }, null, Guid.NewGuid(), DateTime.UtcNow));
        await write.SaveChangesAsync();
    }

    /// <summary>A bill of 10 kWh at 12 (120) and 4 cu.m. at 25 (100).</summary>
    private static UtilityBill MeterBill(Guid stallId, DateOnly month) =>
        UtilityBill.Create(stallId, month.Year, month.Month, 0m, 10m, 12m, 0m, 4m, 25m, "test");

    [SkippableFact]
    public async Task MeterBills_AreTheirOwnLine_AndThePortalStatesTheSameTotal()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var seeded = await SeedBehindStallAsync("STM-F");

        var payor = PayorUser.Create("Maria Santos", "09170000098", new HashedPassword("hash"));
        await using (var write = db.CreateContext(seeded.MunicipalityId))
        {
            write.Add(payor);
            write.Add(PayorStallLink.Create(payor.Id, seeded.StallId));
            // An earlier month's bill stays owed, and this month's is owed too.
            write.AddRange(MeterBill(seeded.StallId, ThisMonth.AddMonths(-3)), MeterBill(seeded.StallId, ThisMonth));
            await write.SaveChangesAsync();
        }

        await CountFromAsync(seeded.MunicipalityId, ThisMonth.AddMonths(-18));
        await using var read = db.CreateContext(seeded.MunicipalityId);
        var statement = await new FacilityReportsRepository(read).GetStallStatementAsync(seeded.StallId, CancellationToken.None);
        var portal = new PayorRepository(read, null!);
        var balance = Assert.Single(await portal.GetBalancesAsync(payor.Id));

        Assert.NotNull(statement);
        Assert.Equal(2, statement.UnpaidUtilities.Count);
        Assert.Equal(440m, statement.UtilityBalance);
        Assert.Equal(18 * Rate, statement.PreviousBalance);   // rent is not moved by a meter bill, nor the bill by rent
        Assert.Equal(19 * Rate + 440m, statement.TotalDue);
        Assert.Equal(statement.TotalDue, balance.OutstandingBalance);
        var bills = (await portal.GetPayableItemsAsync(payor.Id)).Where(p => p.Kind == PayorPayableKind.NpmUtility).ToList();
        Assert.Equal(2, bills.Count);
        Assert.All(bills, b => Assert.False(b.PayableOnline));
    }

    [SkippableFact]
    public async Task ElectricityOverCollected_DoesNotHideWaterStillOwed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var seeded = await SeedBehindStallAsync("STM-G");

        // Water owes 100. Electricity has no charge and took 150 by mistake, the one way a bill's net balance goes negative.
        var lastMonth = ThisMonth.AddMonths(-1);
        var bill = UtilityBill.Create(seeded.StallId, lastMonth.Year, lastMonth.Month, 0m, 0m, 0m, 0m, 4m, 25m, "test");
        bill.RecordPayment("LEGACY-E", null, null, PaymentStatus.Partial, 150m, PaymentStatus.Unpaid, null, updatedBy: "test");
        await using (var write = db.CreateContext(seeded.MunicipalityId))
        {
            write.Add(bill);
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var statement = await new FacilityReportsRepository(read).GetStallStatementAsync(seeded.StallId, CancellationToken.None);

        Assert.Equal(100m, statement!.UtilityBalance);
    }

    [SkippableFact]
    public async Task AnEarlierOccupantsMeterBill_IsNotThisAccounts()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var seeded = await SeedBehindStallAsync("STM-H");

        await using (var write = db.CreateContext(seeded.MunicipalityId))
        {
            write.Add(MeterBill(seeded.StallId, ThisMonth.AddMonths(-19)));   // the month before this account began
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var statement = await new FacilityReportsRepository(read).GetStallStatementAsync(seeded.StallId, CancellationToken.None);

        Assert.Empty(statement!.UnpaidUtilities);
    }

    [SkippableFact]
    public async Task AnotherOfficesStall_HasNoStatementHere()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var theirs = await SeedBehindStallAsync("STM-D");
        var ours = await SeedBehindStallAsync("STM-E");

        await using var read = db.CreateContext(ours.MunicipalityId);

        Assert.Null(await new FacilityReportsRepository(read).GetStallStatementAsync(theirs.StallId, CancellationToken.None));
    }
}
