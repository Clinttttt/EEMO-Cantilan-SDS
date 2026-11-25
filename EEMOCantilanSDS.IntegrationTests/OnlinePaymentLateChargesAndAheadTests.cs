using EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Payments;
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

/// <summary>Online checkout (2026-11-25): a late month is paid with its surcharge and interest, and rent is paid ahead 1, 2, 3 or 12 months in one checkout.</summary>
[Collection(PostgresCollection.Name)]
public class OnlinePaymentLateChargesAndAheadTests(PostgresFixture db)
{
    private const decimal Rate = 1_000m;
    private static readonly DateOnly Today = PhilippineTime.Today;
    private static readonly DateOnly ThisMonth = new(Today.Year, Today.Month, 1);
    private static readonly DateOnly Oldest = ThisMonth.AddMonths(-3);

    private static readonly BillingPolicy Sec168 = BillingPolicy.StallTrackDefaults with
    {
        PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid, PenaltyValue = 25m, InterestPercent = 2m, InterestMaxMonths = 36, CountsMonthsFrom = Oldest
    };

    private static readonly BillingPolicy Ahead = BillingPolicy.StallTrackDefaults with { MaxAdvanceMonths = 3, CountsMonthsFrom = Oldest };

    private sealed class Caller(Guid tenant, Guid? user = null, string role = "SuperAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = user ?? Guid.NewGuid();
        public Guid? MunicipalityId => tenant;
        public string? Username => "online-test";
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

    private sealed class Gateway : IPaymentGateway
    {
        public string Provider => "PayMongo";
        public Task<Result<CheckoutSessionResult>> CreateCheckoutSessionAsync(CreateCheckoutSessionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<CheckoutSessionResult>.Success(new("https://checkout.test/" + request.Reference, "cs_" + request.Reference, Provider)));
        public bool VerifyWebhookSignature(string payload, string signatureHeader) => true;
        public bool VerifyWebhookSignature(string payload, string signatureHeader, string webhookSecret) => true;
        public Result<PaymentGatewayEvent> ParseEvent(string payload) => throw new NotSupportedException();
        public Task<Result<PaymentGatewayEvent>> RetrievePaymentStatusAsync(string gatewayReference, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Urls : IOnlinePaymentUrlBuilder
    {
        public string BuildSuccessUrl(string reference) => "https://portal.test/ok";
        public string BuildCancelUrl(string reference) => "https://portal.test/cancel";
        public string BuildWebhookUrl(string tenantCode) => "https://api.test/hook";
    }

    private sealed class Quiet : IEemoCacheInvalidator, IOnlinePaymentNotifier, ITenantContext
    {
        public string TenantCode => "online-test";
        public Task NotifyPaymentReceivedAsync(OnlinePaymentNotification n, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateRegionAsync(string region, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePeriodAsync(string t, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateFacilityPeriodAsync(string t, FacilityCode f, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePaymentAffectedViewsAsync(string t, FacilityCode? f, int y, int m, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateReferenceDataAsync(string t, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateTenantAsync(string t, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static BillingRules Rules(AppDbContext context, Guid tenant)
    {
        var clock = new SystemClock();
        return new(new(context, new Caller(tenant), new Pinned(tenant), clock), new FacilityReportsRepository(context), context, clock);
    }

    private async Task<(Guid Tenant, Guid Stall, Guid Payor)> SeedAsync(string code, BillingPolicy rules)
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
        var payor = PayorUser.Create("Maria Santos", "09170000031", new HashedPassword("hash"));
        await using var tenant = db.CreateContext(municipality.Id);
        tenant.AddRange(facility, stall, Contract.Create(stall.Id, "Maria Santos", "Maria Santos", Oldest, 5, Rate), payor, PayorStallLink.Create(payor.Id, stall.Id),
            BillingPolicyVersion.Create(municipality.Id, Oldest, Oldest, rules, "Ordinance 12", Guid.NewGuid(), DateTime.UtcNow));
        await tenant.SaveChangesAsync();
        return (municipality.Id, stall.Id, payor.Id);
    }

    private async Task<Result<InitiateOnlinePaymentResultDto>> CheckoutAsync((Guid Tenant, Guid Stall, Guid Payor) s, DateOnly month, int? months = null)
    {
        await using var context = db.CreateContext(s.Tenant);
        var clock = new SystemClock();
        return await new InitiateOnlinePaymentCommandHandler(new OnlinePaymentRepository(context), new PaymentRepository(context), new StallRepository(context),
                new PayorRepository(context, null!), new Gateway(), null!, new Urls(), new Caller(s.Tenant, s.Payor, "Payor"), new UnitOfWork(context), clock,
                new OnlineRentCanonicalPoster(context, clock), Rules(context, s.Tenant), null, new OnlineLateCharges(context, clock))
            .Handle(new InitiateOnlinePaymentCommand(s.Stall, month.Year, month.Month, Months: months), CancellationToken.None);
    }

    private async Task<Result<bool>> CaptureAsync(Guid tenant, string reference, decimal? amount = null)
    {
        await using var context = db.CreateContext(tenant);
        var clock = new SystemClock();
        var tx = await context.OnlinePaymentTransactions.SingleAsync(t => t.Reference == reference);
        var quiet = new Quiet();
        var service = new OnlinePaymentSettlementService(new PaymentRepository(context), new StallRepository(context), null!, null!, quiet, new UnitOfWork(context),
            quiet, quiet, null, new OnlineRentCanonicalPoster(context, clock), new OnlineLateCharges(context, clock));
        return await service.SettleAsync(tx, new(PaymentGatewayEventType.Paid, tx.GatewayReference!, amount ?? tx.Amount, "pay_test", "qrph", DateTime.UtcNow, "{}"));
    }

    private async Task AssessAsync(Guid tenant, Guid stall)
    {
        await using var work = db.CreateContext(tenant);
        var clock = new SystemClock();
        var reports = new FacilityReportsRepository(work);
        await new LatePenaltyAssessor(work, reports, Rules(work, tenant), new Pinned(tenant), clock).AssessStallAsync(stall);
    }

    [SkippableFact]
    public async Task ALateMonth_IsPaidWithItsSurchargeAndInterest_InOneCheckout_AndEachChargeIsSettledOnce()
    {
        var s = await SeedAsync("ONL-L", Sec168);
        await AssessAsync(s.Tenant, s.Stall);   // the oldest month: ₱250 surcharge and 3 × ₱25 interest

        var checkout = await CheckoutAsync(s, Oldest);
        Assert.True(checkout.IsSuccess, checkout.Error);
        await using (var read = db.CreateContext(s.Tenant))
        {
            var tx = await read.OnlinePaymentTransactions.AsNoTracking().SingleAsync();
            Assert.Equal((1_325m, (decimal?)325m, (int?)null), (tx.Amount, tx.LateCharges, tx.Months));
        }

        var settled = await CaptureAsync(s.Tenant, checkout.Value!.Reference);
        Assert.True(settled.IsSuccess, settled.Error);
        Assert.True((await CaptureAsync(s.Tenant, checkout.Value.Reference)).IsSuccess);   // the webhook and the payor's return: one settlement

        await using var after = db.CreateContext(s.Tenant);
        var rent = await after.PaymentRecords.AsNoTracking().SingleAsync(r => r.BillingYear == Oldest.Year && r.BillingMonth == Oldest.Month);
        Assert.Equal(PaymentStatus.Paid, rent.Status);
        var collection = Assert.Single(await after.Collections.AsNoTracking().ToListAsync());
        var allocations = await after.CollectionAllocations.AsNoTracking().ToListAsync();
        Assert.Equal(4, allocations.Count);
        Assert.All(allocations, a => Assert.Equal(CollectionSourceKind.FineAssessment, a.SourceKind));
        Assert.Equal(325m, allocations.Sum(a => a.Amount));
        var statement = await new FacilityReportsRepository(after).GetStallStatementAsync(s.Stall, CancellationToken.None);
        Assert.DoesNotContain(statement!.LateCharges ?? [], m => m.Year == Oldest.Year && m.Month == Oldest.Month);
        Assert.Equal(OnlinePaymentStatus.Paid, (await after.OnlinePaymentTransactions.AsNoTracking().SingleAsync()).Status);
        Assert.NotNull(collection);
    }

    [SkippableFact]
    public async Task ThreeMonthsAhead_ArePaidInOneCheckout_AndEachMonthIsRecordedPaid()
    {
        var s = await SeedAsync("ONL-A", Ahead);
        await using (var write = db.CreateContext(s.Tenant))
        {
            for (var month = Oldest; month < ThisMonth; month = month.AddMonths(1))
            {
                var paid = PaymentRecord.Create(s.Stall, month.Year, month.Month, Rate, "test");
                paid.UpdateStatus(PaymentStatus.Paid, 0m, null, "test", null);
                write.Add(paid);
            }
            await write.SaveChangesAsync();
        }

        var checkout = await CheckoutAsync(s, ThisMonth, 3);
        Assert.True(checkout.IsSuccess, checkout.Error);
        Assert.True((await CaptureAsync(s.Tenant, checkout.Value!.Reference)).IsSuccess);

        await using var after = db.CreateContext(s.Tenant);
        var tx = await after.OnlinePaymentTransactions.AsNoTracking().SingleAsync();
        Assert.Equal((3_000m, (int?)3, OnlinePaymentStatus.Paid), (tx.Amount, tx.Months, tx.Status));
        var ahead = await after.PaymentRecords.AsNoTracking().Where(r => r.BillingYear * 12 + r.BillingMonth >= ThisMonth.Year * 12 + ThisMonth.Month).ToListAsync();
        Assert.Equal(3, ahead.Count);
        Assert.All(ahead, r => Assert.Equal(PaymentStatus.Paid, r.Status));
    }

    [SkippableFact]
    public async Task NothingIsPaidAhead_WhileAnEarlierMonthIsOwed()
    {
        var s = await SeedAsync("ONL-H", Ahead);

        var refused = await CheckoutAsync(s, ThisMonth, 2);

        Assert.Equal(ResultStatus.Conflict, refused.Status);
        Assert.Contains("owe", refused.Error);
        await using var read = db.CreateContext(s.Tenant);
        Assert.Empty(await read.OnlinePaymentTransactions.ToListAsync());
    }

    [SkippableFact]
    public async Task ACaptureThatNoLongerMatchesTheLateCharges_IsHeldForTheOffice_AndNothingIsApplied()
    {
        var s = await SeedAsync("ONL-M", Sec168);
        await AssessAsync(s.Tenant, s.Stall);
        var checkout = await CheckoutAsync(s, Oldest);
        Assert.True(checkout.IsSuccess, checkout.Error);
        await using (var write = db.CreateContext(s.Tenant))
        {
            // The Head waives the surcharge after the payor opened checkout.
            var surcharge = await write.FineAssessments.SingleAsync(f => f.RelatedPeriodStart == Oldest && f.PenaltyCode == FineAssessment.SurchargeCode);
            write.Entry(surcharge).Property(nameof(FineAssessment.AdjustedAmount)).CurrentValue = 0m;
            await write.SaveChangesAsync();
        }

        var held = await CaptureAsync(s.Tenant, checkout.Value!.Reference);

        Assert.Equal(ResultStatus.Conflict, held.Status);
        await using var after = db.CreateContext(s.Tenant);
        Assert.Empty(await after.Collections.ToListAsync());
        Assert.DoesNotContain(await after.PaymentRecords.AsNoTracking().ToListAsync(), r => r.Status == PaymentStatus.Paid);
        Assert.Equal(OnlinePaymentStatus.ReconciliationRequired, (await after.OnlinePaymentTransactions.AsNoTracking().SingleAsync()).Status);
    }
}
