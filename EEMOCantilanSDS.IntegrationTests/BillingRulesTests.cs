using EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;
using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>The office's billing rules decide which month of rent can be taken now, and the portal offers exactly that. The repository reads the real clock, so the tests anchor on it too.</summary>
[Collection(PostgresCollection.Name)]
public class BillingRulesTests(PostgresFixture db)
{
    private const decimal Rate = 1_000m;
    private static readonly DateOnly Today = PhilippineTime.Today;
    private static readonly DateOnly ThisMonth = new(Today.Year, Today.Month, 1);
    private static readonly DateOnly Oldest = ThisMonth.AddMonths(-3);

    private sealed class Caller(Guid tenant, string role = "SuperAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? MunicipalityId => tenant;
        public string? Username => "billing-test";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "TEST";
        public AdminUserDto? GetCurrentUser() => null;
    }

    private sealed class Pinned(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid value) { }
    }

    private static BillingPolicyWorkflow Policy(AppDbContext context, Guid tenant, string role = "SuperAdmin") =>
        new(context, new Caller(tenant, role), new Pinned(tenant), new SystemClock());

    private static BillingRules Rules(AppDbContext context, Guid tenant) =>
        new(Policy(context, tenant), new FacilityReportsRepository(context), context, new SystemClock());

    /// <summary>Let three months ago on a five-year term and never paid: three elapsed months and this one are open.</summary>
    private async Task<(Guid Tenant, Guid Stall)> SeedAsync(string code)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var municipality = Municipality.Create(code, $"Municipality {code}", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC", municipalityId: municipality.Id);
        var stall = Stall.Create(facility.Id, "7", Rate, ApplicableFees.BaseRental, municipalityId: municipality.Id);
        await using var tenant = db.CreateContext(municipality.Id);
        tenant.AddRange(facility, stall, Contract.Create(stall.Id, "Maria Santos", "Maria Santos", Oldest, 5, Rate),
            Version(municipality.Id, BillingPolicy.StallTrackDefaults with { CountsMonthsFrom = Oldest }));
        await tenant.SaveChangesAsync();
        return (municipality.Id, stall.Id);
    }

    private static PaymentRecord Paid(Guid stall, DateOnly month)
    {
        var record = PaymentRecord.Create(stall, month.Year, month.Month, Rate, "test");
        record.UpdateStatus(PaymentStatus.Paid, 0m, null, "test", null);
        return record;
    }

    private async Task AddAsync(Guid tenant, params object[] rows)
    {
        await using var write = db.CreateContext(tenant);
        write.AddRange(rows);
        await write.SaveChangesAsync();
    }

    private static BillingPolicyVersion Version(Guid tenant, BillingPolicy rules, DateOnly? from = null) =>
        BillingPolicyVersion.Create(tenant, from ?? Today, Today, rules, null, Guid.NewGuid(), DateTime.UtcNow);

    private static (int, int) Key(DateOnly month) => (month.Year, month.Month);

    [SkippableFact]
    public async Task ANewerMonth_IsRefused_WhileAnOlderOneIsOpen_AndTheOldestCanBePaid()
    {
        var seeded = await SeedAsync("BIL-A");
        await using var read = db.CreateContext(seeded.Tenant);
        var rules = Rules(read, seeded.Tenant);

        var refused = await rules.RefusalAsync(seeded.Stall, Today.Year, Today.Month, CancellationToken.None);

        Assert.Contains(Oldest.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture), refused);
        Assert.Null(await rules.RefusalAsync(seeded.Stall, Oldest.Year, Oldest.Month, CancellationToken.None));
    }

    [SkippableFact]
    public async Task AMonthFromBeforeTheRulesBegin_HoldsNothingUp_AndCanStillBeTaken()
    {
        var seeded = await SeedAsync("BIL-P");
        var second = Oldest.AddMonths(1);
        await AddAsync(seeded.Tenant, Version(seeded.Tenant, BillingPolicy.StallTrackDefaults with { CountsMonthsFrom = second }));
        await using var read = db.CreateContext(seeded.Tenant);
        var rules = Rules(read, seeded.Tenant);

        Assert.Null(await rules.RefusalAsync(seeded.Stall, Oldest.Year, Oldest.Month, CancellationToken.None));
        Assert.Null(await rules.RefusalAsync(seeded.Stall, second.Year, second.Month, CancellationToken.None));
        // The line starts at the first month the rules reach, not at the first the term ever billed.
        Assert.Contains(second.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
            await rules.RefusalAsync(seeded.Stall, Today.Year, Today.Month, CancellationToken.None));
    }

    [SkippableFact]
    public async Task WhileTheStartMonthIsStillAhead_NextMonth_WaitsForThisOne()
    {
        var seeded = await SeedAsync("BIL-Q");
        var next = ThisMonth.AddMonths(1);
        await AddAsync(seeded.Tenant, Version(seeded.Tenant, BillingPolicy.StallTrackDefaults with { CountsMonthsFrom = ThisMonth.AddMonths(2) }));
        await using var read = db.CreateContext(seeded.Tenant);
        var rules = Rules(read, seeded.Tenant);

        Assert.Null(await rules.RefusalAsync(seeded.Stall, Today.Year, Today.Month, CancellationToken.None));
        Assert.Contains(ThisMonth.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture),
            await rules.RefusalAsync(seeded.Stall, next.Year, next.Month, CancellationToken.None));
    }

    [SkippableFact]
    public async Task OlderMonthsOnTheSameReceipt_DoNotHoldUpTheNext()
    {
        var seeded = await SeedAsync("BIL-B");
        await using var read = db.CreateContext(seeded.Tenant);
        var second = Oldest.AddMonths(1);

        var refusal = await Rules(read, seeded.Tenant).RefusalAsync(seeded.Stall, second.Year, second.Month, CancellationToken.None, [Key(Oldest)]);

        Assert.Null(refusal);
    }

    [SkippableFact]
    public async Task AHeadWhoTurnsItOff_LetsANewerMonthBePaid()
    {
        var seeded = await SeedAsync("BIL-C");
        await AddAsync(seeded.Tenant, Version(seeded.Tenant, BillingPolicy.StallTrackDefaults with { OldestMonthFirst = false }));
        await using var read = db.CreateContext(seeded.Tenant);

        Assert.Null(await Rules(read, seeded.Tenant).RefusalAsync(seeded.Stall, Today.Year, Today.Month, CancellationToken.None));
    }

    [SkippableFact]
    public async Task APolicyNotYetEffective_DoesNotApply()
    {
        var seeded = await SeedAsync("BIL-D");
        await AddAsync(seeded.Tenant, Version(seeded.Tenant, BillingPolicy.StallTrackDefaults with { OldestMonthFirst = false }, Today.AddDays(5)));
        await using var read = db.CreateContext(seeded.Tenant);

        Assert.NotNull(await Rules(read, seeded.Tenant).RefusalAsync(seeded.Stall, Today.Year, Today.Month, CancellationToken.None));
    }

    [SkippableFact]
    public async Task AnOlderMonthHeldForTheOffice_DoesNotHoldUpTheNext()
    {
        var seeded = await SeedAsync("BIL-E");
        var held = PaymentRecord.Create(seeded.Stall, Oldest.Year, Oldest.Month, Rate, "test");
        held.MarkSettlementPendingCutover();
        await AddAsync(seeded.Tenant, held);
        await using var read = db.CreateContext(seeded.Tenant);
        var second = Oldest.AddMonths(1);

        Assert.Null(await Rules(read, seeded.Tenant).RefusalAsync(seeded.Stall, second.Year, second.Month, CancellationToken.None));
    }

    [SkippableFact]
    public async Task AMonthAhead_NeedsEverythingBeforeItPaid_AndStaysWithinTheLimit()
    {
        var seeded = await SeedAsync("BIL-F");
        var next = ThisMonth.AddMonths(1);
        await using (var read = db.CreateContext(seeded.Tenant))
            Assert.NotNull(await Rules(read, seeded.Tenant).RefusalAsync(seeded.Stall, next.Year, next.Month, CancellationToken.None));

        await AddAsync(seeded.Tenant, Paid(seeded.Stall, Oldest), Paid(seeded.Stall, Oldest.AddMonths(1)), Paid(seeded.Stall, Oldest.AddMonths(2)), Paid(seeded.Stall, ThisMonth));

        await using var settled = db.CreateContext(seeded.Tenant);
        var rules = Rules(settled, seeded.Tenant);
        var pastTheLimit = ThisMonth.AddMonths(BillingPolicy.StallTrackDefaults.MaxAdvanceMonths + 1);
        Assert.Null(await rules.RefusalAsync(seeded.Stall, next.Year, next.Month, CancellationToken.None));
        Assert.Contains("ahead", await rules.RefusalAsync(seeded.Stall, pastTheLimit.Year, pastTheLimit.Month, CancellationToken.None));
    }

    [SkippableFact]
    public async Task AnOfficeThatTakesNothingAhead_RefusesNextMonth()
    {
        var seeded = await SeedAsync("BIL-G");
        await AddAsync(seeded.Tenant, Version(seeded.Tenant, BillingPolicy.StallTrackDefaults with { MaxAdvanceMonths = 0 }));
        await using var read = db.CreateContext(seeded.Tenant);
        var next = ThisMonth.AddMonths(1);

        Assert.Contains("has not begun", await Rules(read, seeded.Tenant).RefusalAsync(seeded.Stall, next.Year, next.Month, CancellationToken.None));
    }

    [SkippableFact]
    public async Task AnEarlierOccupantsDebt_NeverHoldsUpTheSittingLessee()
    {
        var seeded = await SeedAsync("BIL-H");
        // The present lessee took over this month; the three unpaid months are the old account's, stated in its own register.
        await using (var write = db.CreateContext(seeded.Tenant))
        {
            var old = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(write.Contracts);
            old.Terminate("test", ThisMonth.AddDays(-1));
            write.Add(Contract.Create(seeded.Stall, "Juan Cruz", "Juan Cruz", ThisMonth, 5, Rate));
            await write.SaveChangesAsync();
        }

        await using var read = db.CreateContext(seeded.Tenant);

        Assert.Null(await Rules(read, seeded.Tenant).RefusalAsync(seeded.Stall, Today.Year, Today.Month, CancellationToken.None));
    }

    private async Task<Guid> LinkPayorAsync(Guid tenant, Guid stall, string phone)
    {
        var payor = PayorUser.Create("Maria Santos", phone, new HashedPassword("hash"));
        await AddAsync(tenant, payor, PayorStallLink.Create(payor.Id, stall));
        return payor.Id;
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

    private sealed class Code : ITenantContext { public string TenantCode => "billing-test"; }

    private sealed class ThePayor(Guid id, Guid tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => id;
        public Guid? MunicipalityId => tenant;
        public string? Username => "payor";
        public string? Role => "Payor";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "TEST";
        public AdminUserDto? GetCurrentUser() => null;
    }

    private async Task<Result<bool>> RecordAsync(Guid tenant, RecordPaymentCommand command)
    {
        await using var context = db.CreateContext(tenant);
        return await new RecordPaymentCommandHandler(new PaymentRepository(context), new StallRepository(context), null!, new Caller(tenant, "Admin"),
            new UnitOfWork(context), new QuietCache(), new Code(), new SystemClock(), null, Rules(context, tenant)).Handle(command, CancellationToken.None);
    }

    [SkippableFact]
    public async Task TheOffice_IsRefusedANewerMonth_ButMoneyQueuedOffline_IsRecorded()
    {
        var seeded = await SeedAsync("BIL-M");

        var refused = await RecordAsync(seeded.Tenant, new(seeded.Stall, Today.Year, Today.Month, PaymentStatus.Paid, null, null));
        Assert.Equal(ResultStatus.Conflict, refused.Status);
        Assert.Contains(Oldest.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture), refused.Error);

        var queued = await RecordAsync(seeded.Tenant, new(seeded.Stall, Today.Year, Today.Month, PaymentStatus.Paid, null, null, ClientOperationId: Guid.NewGuid()));
        Assert.True(queued.IsSuccess, queued.Error);
    }

    [SkippableFact]
    public async Task OnlineCheckout_IsRefusedANewerMonth_BeforeAnythingIsCreated()
    {
        var seeded = await SeedAsync("BIL-N");
        var payor = await LinkPayorAsync(seeded.Tenant, seeded.Stall, "09170000014");
        await using var context = db.CreateContext(seeded.Tenant);

        // The gateway and its helpers are never reached, so a refusal here is the guard's own.
        var refused = await new InitiateOnlinePaymentCommandHandler(null!, new PaymentRepository(context), new StallRepository(context), new PayorRepository(context, null!),
                null!, null!, null!, new ThePayor(payor, seeded.Tenant), new UnitOfWork(context), new SystemClock(), null, Rules(context, seeded.Tenant))
            .Handle(new InitiateOnlinePaymentCommand(seeded.Stall, Today.Year, Today.Month), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, refused.Status);
        Assert.Contains(Oldest.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture), refused.Error);
        Assert.Empty(await context.PaymentRecords.ToListAsync());
    }

    [SkippableFact]
    public async Task ThePortal_OffersOnlyTheOldestMonth_AndNamesItOnTheRest()
    {
        var seeded = await SeedAsync("BIL-I");
        var payor = await LinkPayorAsync(seeded.Tenant, seeded.Stall, "09170000011");
        await using var read = db.CreateContext(seeded.Tenant);

        var items = await new PayorRepository(read, null!).GetPayableItemsAsync(payor);

        var payable = Assert.Single(items, i => i.PayableOnline);
        Assert.Equal((Oldest.Year, Oldest.Month), (payable.Year, payable.Month));
        Assert.All(items.Where(i => !i.PayableOnline), i => Assert.Equal(payable.Period, i.PayFirst));
        Assert.All(items, i => Assert.False(i.Advance));   // nothing ahead is offered while rent is owed
        Assert.Equal(BillingPolicy.StallTrackDefaults.DueDate(Today.Year, Today.Month), items.Single(i => i.Year == Today.Year && i.Month == Today.Month).DueDate);
    }

    [SkippableFact]
    public async Task MonthsAhead_AreOfferedOnceNothingIsOwed_AndAreNeverABalance()
    {
        var seeded = await SeedAsync("BIL-J");
        var payor = await LinkPayorAsync(seeded.Tenant, seeded.Stall, "09170000012");
        await AddAsync(seeded.Tenant, Paid(seeded.Stall, Oldest), Paid(seeded.Stall, Oldest.AddMonths(1)), Paid(seeded.Stall, Oldest.AddMonths(2)), Paid(seeded.Stall, ThisMonth));
        await using var read = db.CreateContext(seeded.Tenant);
        var portal = new PayorRepository(read, null!);

        var items = await portal.GetPayableItemsAsync(payor);
        var balance = Assert.Single(await portal.GetBalancesAsync(payor));

        Assert.Equal(BillingPolicy.StallTrackDefaults.MaxAdvanceMonths, items.Count);
        Assert.All(items, i => Assert.True(i.Advance));
        Assert.Equal((ThisMonth.AddMonths(1).Year, ThisMonth.AddMonths(1).Month), (items.Single(i => i.PayableOnline).Year, items.Single(i => i.PayableOnline).Month));
        Assert.Equal(0m, balance.OutstandingBalance);
    }

    [SkippableFact]
    public async Task ACheckoutAbandonedForAMonthAhead_IsNotOwed()
    {
        var seeded = await SeedAsync("BIL-K");
        var payor = await LinkPayorAsync(seeded.Tenant, seeded.Stall, "09170000013");
        var next = ThisMonth.AddMonths(1);
        await AddAsync(seeded.Tenant, Paid(seeded.Stall, Oldest), Paid(seeded.Stall, Oldest.AddMonths(1)), Paid(seeded.Stall, Oldest.AddMonths(2)), Paid(seeded.Stall, ThisMonth),
            PaymentRecord.Create(seeded.Stall, next.Year, next.Month, Rate, "Online"));
        await using var read = db.CreateContext(seeded.Tenant);
        var reports = new FacilityReportsRepository(read);
        var portal = new PayorRepository(read, null!);

        var statement = await reports.GetStallStatementAsync(seeded.Stall, CancellationToken.None);
        var balance = Assert.Single(await portal.GetBalancesAsync(payor));
        var rows = await portal.GetPayableItemsAsync(payor);

        Assert.Equal(0m, statement!.TotalDue);
        Assert.Equal(0m, balance.OutstandingBalance);
        Assert.Single(rows, i => i.Year == next.Year && i.Month == next.Month && i.Advance);   // listed once, not twice
    }

    [SkippableFact]
    public async Task OnlyTheHead_RecordsAPolicy_AndAnotherOfficesIsNotApplied()
    {
        var ours = await SeedAsync("BIL-L");
        var theirs = Municipality.Create("BIL-M", "Other", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "bil-m");
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(theirs);
            await setup.SaveChangesAsync();
        }
        await AddAsync(theirs.Id, Version(theirs.Id, BillingPolicy.StallTrackDefaults with { OldestMonthFirst = false }));
        var request = new RecordBillingPolicyRequest(Today, BillingPolicy.StallTrackDefaults with { GraceDays = 5 }, null);

        await using var read = db.CreateContext(ours.Tenant);
        var admin = await Policy(read, ours.Tenant, "Admin").RecordAsync(request);
        var head = await Policy(read, ours.Tenant).RecordAsync(request);

        Assert.Equal(ResultStatus.Forbidden, admin.Status);
        Assert.True(head.IsSuccess, head.Error);
        Assert.Equal(5, head.Value!.Current.GraceDays);
        Assert.True(head.Value.Current.OldestMonthFirst);
        Assert.Equal(2, head.Value.Versions.Count);   // the one seeded and the Head's; the Admin's was never recorded
    }

    [SkippableFact]
    public async Task APenaltyRate_NeedsItsAuthority_AndAPolicyCannotReachBack()
    {
        var seeded = await SeedAsync("BIL-N");
        await using var read = db.CreateContext(seeded.Tenant);
        var policy = Policy(read, seeded.Tenant);
        var penalised = BillingPolicy.StallTrackDefaults with { PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid, PenaltyValue = 2m };

        var noAuthority = await policy.RecordAsync(new(Today, penalised, null));
        var backdated = await policy.RecordAsync(new(Today.AddDays(-1), BillingPolicy.StallTrackDefaults, null));
        var fine = await policy.RecordAsync(new(Today, penalised, "Ordinance 12"));

        Assert.Equal(ResultStatus.Invalid, noAuthority.Status);
        Assert.Equal(ResultStatus.Invalid, backdated.Status);
        Assert.True(fine.IsSuccess, fine.Error);
        Assert.True(fine.Value!.Current.ChargesPenalty);
    }
}
