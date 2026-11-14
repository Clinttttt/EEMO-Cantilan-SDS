using EEMOCantilanSDS.Application.Command.OnlinePayments.ApplyHeld;
using EEMOCantilanSDS.Application.Command.OnlinePayments.ResolveException;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.OnlinePayments.GetHeldPaymentPlan;
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

/// <summary>
/// A held online payment applied to the space's rent by the Head (IA-082): oldest month first, then ahead within the office's limit, the last month
/// in part; one OR; recorded once, never alongside a refund, and refused whole when any of it would have nowhere to go.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ApplyHeldOnlinePaymentTests(PostgresFixture db)
{
    private const decimal Rate = 1_000m;
    private static readonly DateOnly Today = PhilippineTime.Today;
    private static readonly DateOnly ThisMonth = new(Today.Year, Today.Month, 1);

    private sealed class Head(Guid tenant) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? MunicipalityId => tenant;
        public string? Username => "head-test";
        public string? Role => "SuperAdmin";
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

    private sealed class Code : ITenantContext { public string TenantCode => "apply-held-test"; }

    private static BillingRules Rules(AppDbContext context, Guid tenant)
    {
        var clock = new SystemClock();
        return new(new(context, new Head(tenant), new Pinned(tenant), clock), new FacilityReportsRepository(context), context, clock);
    }

    private async Task<Result<HeldPaymentPlanDto>> PlanAsync(Guid tenant, Guid payment)
    {
        await using var context = db.CreateContext(tenant);
        return await new GetHeldPaymentPlanQueryHandler(new OnlinePaymentRepository(context), new PaymentRepository(context), new StallRepository(context), Rules(context, tenant))
            .Handle(new GetHeldPaymentPlanQuery(payment), CancellationToken.None);
    }

    private async Task<Result<bool>> ApplyAsync(Guid tenant, Guid payment, IReadOnlyList<HeldPaymentMonthDto> months, string or = "OR-77")
    {
        await using var context = db.CreateContext(tenant);
        return await new ApplyHeldOnlinePaymentCommandHandler(new OnlinePaymentRepository(context), new PaymentRepository(context), new StallRepository(context),
                Rules(context, tenant), context, new Head(tenant), new UnitOfWork(context), new QuietCache(), new Code(), new SystemClock())
            .Handle(new ApplyHeldOnlinePaymentCommand(payment, or, "Letter dated today", months), CancellationToken.None);
    }

    private async Task<Result<bool>> RefundAsync(Guid tenant, Guid payment)
    {
        await using var context = db.CreateContext(tenant);
        return await new ResolveOnlinePaymentExceptionCommandHandler(new OnlinePaymentRepository(context), new Head(tenant), new UnitOfWork(context), new SystemClock(), context)
            .Handle(new ResolveOnlinePaymentExceptionCommand(payment, OnlinePaymentDisposition.Refunded, "ref_1"), CancellationToken.None);
    }

    private async Task<List<PaymentRecord>> RowsAsync(Guid tenant, Guid stall)
    {
        await using var read = db.CreateContext(tenant);
        return await read.PaymentRecords.AsNoTracking().Where(r => r.StallId == stall).OrderBy(r => r.BillingYear).ThenBy(r => r.BillingMonth).ToListAsync();
    }

    private async Task<OnlinePaymentTransaction> PaymentRowAsync(Guid tenant, Guid payment)
    {
        await using var read = db.CreateContext(tenant);
        return await read.OnlinePaymentTransactions.AsNoTracking().SingleAsync(t => t.Id == payment);
    }

    private async Task AddAsync(Guid tenant, params object[] rows)
    {
        await using var write = db.CreateContext(tenant);
        write.AddRange(rows);
        await write.SaveChangesAsync();
    }

    /// <summary>A lessee whose term began <paramref name="termStart"/>, under rules that count from then.</summary>
    private async Task<(Guid Tenant, Guid Stall)> SeedAsync(string code, DateOnly termStart, BillingPolicy? rules = null)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync();
        var municipality = Municipality.Create(code, $"Municipality {code}", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var facility = Facility.Create(FacilityCode.TCC, "A facility", "TCC", municipalityId: municipality.Id);
        var stall = Stall.Create(facility.Id, "7", Rate, ApplicableFees.BaseRental, municipalityId: municipality.Id);
        await AddAsync(municipality.Id, facility, stall, Contract.Create(stall.Id, "Maria Santos", "Maria Santos", termStart, 5, Rate),
            BillingPolicyVersion.Create(municipality.Id, Today, Today, (rules ?? BillingPolicy.StallTrackDefaults) with { CountsMonthsFrom = termStart }, null, Guid.NewGuid(), DateTime.UtcNow));
        return (municipality.Id, stall.Id);
    }

    /// <summary>A capture held on <paramref name="month"/>'s record: the checkout asked for a month's rent and the gateway took <paramref name="captured"/>.</summary>
    private async Task<Guid> HeldAsync(Guid tenant, Guid stall, DateOnly month, decimal captured)
    {
        var record = PaymentRecord.Create(stall, month.Year, month.Month, Rate, "test");
        var payment = OnlinePaymentTransaction.Create($"OP-{Guid.NewGuid():N}"[..17], Guid.NewGuid(), record.Id, Rate, "PayMongo");
        payment.SetPending($"cs_{payment.Id:N}"[..15], $"https://pay.test/{payment.Id}");
        payment.MarkPaid("pay_1", "gcash", DateTime.UtcNow, "{}");
        payment.MarkSettlementReconciliationRequired($"The gateway captured {captured:0.00} but the checkout asked for {Rate:0.00}.", capturedAmount: captured);
        await AddAsync(tenant, record, payment);
        return payment.Id;
    }

    [SkippableFact]
    public async Task TheOldestMonthsArePaidFirst_ThenThisMonthInPart_UnderOneReceipt()
    {
        var older = ThisMonth.AddMonths(-2);
        var seeded = await SeedAsync("HLD-A", older);
        var payment = await HeldAsync(seeded.Tenant, seeded.Stall, older, 2_500m);

        var plan = await PlanAsync(seeded.Tenant, payment);

        Assert.True(plan.IsSuccess, plan.Error);
        Assert.Null(plan.Value!.Refusal);
        // The capture holds its own month; that month is still first, not skipped.
        Assert.Equal([new(older.Year, older.Month, Rate, Rate), new(older.AddMonths(1).Year, older.AddMonths(1).Month, Rate, Rate), new(ThisMonth.Year, ThisMonth.Month, Rate, 500m)],
            plan.Value.Months);

        var result = await ApplyAsync(seeded.Tenant, payment, plan.Value.Months);

        Assert.True(result.IsSuccess, result.Error);
        var rows = await RowsAsync(seeded.Tenant, seeded.Stall);
        Assert.Equal([(PaymentStatus.Paid, Rate), (PaymentStatus.Paid, Rate), (PaymentStatus.Partial, 500m)], rows.Select(r => (r.Status, r.AmountPaid)));
        Assert.All(rows, r => Assert.Equal("OR-77", r.ORNumber));
        Assert.Equal(2_500m, rows.Sum(r => r.AmountPaid));

        var decided = await PaymentRowAsync(seeded.Tenant, payment);
        Assert.Equal((OnlinePaymentDisposition.Applied, "OR-77", "head-test"), (decided.Disposition, decided.DispositionReference, decided.DispositionBy));
        Assert.Contains("Letter dated today", decided.DispositionNote);
        // History is appended to: why it was held stays.
        Assert.Equal(OnlinePaymentStatus.ReconciliationRequired, decided.Status);

        await using var read = db.CreateContext(seeded.Tenant);
        Assert.Empty(await read.Collections.ToListAsync());
        var statement = await new FacilityReportsRepository(read).GetStallStatementAsync(seeded.Stall, CancellationToken.None);
        Assert.Equal((0m, 500m), (statement!.PreviousBalance, statement.CurrentBalance));
    }

    [SkippableFact]
    public async Task AppliedTwiceAtOnce_PaysOnce()
    {
        var seeded = await SeedAsync("HLD-B", ThisMonth);
        var payment = await HeldAsync(seeded.Tenant, seeded.Stall, ThisMonth, 1_500m);
        var months = (await PlanAsync(seeded.Tenant, payment)).Value!.Months;

        var results = await Task.WhenAll(ApplyAsync(seeded.Tenant, payment, months), ApplyAsync(seeded.Tenant, payment, months));

        Assert.Single(results, r => r.IsSuccess);
        Assert.Single(results, r => r.Status == ResultStatus.Conflict);
        Assert.Equal(1_500m, (await RowsAsync(seeded.Tenant, seeded.Stall)).Sum(r => r.AmountPaid));
        Assert.Equal(ResultStatus.Conflict, (await ApplyAsync(seeded.Tenant, payment, months)).Status);
    }

    [SkippableFact]
    public async Task AppliedAndRefundedAtOnce_OnlyOneStands()
    {
        var seeded = await SeedAsync("HLD-C", ThisMonth);
        var payment = await HeldAsync(seeded.Tenant, seeded.Stall, ThisMonth, 1_500m);
        var months = (await PlanAsync(seeded.Tenant, payment)).Value!.Months;

        var results = await Task.WhenAll(ApplyAsync(seeded.Tenant, payment, months), RefundAsync(seeded.Tenant, payment));

        Assert.Single(results, r => r.IsSuccess);
        var paid = (await RowsAsync(seeded.Tenant, seeded.Stall)).Sum(r => r.AmountPaid);
        var decided = await PaymentRowAsync(seeded.Tenant, payment);
        Assert.Equal(decided.Disposition == OnlinePaymentDisposition.Applied ? 1_500m : 0m, paid);
    }

    [SkippableFact]
    public async Task AChangedPlan_IsRefused_AndNothingIsWritten()
    {
        var seeded = await SeedAsync("HLD-D", ThisMonth);
        var payment = await HeldAsync(seeded.Tenant, seeded.Stall, ThisMonth, 1_500m);

        var result = await ApplyAsync(seeded.Tenant, payment, [new(ThisMonth.Year, ThisMonth.Month, Rate, 1_500m)]);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(0m, (await RowsAsync(seeded.Tenant, seeded.Stall)).Sum(r => r.AmountPaid));
        Assert.Null((await PaymentRowAsync(seeded.Tenant, payment)).Disposition);
    }

    [SkippableFact]
    public async Task MoneyWithNoMonthToGoTo_RefusesTheWholePayment()
    {
        var seeded = await SeedAsync("HLD-E", ThisMonth, BillingPolicy.StallTrackDefaults with { MaxAdvanceMonths = 0 });
        var payment = await HeldAsync(seeded.Tenant, seeded.Stall, ThisMonth, 1_500m);

        var plan = (await PlanAsync(seeded.Tenant, payment)).Value!;
        var result = await ApplyAsync(seeded.Tenant, payment, plan.Months);

        Assert.Contains("₱500.00", plan.Refusal);
        Assert.Contains("Record a refund", plan.Refusal);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(0m, (await RowsAsync(seeded.Tenant, seeded.Stall)).Sum(r => r.AmountPaid));
    }

    [SkippableFact]
    public async Task AMonthInCutover_EndsThePlan()
    {
        var seeded = await SeedAsync("HLD-F", ThisMonth);
        var payment = await HeldAsync(seeded.Tenant, seeded.Stall, ThisMonth, 2_000m);
        var next = ThisMonth.AddMonths(1);
        var moving = PaymentRecord.Create(seeded.Stall, next.Year, next.Month, Rate, "test");
        moving.MarkSettlementPendingCutover();
        await AddAsync(seeded.Tenant, moving);

        var plan = (await PlanAsync(seeded.Tenant, payment)).Value!;

        Assert.Contains("cannot take a payment here right now", plan.Refusal);
        Assert.Equal(ResultStatus.Conflict, (await ApplyAsync(seeded.Tenant, payment, plan.Months)).Status);
    }

    [SkippableFact]
    public async Task AReceiptUsedOnAnotherStall_IsRefused()
    {
        var seeded = await SeedAsync("HLD-G", ThisMonth);
        var payment = await HeldAsync(seeded.Tenant, seeded.Stall, ThisMonth, Rate);
        await using (var write = db.CreateContext(seeded.Tenant))
        {
            var facility = await write.Facilities.SingleAsync();
            var neighbour = Stall.Create(facility.Id, "8", Rate, ApplicableFees.BaseRental, municipalityId: seeded.Tenant);
            var theirs = PaymentRecord.Create(neighbour.Id, ThisMonth.Year, ThisMonth.Month, Rate, "test");
            theirs.UpdateStatus(PaymentStatus.Paid, 0m, null, "test", null);
            theirs.SetOrNumber("OR-77", "test");
            write.AddRange(neighbour, theirs);
            await write.SaveChangesAsync();
        }

        var result = await ApplyAsync(seeded.Tenant, payment, (await PlanAsync(seeded.Tenant, payment)).Value!.Months);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Null((await PaymentRowAsync(seeded.Tenant, payment)).Disposition);
    }

    [SkippableFact]
    public async Task AnotherOffice_CannotSeeOrApplyIt()
    {
        var seeded = await SeedAsync("HLD-H", ThisMonth);
        var payment = await HeldAsync(seeded.Tenant, seeded.Stall, ThisMonth, Rate);
        var months = (await PlanAsync(seeded.Tenant, payment)).Value!.Months;
        var other = Municipality.Create("HLD-I", "Other", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "hld-i");
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(other);
            await setup.SaveChangesAsync();
        }

        Assert.Equal(ResultStatus.NotFound, (await PlanAsync(other.Id, payment)).Status);
        Assert.Equal(ResultStatus.NotFound, (await ApplyAsync(other.Id, payment, months)).Status);
        Assert.Equal(0m, (await RowsAsync(seeded.Tenant, seeded.Stall)).Sum(r => r.AmountPaid));
    }
}
