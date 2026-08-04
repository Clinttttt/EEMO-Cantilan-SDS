using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Who is behind, and by how many months — the figure behind "Accounts in arrears" on the Financial Reports and
/// "Overdue Vendors" on the dashboard.
///
/// <para>It used to be counted from PaymentRecord rows whose status was not Paid. Nothing writes such a row until
/// money is recorded, so a payor who simply never paid last month counted as zero months behind and appeared on
/// neither list — while the same stall's own compliance row showed the month as missed. These tests hold the two
/// to one rule, and they run against a real database because the rule is a query.</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class ArrearsCountingTests(PostgresFixture db)
{
    // A fixed "today" is not available to the repository (it reads PhilippineTime.Today), so the tests anchor on
    // the real clock: the year in progress, with the anchor month being this month. Any month before this one has
    // fully elapsed, which is what the count is about.
    private static readonly DateOnly Today = EEMOCantilanSDS.Domain.Common.PhilippineTime.Today;

    private Task<(Guid MunicipalityId, Guid FacilityId, Guid StallId)> SeedMonthlyStallAsync(
        string code, decimal monthlyRate, int contractStartMonth)
        => SeedMonthlyStallAsync(code, monthlyRate, new DateOnly(Today.Year, contractStartMonth, 1));

    private async Task<(Guid MunicipalityId, Guid FacilityId, Guid StallId)> SeedMonthlyStallAsync(
        string code, decimal monthlyRate, DateOnly contractStart)
    {
        var municipality = Municipality.Create(code, $"Municipality {code}", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant());

        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        // A monthly-rental facility: the rent is per stall, and a month is covered only by a fully-paid record.
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC",
            municipalityId: municipality.Id);
        var stall = Stall.Create(facility.Id, "7", monthlyRate, ApplicableFees.BaseRental,
            municipalityId: municipality.Id);
        var contract = Contract.Create(stall.Id, "Maria Santos", "Maria Santos",
            contractStart, 5, monthlyRate);

        await using (var tenant = db.CreateContext(municipality.Id))
        {
            tenant.Facilities.Add(facility);
            tenant.Stalls.Add(stall);
            tenant.Contracts.Add(contract);
            await tenant.SaveChangesAsync();
        }

        return (municipality.Id, facility.Id, stall.Id);
    }

    [SkippableFact]
    public async Task AMonthWithNoPaymentRecordAtAll_CountsAsBehind()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        Skip.If(Today.Month == 1, "No month of this year has elapsed yet; the count has nothing to report.");
        await db.ResetAsync();

        // Let from the start of the year and never paid: every elapsed month is owed, and not one of them has a
        // payment record — which is exactly the case the old count could not see.
        var seeded = await SeedMonthlyStallAsync("ARR-A", 2_400m, contractStartMonth: 1);

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var arrears = await new FacilityReportsRepository(read)
            .GetDelinquentStallsAsync(FacilityCode.TCC, Today.Year, Today.Month, CancellationToken.None);

        var row = Assert.Single(arrears);
        Assert.Equal("7", row.StallNo);
        Assert.Equal("Maria Santos", row.Occupant);
        Assert.Equal(Today.Month - 1, row.MonthsUnpaid);          // every elapsed month, none of them recorded
        Assert.True(row.OutstandingBalance > 0m, "the months owed carry a balance");
    }

    [SkippableFact]
    public async Task DebtFromLastYear_SurvivesTheYearBoundary()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        // A stall let 18 months ago that never paid. Counting from January of the anchor's year would have said it
        // was one month behind on the first of February — the count now walks a rolling twelve months, so the debt
        // does not reset when the calendar does.
        var start = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-18);
        var seeded = await SeedMonthlyStallAsync("ARR-F", 1_000m, contractStart: start);

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var arrears = await new FacilityReportsRepository(read)
            .GetDelinquentStallsAsync(FacilityCode.TCC, Today.Year, Today.Month, CancellationToken.None);

        var row = Assert.Single(arrears);
        Assert.Equal(12, row.MonthsUnpaid);                       // the whole rolling window is owed
        Assert.Equal(12_000m, row.OutstandingBalance);
    }

    [SkippableFact]
    public async Task AFutureAnchorMonth_CountsOnlyWhatHasElapsed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        Skip.If(Today.Month == 12, "December has no later month in its own year to anchor on.");
        await db.ResetAsync();

        // The Yearly view offers every month, including ones still ahead. The count and the money must be about
        // the same span, or a row reads "7 months" beside eight months of balance.
        var seeded = await SeedMonthlyStallAsync("ARR-G", 1_000m, contractStartMonth: 1);

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var repo = new FacilityReportsRepository(read);

        var asked = await repo.GetDelinquentStallsAsync(FacilityCode.TCC, Today.Year, 12, CancellationToken.None);
        var elapsed = await repo.GetDelinquentStallsAsync(FacilityCode.TCC, Today.Year, Today.Month, CancellationToken.None);

        Assert.Equal(
            Assert.Single(elapsed).MonthsUnpaid,
            Assert.Single(asked).MonthsUnpaid);
        Assert.Equal(Assert.Single(elapsed).OutstandingBalance, Assert.Single(asked).OutstandingBalance);
    }

    [SkippableFact]
    public async Task OneElapsedMonthOwed_IsEnoughToAppear()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        Skip.If(Today.Month == 1, "No month of this year has elapsed yet.");
        await db.ResetAsync();

        // Let from the month before this one: exactly one elapsed month is owed. "1–2 unpaid months" must include
        // one, which is the reported complaint — the list stayed empty until a second month went by.
        var seeded = await SeedMonthlyStallAsync("ARR-B", 2_400m, contractStartMonth: Today.Month - 1);

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var arrears = await new FacilityReportsRepository(read)
            .GetDelinquentStallsAsync(FacilityCode.TCC, Today.Year, Today.Month, CancellationToken.None);

        var row = Assert.Single(arrears);
        Assert.Equal(1, row.MonthsUnpaid);
        Assert.Equal(2_400m, row.OutstandingBalance);
    }

    [SkippableFact]
    public async Task AMonthNotYetUnderContract_IsNotOwed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        Skip.If(Today.Month < 3, "Needs at least two elapsed months to tell 'not yet let' from 'behind'.");
        await db.ResetAsync();

        // Let from this month: nothing has elapsed under this contract, so the stall is not behind at all.
        var seeded = await SeedMonthlyStallAsync("ARR-C", 2_400m, contractStartMonth: Today.Month);

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var arrears = await new FacilityReportsRepository(read)
            .GetDelinquentStallsAsync(FacilityCode.TCC, Today.Year, Today.Month, CancellationToken.None);

        Assert.Empty(arrears);
    }

    [SkippableFact]
    public async Task TheDashboardsAllFacilitiesQuery_SeesTheSameStall()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        Skip.If(Today.Month == 1, "No month of this year has elapsed yet.");
        await db.ResetAsync();

        // The dashboard asks for every facility at once (facility: null) — the same rule must answer.
        var seeded = await SeedMonthlyStallAsync("ARR-D", 2_400m, contractStartMonth: 1);

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var arrears = await new FacilityReportsRepository(read)
            .GetDelinquentStallsAsync(null, Today.Year, Today.Month, CancellationToken.None);

        var row = Assert.Single(arrears);
        Assert.Equal(FacilityCode.TCC, row.FacilityCode);
        Assert.True(row.MonthsUnpaid >= 1);
    }

    [SkippableFact]
    public async Task AClosedStallIsNotCountedHere_ItsDebtBelongsToTheClosedRegister()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        Skip.If(Today.Month == 1, "No month of this year has elapsed yet.");
        await db.ResetAsync();

        var seeded = await SeedMonthlyStallAsync("ARR-E", 2_400m, contractStartMonth: 1);

        await using (var write = db.CreateContext(seeded.MunicipalityId))
        {
            var stall = await write.Stalls.FindAsync(seeded.StallId);
            stall!.Close(Today, "tester");
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var repo = new FacilityReportsRepository(read);

        // Freezing a stall stops its obligation — the platform's rule everywhere (IsStallCollectableOn requires an
        // active stall), which is why a closed stall's own compliance row also reports no missed months. What it
        // still owes is reported by the Closed / Inactive Accounts register, which exists for exactly that and
        // states each closed account's uncollected balance. So this count leaves it out either way, and the flag
        // no longer resurrects a figure the rest of the system does not recognise.
        Assert.Empty(await repo.GetDelinquentStallsAsync(FacilityCode.TCC, Today.Year, Today.Month, CancellationToken.None));
        Assert.Empty(await repo.GetDelinquentStallsAsync(FacilityCode.TCC, Today.Year, Today.Month, includeClosed: true, CancellationToken.None));
    }

    [SkippableFact]
    public async Task ARenewedStall_CountsOnlyThePresentContract_NotTheSupersededOne()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        // Stall 23's shape from the office's own data: let in June 2023 for three years, then renewed onto a new
        // contract from the first of last month. The superseded occupancy is reported by the Closed / Inactive
        // register under its own lessee, with its own uncollected balance. Counting its months here as well billed
        // the same debt twice — the row read twelve months and ₱10,050 on a contract five weeks old.
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        var renewedFrom = thisMonth.AddMonths(-1);
        var seeded = await SeedMonthlyStallAsync("ARR-R", 900m, renewedFrom.AddYears(-3).AddDays(-6));

        await using (var write = db.CreateContext(seeded.MunicipalityId))
        {
            var superseded = write.Contracts.First(c => c.StallId == seeded.StallId);
            superseded.Terminate("test", renewedFrom.AddDays(-1));
            write.Contracts.Add(Contract.Create(
                seeded.StallId, "Maria Santos", "Maria Santos", renewedFrom, 3, 900m));
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var arrears = await new FacilityReportsRepository(read)
            .GetDelinquentStallsAsync(FacilityCode.TCC, Today.Year, Today.Month, CancellationToken.None);

        var row = Assert.Single(arrears);
        Assert.Equal(1, row.MonthsUnpaid);              // last month, under the contract in force
        Assert.Equal(900m, row.OutstandingBalance);     // one month's rent, not three years of it
    }

    [SkippableFact]
    public async Task ALapsedTermWithTheTenantStillThere_StaysInTheArrearsList()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();

        // Iceplant stall 7's shape: one contract, term run out, tenant still trading, nothing collected. The office
        // continues to collect from these accounts, so they must stay here — the register is for occupancies that
        // genuinely ended. Their months are counted to the term's end and no further.
        var lapsedOn = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-2);
        var seeded = await SeedMonthlyStallAsync("ARR-L", 900m, lapsedOn.AddYears(-3));

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var arrears = await new FacilityReportsRepository(read)
            .GetDelinquentStallsAsync(FacilityCode.TCC, Today.Year, Today.Month, CancellationToken.None);

        var row = Assert.Single(arrears);
        Assert.True(row.MonthsUnpaid >= 10, $"a lapsed account is still chased; counted {row.MonthsUnpaid}");
        Assert.True(row.OutstandingBalance > 0m, "and its balance is still stated");
    }

    [SkippableFact]
    public async Task AMonthPaidTowardsButNotSettled_StaysOutstanding_WithOnlyTheRestOwed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        Skip.If(Today.Month == 1, "No month of this year has elapsed yet.");
        await db.ResetAsync();

        // A market space let for ₱900 a month, collected daily. One day was collected last month — ₱30 of the
        // ₱900. The month is not settled: it stays outstanding, and what is owed drops to ₱870.
        var lastMonth = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);
        var seeded = await SeedNpmStallAsync("ARR-H", lastMonth);

        await using (var write = db.CreateContext(seeded.MunicipalityId))
        {
            var day = DailyCollection.Create(seeded.StallId, lastMonth.AddDays(9));
            day.MarkPaid("OR-ARR-H", Guid.NewGuid());
            write.DailyCollections.Add(day);
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext(seeded.MunicipalityId);
        var arrears = await new FacilityReportsRepository(read)
            .GetDelinquentStallsAsync(FacilityCode.NPM, Today.Year, Today.Month, CancellationToken.None);

        var row = Assert.Single(arrears);
        Assert.Equal(1, row.MonthsUnpaid);              // paid towards, not settled
        Assert.Equal(870m, row.OutstandingBalance);     // ₱900 owed less the ₱30 collected
    }

    /// <summary>A daily-collected market space at the ordinance ₱30/day, so its month is ₱900.</summary>
    private async Task<(Guid MunicipalityId, Guid StallId)> SeedNpmStallAsync(string code, DateOnly contractStart)
    {
        var municipality = Municipality.Create(code, $"Municipality {code}", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant());

        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM", municipalityId: municipality.Id);
        var stall = Stall.Create(facility.Id, "3", 900m, ApplicableFees.BaseRental,
            section: MarketSection.VegetableArea, municipalityId: municipality.Id);
        var contract = Contract.Create(stall.Id, "Dante Revilla", "Dante Revilla", contractStart, 3, 900m);

        await using (var tenant = db.CreateContext(municipality.Id))
        {
            tenant.Facilities.Add(facility);
            tenant.Stalls.Add(stall);
            tenant.Contracts.Add(contract);
            await tenant.SaveChangesAsync();
        }

        return (municipality.Id, stall.Id);
    }
}
