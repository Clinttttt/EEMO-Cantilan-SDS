using EEMOCantilanSDS.Application.Command.Payments.RecordPayAhead;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Payments.GetPayAheadOptions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>Rent taken ahead at the office, a year included (IA-071): identifiable months on one receipt, recorded whole or not at all, and never twice on a retry.</summary>
[Collection(PostgresCollection.Name)]
public class PayAheadTests(PostgresFixture db)
{
    private const decimal Rate = 1_000m;
    private static readonly DateOnly Today = PhilippineTime.Today;
    private static readonly DateOnly ThisMonth = new(Today.Year, Today.Month, 1);

    private sealed class Caller(Guid tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? MunicipalityId => tenant;
        public string? Username => "pay-ahead-test";
        public string? Role => "Admin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "TEST";
        public AdminUserDto? GetCurrentUser() => null;
    }

    private sealed class Pinned(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid value) { }
    }

    private sealed class QuietCache : IEemoCacheInvalidator
    {
        public Task InvalidateRegionAsync(string region, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePeriodAsync(string t, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateFacilityPeriodAsync(string t, FacilityCode f, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePaymentAffectedViewsAsync(string t, FacilityCode? f, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateReferenceDataAsync(string t, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateTenantAsync(string t, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Code : ITenantContext { public string TenantCode => "pay-ahead-test"; }

    private static BillingRules Rules(AppDbContext context, Guid tenant)
    {
        var clock = new SystemClock();
        return new(new(context, new Caller(tenant), new Pinned(tenant), clock), new FacilityReportsRepository(context), context, clock);
    }

    private static async Task<PayAheadOptionsDto> OptionsAsync(AppDbContext context, Guid tenant, Guid stall) =>
        (await new GetPayAheadOptionsQueryHandler(new StallRepository(context), Rules(context, tenant))
            .Handle(new GetPayAheadOptionsQuery(stall), CancellationToken.None)).Value!;

    private async Task<Result<bool>> RecordAsync(Guid tenant, Guid stall, DateOnly first, int months, decimal total, string or = "OR-1001")
    {
        await using var context = db.CreateContext(tenant);
        return await new RecordPayAheadCommandHandler(new PaymentRepository(context), new StallRepository(context), Rules(context, tenant),
                new Caller(tenant), new UnitOfWork(context), new QuietCache(), new Code())
            .Handle(new RecordPayAheadCommand(stall, first.Year, first.Month, months, total, or), CancellationToken.None);
    }

    private async Task<List<PaymentRecord>> RowsAsync(Guid tenant, Guid stall)
    {
        await using var read = db.CreateContext(tenant);
        return await read.PaymentRecords.AsNoTracking().Where(r => r.StallId == stall).OrderBy(r => r.BillingYear).ThenBy(r => r.BillingMonth).ToListAsync();
    }

    private async Task AddAsync(Guid tenant, params object[] rows)
    {
        await using var write = db.CreateContext(tenant);
        write.AddRange(rows);
        await write.SaveChangesAsync();
    }

    /// <summary>A lessee whose term began this month, so nothing is older, unless a test starts it earlier.</summary>
    private async Task<(Guid Tenant, Guid Stall)> SeedAsync(string code, DateOnly? termStart = null, int years = 5, FacilityCode facilityCode = FacilityCode.TCC)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var municipality = Municipality.Create(code, $"Municipality {code}", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var facility = Facility.Create(facilityCode, "A facility", facilityCode.ToString(), municipalityId: municipality.Id);
        var stall = Stall.Create(facility.Id, "7", Rate, ApplicableFees.BaseRental, municipalityId: municipality.Id);
        await AddAsync(municipality.Id, facility, stall, Contract.Create(stall.Id, "Maria Santos", "Maria Santos", termStart ?? ThisMonth, years, Rate));
        return (municipality.Id, stall.Id);
    }

    private static PaymentRecord Paid(Guid stall, DateOnly month)
    {
        var record = PaymentRecord.Create(stall, month.Year, month.Month, Rate, "test");
        record.UpdateStatus(PaymentStatus.Paid, 0m, null, "test", null);
        return record;
    }

    [SkippableFact]
    public async Task AYear_IsTwelveIdentifiableMonths_OnOneReceipt()
    {
        var seeded = await SeedAsync("AHD-A");

        var result = await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 12, 12_000m);

        Assert.True(result.IsSuccess, result.Error);
        var rows = await RowsAsync(seeded.Tenant, seeded.Stall);
        Assert.Equal(Enumerable.Range(0, 12).Select(i => (ThisMonth.AddMonths(i).Year, ThisMonth.AddMonths(i).Month)), rows.Select(r => (r.BillingYear, r.BillingMonth)));
        Assert.All(rows, r => Assert.Equal((PaymentStatus.Paid, Rate, "OR-1001"), (r.Status, r.BalanceDue + r.AmountPaid, r.ORNumber)));
        await using var read = db.CreateContext(seeded.Tenant);
        Assert.Empty(await read.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task ARetry_AfterSuccess_IsRefused_AndPaysNothingMore()
    {
        var seeded = await SeedAsync("AHD-B");
        Assert.True((await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 2, 2_000m)).IsSuccess);

        var retry = await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 2, 2_000m);

        Assert.Equal(ResultStatus.Conflict, retry.Status);
        Assert.Equal(2, (await RowsAsync(seeded.Tenant, seeded.Stall)).Count);
    }

    [SkippableFact]
    public async Task TheNextPayment_StartsAfterTheMonthsAlreadyPaid()
    {
        var seeded = await SeedAsync("AHD-C");
        Assert.True((await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 2, 2_000m)).IsSuccess);

        await using var read = db.CreateContext(seeded.Tenant);
        var options = await OptionsAsync(read, seeded.Tenant, seeded.Stall);

        var next = ThisMonth.AddMonths(2);
        Assert.Equal((next.Year, next.Month), (options.Months[0].Year, options.Months[0].Month));
        // +2 and +3 are within the three-month limit; a year is its own permission.
        Assert.Equal([1, 2, 12], options.Counts);
    }

    [SkippableFact]
    public async Task AnOlderOpenMonth_HoldsItAllBack_WithTheMonthToPayFirst()
    {
        var older = ThisMonth.AddMonths(-2);
        var seeded = await SeedAsync("AHD-D", older);
        await AddAsync(seeded.Tenant, CountingFrom(seeded.Tenant, older));

        await using var read = db.CreateContext(seeded.Tenant);
        var options = await OptionsAsync(read, seeded.Tenant, seeded.Stall);
        var result = await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 1, Rate);

        Assert.Empty(options.Months);
        Assert.Contains(older.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture), options.HeldBy);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Empty(await RowsAsync(seeded.Tenant, seeded.Stall));
    }

    [SkippableFact]
    public async Task AnOlderMonthFromBeforeTheRulesBegin_HoldsNothingBack()
    {
        var seeded = await SeedAsync("AHD-G", ThisMonth.AddMonths(-2));
        await AddAsync(seeded.Tenant, CountingFrom(seeded.Tenant, ThisMonth));

        await using var read = db.CreateContext(seeded.Tenant);
        var options = await OptionsAsync(read, seeded.Tenant, seeded.Stall);

        Assert.Null(options.HeldBy);
        Assert.Equal((ThisMonth.Year, ThisMonth.Month), (options.Months[0].Year, options.Months[0].Month));
        Assert.True((await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 1, Rate)).IsSuccess);
    }

    private static BillingPolicyVersion CountingFrom(Guid tenant, DateOnly month) =>
        BillingPolicyVersion.Create(tenant, Today, Today, BillingPolicy.StallTrackDefaults with { CountsMonthsFrom = month }, null, Guid.NewGuid(), DateTime.UtcNow);

    [SkippableFact]
    public async Task AnOfficeThatTakesAnyMonth_IsNotHeldBack_ByAnOlderOne()
    {
        var seeded = await SeedAsync("AHD-E", ThisMonth.AddMonths(-2));
        await AddAsync(seeded.Tenant, BillingPolicyVersion.Create(seeded.Tenant, Today, Today, BillingPolicy.StallTrackDefaults with { OldestMonthFirst = false }, null, Guid.NewGuid(), DateTime.UtcNow));

        Assert.True((await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 1, Rate)).IsSuccess);
    }

    [SkippableFact]
    public async Task AYear_NeedsItsOwnPermission_AndTheLimitStillApplies()
    {
        var seeded = await SeedAsync("AHD-F");
        await AddAsync(seeded.Tenant, BillingPolicyVersion.Create(seeded.Tenant, Today, Today, BillingPolicy.StallTrackDefaults with { AnnualPaymentAllowed = false, MaxAdvanceMonths = 1 }, null, Guid.NewGuid(), DateTime.UtcNow));

        await using var read = db.CreateContext(seeded.Tenant);
        var options = await OptionsAsync(read, seeded.Tenant, seeded.Stall);

        Assert.Equal([1, 2], options.Counts);
        Assert.Equal(ResultStatus.Invalid, (await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 12, 12_000m)).Status);
        Assert.Equal(ResultStatus.Invalid, (await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 3, 3_000m)).Status);
        Assert.Empty(await RowsAsync(seeded.Tenant, seeded.Stall));
    }

    [SkippableFact]
    public async Task AYear_IsNotOffered_WhenTheTermEndsFirst()
    {
        var start = ThisMonth.AddMonths(-3);
        var seeded = await SeedAsync("AHD-G", start, years: 1);
        await AddAsync(seeded.Tenant, Paid(seeded.Stall, start), Paid(seeded.Stall, start.AddMonths(1)), Paid(seeded.Stall, start.AddMonths(2)));

        await using var read = db.CreateContext(seeded.Tenant);
        var options = await OptionsAsync(read, seeded.Tenant, seeded.Stall);

        Assert.DoesNotContain(12, options.Counts);
        Assert.Equal(ResultStatus.Invalid, (await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 12, 12_000m)).Status);
    }

    [SkippableFact]
    public async Task AChangedAmount_IsRefused()
    {
        var seeded = await SeedAsync("AHD-H");

        Assert.Equal(ResultStatus.Conflict, (await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 2, 1_999m)).Status);
        Assert.Empty(await RowsAsync(seeded.Tenant, seeded.Stall));
    }

    [SkippableFact]
    public async Task AHeldMonth_AndAnExcusedMonth_EndTheRun()
    {
        var seeded = await SeedAsync("AHD-I");
        var held = PaymentRecord.Create(seeded.Stall, ThisMonth.AddMonths(2).Year, ThisMonth.AddMonths(2).Month, Rate, "test");
        held.MarkSettlementPendingCutover();
        await AddAsync(seeded.Tenant, held);

        await using (var read = db.CreateContext(seeded.Tenant))
            Assert.Equal(2, (await OptionsAsync(read, seeded.Tenant, seeded.Stall)).Months.Count);

        var excused = ThisMonth.AddMonths(1);
        await AddAsync(seeded.Tenant, StallMonthlyException.Create(seeded.Stall, excused.Year, excused.Month));

        await using var again = db.CreateContext(seeded.Tenant);
        Assert.Single((await OptionsAsync(again, seeded.Tenant, seeded.Stall)).Months);
    }

    [SkippableFact]
    public async Task APartPaidMonth_IsChargedOnlyItsBalance()
    {
        var seeded = await SeedAsync("AHD-J");
        var part = PaymentRecord.Create(seeded.Stall, ThisMonth.Year, ThisMonth.Month, Rate, "test");
        part.UpdateStatus(PaymentStatus.Partial, 400m, null, "test", null);
        await AddAsync(seeded.Tenant, part);

        var result = await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 2, 1_600m);

        Assert.True(result.IsSuccess, result.Error);
        Assert.All(await RowsAsync(seeded.Tenant, seeded.Stall), r => Assert.Equal(PaymentStatus.Paid, r.Status));
    }

    [SkippableFact]
    public async Task AReceiptUsedOnAnotherStall_RefusesTheWholePayment()
    {
        var seeded = await SeedAsync("AHD-K");
        await using (var write = db.CreateContext(seeded.Tenant))
        {
            var facility = await write.Facilities.SingleAsync();
            var neighbour = Stall.Create(facility.Id, "8", Rate, ApplicableFees.BaseRental, municipalityId: seeded.Tenant);
            write.Add(neighbour);
            var theirs = Paid(neighbour.Id, ThisMonth);
            theirs.SetOrNumber("OR-1001", "test");
            write.Add(theirs);
            await write.SaveChangesAsync();
        }

        Assert.Equal(ResultStatus.Conflict, (await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 12, 12_000m)).Status);
        Assert.Empty(await RowsAsync(seeded.Tenant, seeded.Stall));
    }

    [SkippableFact]
    public async Task AnotherOffice_CannotReachTheStall()
    {
        var seeded = await SeedAsync("AHD-L");
        var other = Municipality.Create("AHD-M", "Other", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "ahd-m");
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(other);
            await setup.SaveChangesAsync();
        }

        Assert.Equal(ResultStatus.NotFound, (await RecordAsync(other.Id, seeded.Stall, ThisMonth, 1, Rate)).Status);
        Assert.Empty(await RowsAsync(seeded.Tenant, seeded.Stall));
    }

    [SkippableFact]
    public async Task ADayBilledMarket_IsNotOffered()
    {
        var seeded = await SeedAsync("AHD-N", facilityCode: FacilityCode.NPM);

        await using var read = db.CreateContext(seeded.Tenant);
        Assert.Empty((await OptionsAsync(read, seeded.Tenant, seeded.Stall)).Counts);
        Assert.Equal(ResultStatus.Conflict, (await RecordAsync(seeded.Tenant, seeded.Stall, ThisMonth, 1, Rate)).Status);
    }
}
