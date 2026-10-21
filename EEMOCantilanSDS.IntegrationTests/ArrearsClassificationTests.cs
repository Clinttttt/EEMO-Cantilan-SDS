using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// IA-019 / IA-020 / IA-069 against a real database: every unpaid obligation is classified on its own - previous-calendar-year rent and
/// the rent of ended occupancies or expired contracts are Arrears; fully elapsed current-year months of a live account are Delinquent; the
/// month in progress is neither. The clock is fixed (19 October 2026) so the year boundary is exercised the same way on every run.
/// The classification is read-side only: nothing is posted, reclassified or moved between official revenue lines.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ArrearsClassificationTests(PostgresFixture db)
{
    private static readonly DateOnly Now = new(2026, 10, 19);

    private sealed class FixedClock(DateOnly today) : IClock
    {
        public DateOnly PhilippineToday => today;
        public DateTime UtcNow => DateTime.SpecifyKind(today.ToDateTime(new TimeOnly(4, 0)), DateTimeKind.Utc);
        public DateTime PhilippineNow => DateTime.SpecifyKind(today.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Unspecified);
    }

    private static FacilityReportsRepository Reports(AppDbContext context) => new(context, new FeeRateResolver(context), new FixedClock(Now));

    private static Task<IReadOnlyList<Application.Dtos.Facilities.DelinquentStallDto>> Whole(AppDbContext context, FacilityCode? facility = null) =>
        Reports(context).GetDelinquentStallsAsync(facility, Now.Year, Now.Month, includeClosed: true, wholeAccount: true, default);

    private async Task<(Guid Tenant, Guid Facility)> SeedTenantAsync(string code, FacilityCode facility, BillingArchetype? archetype = null)
    {
        var municipality = Municipality.Create(code, $"Municipality {code}", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Municipalities.Add(municipality); await setup.SaveChangesAsync(); }
        var f = Facility.Create(facility, facility.ToString(), facility.ToString(), archetype: archetype, municipalityId: municipality.Id);
        await using (var tenant = db.CreateContext(municipality.Id))
        {
            tenant.Facilities.Add(f);
            if (facility == FacilityCode.NPM)
                tenant.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, new DateOnly(2020, 1, 1), municipality.Id));
            await tenant.SaveChangesAsync();
        }
        return (municipality.Id, f.Id);
    }

    private static PaymentRecord Paid(Guid stall, int year, int month, decimal rent)
    {
        var r = PaymentRecord.Create(stall, year, month, rent, "test");
        r.UpdateStatus(PaymentStatus.Paid, 0m, updatedBy: "test");
        return r;
    }

    private static PaymentRecord Part(Guid stall, int year, int month, decimal rent, decimal paid)
    {
        var r = PaymentRecord.Create(stall, year, month, rent, "test");
        r.UpdateStatus(PaymentStatus.Partial, paid, updatedBy: "test");
        return r;
    }

    // Occupant paid every month except Nov 2025 (no record), Dec 2025 (400 of 1,000), Apr 2026 (no record) and May 2026 (300 of 1,000).
    private async Task<Guid> SeedMixedAccountAsync(Guid tenant, Guid facility, string stallNo)
    {
        var stall = Stall.Create(facility, stallNo, 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        var contract = Contract.Create(stall.Id, "Maria Santos", "Maria Santos", new DateOnly(2025, 11, 1), 5, 1_000m);
        await using var write = db.CreateContext(tenant);
        write.Stalls.Add(stall); write.Contracts.Add(contract);
        write.PaymentRecords.AddRange(
            Part(stall.Id, 2025, 12, 1_000m, 400m),
            Paid(stall.Id, 2026, 1, 1_000m), Paid(stall.Id, 2026, 2, 1_000m), Paid(stall.Id, 2026, 3, 1_000m),
            Part(stall.Id, 2026, 5, 1_000m, 300m),
            Paid(stall.Id, 2026, 6, 1_000m), Paid(stall.Id, 2026, 7, 1_000m), Paid(stall.Id, 2026, 8, 1_000m), Paid(stall.Id, 2026, 9, 1_000m));
        await write.SaveChangesAsync();
        return stall.Id;
    }

    [SkippableTheory]
    [InlineData(FacilityCode.TCC, null)]
    [InlineData(FacilityCode.NPM, BillingArchetype.MonthlyRental)]       // a Public Market rented by the month behaves exactly like a monthly facility
    public async Task A_live_tenant_with_previous_year_and_current_year_months_owes_both_categories_and_partial_payments_reduce_each_month(
        FacilityCode code, BillingArchetype? archetype)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var (tenant, facility) = await SeedTenantAsync("ARR-MIX", code, archetype);
        await SeedMixedAccountAsync(tenant, facility, "7");

        await using var read = db.CreateContext(tenant);
        var row = Assert.Single(await Whole(read, code));

        Assert.True(row.Classified);
        // Previous calendar year: Nov 2025 (nothing paid) + Dec 2025 (₱400 of ₱1,000 paid) = ₱1,000 + ₱600.
        Assert.Equal((2, 1_600m), (row.ArrearsMonths, row.ArrearsAmount));
        Assert.Equal(("2025-11", "2025-12"), (row.ArrearsFirstPeriod, row.ArrearsLastPeriod));
        // Current year, fully elapsed: Apr 2026 (nothing) + May 2026 (₱300 of ₱1,000) = ₱1,000 + ₱700. October (the month in progress) is neither.
        Assert.Equal((2, 1_700m), (row.DelinquentMonths, row.DelinquentAmount));
        Assert.Equal(("2026-04", "2026-05"), (row.DelinquentFirstPeriod, row.DelinquentLastPeriod));
        // The two categories partition the account: nothing double counted, nothing dropped.
        Assert.Equal((4, 3_300m), (row.MonthsUnpaid, row.OutstandingBalance));
        Assert.Equal(row.OutstandingBalance, row.ArrearsAmount + row.DelinquentAmount);
        Assert.False(row.TermLapsed);
    }

    [SkippableFact]
    public async Task An_expired_contract_makes_every_unpaid_month_Arrears_even_in_the_current_year()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var (tenant, facility) = await SeedTenantAsync("ARR-LAP", FacilityCode.TCC);
        var stall = Stall.Create(facility, "9", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        // Term Oct 2025 - Sep 2026: it ran out on 30 September while the tenant stayed and never paid.
        var contract = Contract.Create(stall.Id, "Pedro Reyes", "Pedro Reyes", new DateOnly(2025, 10, 1), 1, 1_000m);
        await using (var write = db.CreateContext(tenant)) { write.Stalls.Add(stall); write.Contracts.Add(contract); await write.SaveChangesAsync(); }

        await using var read = db.CreateContext(tenant);
        var row = Assert.Single(await Whole(read));

        Assert.True(row.TermLapsed);
        Assert.Equal((12, 12_000m), (row.ArrearsMonths, row.ArrearsAmount));      // Jan-Sep 2026 are current-year months, yet the contract has expired
        Assert.Equal((0, 0m), (row.DelinquentMonths, row.DelinquentAmount));
        Assert.Equal(row.OutstandingBalance, row.ArrearsAmount);
    }

    [SkippableFact]
    public async Task A_report_read_as_of_an_earlier_month_judges_the_contract_term_by_that_date_not_by_today()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var (tenant, facility) = await SeedTenantAsync("ARR-ASOF", FacilityCode.TCC);
        var stall = Stall.Create(facility, "9", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        // Term Oct 2025 - Sep 2026: expired by 19 October 2026, but still running when June 2026 was read.
        var contract = Contract.Create(stall.Id, "Pedro Reyes", "Pedro Reyes", new DateOnly(2025, 10, 1), 1, 1_000m);
        await using (var write = db.CreateContext(tenant)) { write.Stalls.Add(stall); write.Contracts.Add(contract); await write.SaveChangesAsync(); }

        await using var read = db.CreateContext(tenant);
        var june = Assert.Single(await Reports(read).GetDelinquentStallsAsync(FacilityCode.TCC, 2026, 6, includeClosed: true, wholeAccount: true, default));

        Assert.False(june.TermLapsed);
        Assert.Equal((3, 3_000m), (june.ArrearsMonths, june.ArrearsAmount));          // Oct-Dec 2025: previous calendar year
        Assert.Equal((5, 5_000m), (june.DelinquentMonths, june.DelinquentAmount));    // Jan-May 2026: elapsed months of a live term
    }

    [SkippableFact]
    public async Task A_new_occupant_does_not_inherit_the_former_occupants_arrears_and_the_two_accounts_add_up_once()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var (tenant, facility) = await SeedTenantAsync("ARR-OCC", FacilityCode.TCC);
        var stall = Stall.Create(facility, "11", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        // Anna held the stall from January 2025 until it was handed to Ben on 1 April 2026; neither ever paid.
        var anna = Contract.Create(stall.Id, "Anna Cruz", "Anna Cruz", new DateOnly(2025, 1, 1), 5, 1_000m);
        var ben = Contract.Create(stall.Id, "Ben Lopez", "Ben Lopez", new DateOnly(2026, 4, 1), 5, 1_000m);
        await using (var write = db.CreateContext(tenant)) { write.Stalls.Add(stall); write.Contracts.AddRange(anna, ben); await write.SaveChangesAsync(); }

        await using var read = db.CreateContext(tenant);
        var live = Assert.Single(await Whole(read));
        var closed = Assert.Single(await new StallRepository(read, new FeeRateResolver(read), new FixedClock(Now))
            .GetClosedStallAccountsAsync(default), a => a.Occupant == "Anna Cruz");

        // Ben owes only his own term: April-September 2026 are current-year elapsed months of a live account => Delinquent.
        Assert.Equal("Ben Lopez", live.Occupant);
        Assert.Equal((0, 0m, 6, 6_000m), (live.ArrearsMonths, live.ArrearsAmount, live.DelinquentMonths, live.DelinquentAmount));
        // Anna's debt stays with Anna and her period (January 2025 - March 2026, 15 months): an ended occupancy => Arrears, listed by the register.
        Assert.Equal(15_000m, closed.Uncollected);
        Assert.Equal(InactiveAccountState.Superseded, closed.State);
        Assert.NotEqual(live.OutstandingBalance, closed.Uncollected);                // never merged into one figure
    }

    [SkippableFact]
    public async Task A_daily_market_month_is_classified_on_what_its_rent_still_owes_after_partial_collections()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var (tenant, facility) = await SeedTenantAsync("ARR-NPM", FacilityCode.NPM, BillingArchetype.DailyStall);
        var stall = Stall.Create(facility, "3", 900m, ApplicableFees.BaseRental, section: MarketSection.VegetableArea, municipalityId: tenant);
        var contract = Contract.Create(stall.Id, "Dante Revilla", "Dante Revilla", new DateOnly(2025, 11, 1), 3, 900m);
        await using (var write = db.CreateContext(tenant))
        {
            write.Stalls.Add(stall); write.Contracts.Add(contract);
            // Every month is settled by a fully paid monthly record, except: Nov 2025 (nothing), Dec 2025 (10 days = ₱300 collected), Apr 2026 (nothing).
            foreach (var (y, m) in new[] { (2026, 1), (2026, 2), (2026, 3), (2026, 5), (2026, 6), (2026, 7), (2026, 8), (2026, 9) })
                write.PaymentRecords.Add(Paid(stall.Id, y, m, 900m));
            for (var d = 1; d <= 10; d++)
            {
                var day = DailyCollection.Create(stall.Id, new DateOnly(2025, 12, d), "test", 30m);
                day.MarkPaid("OR-ARR-NPM", Guid.NewGuid());
                write.DailyCollections.Add(day);
            }
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext(tenant);
        var row = Assert.Single(await Whole(read, FacilityCode.NPM));

        // A month's obligation is its ₱900 rent (RentGoal): Nov 2025 owes ₱900, Dec 2025 owes ₱600 after the ₱300 collected => Arrears ₱1,500.
        Assert.Equal((2, 1_500m), (row.ArrearsMonths, row.ArrearsAmount));
        // Apr 2026 owes ₱900 and is the only current-year elapsed unpaid month => Delinquent.
        Assert.Equal((1, 900m), (row.DelinquentMonths, row.DelinquentAmount));
        // A daily account's balance also accrues the days already elapsed in October (current-period outstanding): it is neither category,
        // so the classified money is the balance less that accrual, and never more than it.
        Assert.Equal(2_400m, row.ArrearsAmount + row.DelinquentAmount);
        Assert.InRange(row.OutstandingBalance - 2_400m, 0m, 30m * Now.Day);
    }

    [SkippableFact]
    public async Task An_account_that_owes_nothing_before_the_current_month_is_in_no_category()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var (tenant, facility) = await SeedTenantAsync("ARR-NIL", FacilityCode.TCC);
        var stall = Stall.Create(facility, "2", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        var contract = Contract.Create(stall.Id, "Lia Gomez", "Lia Gomez", new DateOnly(2026, 9, 1), 5, 1_000m);
        await using (var write = db.CreateContext(tenant))
        {
            write.Stalls.Add(stall); write.Contracts.Add(contract);
            write.PaymentRecords.Add(Paid(stall.Id, 2026, 9, 1_000m));                // October is the month in progress and unpaid
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext(tenant);
        Assert.Empty(await Whole(read));                                              // current-period unpaid is never delinquent and never Arrears
    }

    [SkippableFact]
    public async Task The_rolling_and_whole_account_readers_agree_on_the_delinquent_part_and_only_the_whole_account_states_all_arrears()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var (tenant, facility) = await SeedTenantAsync("ARR-SCP", FacilityCode.TCC);
        await SeedMixedAccountAsync(tenant, facility, "7");

        await using var read = db.CreateContext(tenant);
        var whole = Assert.Single(await Whole(read));
        var rolling = Assert.Single(await Reports(read).GetDelinquentStallsAsync(FacilityCode.TCC, Now.Year, Now.Month, default));

        Assert.Equal(whole.DelinquentAmount, rolling.DelinquentAmount);                // current-year months sit inside any twelve-month window
        Assert.Equal(whole.DelinquentMonths, rolling.DelinquentMonths);
        Assert.True(rolling.ArrearsAmount <= whole.ArrearsAmount);                     // the window can only miss older Arrears, which is why the queue now reads the whole account
    }

    [SkippableFact]
    public async Task An_ended_occupancy_only_enters_historical_arrears_after_its_actual_end()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var (tenant, facility) = await SeedTenantAsync("ARR-HIST-END", FacilityCode.TCC);
        var expiredLater = Stall.Create(facility, "20", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        var expiredLaterContract = Contract.Create(expiredLater.Id, "Future expired", "Future expired", new DateOnly(2025, 10, 1), 1, 1_000m);
        var endedEarlier = Stall.Create(facility, "21", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        var endedEarlierContract = Contract.Create(endedEarlier.Id, "Already expired", "Already expired", new DateOnly(2024, 1, 1), 1, 1_000m);
        var reletLater = Stall.Create(facility, "22", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        var previous = Contract.Create(reletLater.Id, "Handover later", "Handover later", new DateOnly(2025, 1, 1), 5, 1_000m);
        var incoming = Contract.Create(reletLater.Id, "New occupant", "New occupant", new DateOnly(2026, 9, 1), 5, 1_000m);
        await using (var write = db.CreateContext(tenant))
        {
            write.Stalls.AddRange(expiredLater, endedEarlier, reletLater);
            write.Contracts.AddRange(expiredLaterContract, endedEarlierContract, previous, incoming);
            await write.SaveChangesAsync();
        }
        await using var read = db.CreateContext(tenant);
        var repository = new StallRepository(read, new FeeRateResolver(read), new FixedClock(Now));
        var historical = await repository.GetClosedStallAccountsAsOfAsync(new DateOnly(2026, 6, 30), default);
        var prior = Assert.Single(historical);
        Assert.Equal("Already expired", prior.Occupant);
        Assert.Equal(12_000m, prior.Uncollected);
        var october = await repository.GetClosedStallAccountsAsOfAsync(Now, default);
        Assert.Contains(october, x => x.Occupant == "Future expired");
        Assert.Contains(october, x => x.Occupant == "Handover later");
    }
}
