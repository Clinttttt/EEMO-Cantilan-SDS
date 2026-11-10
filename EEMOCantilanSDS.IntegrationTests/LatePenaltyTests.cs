using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Payors;
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

/// <summary>Automatic late penalties (IA-072): a normal fine assessment per charge, never cash, never twice, and the Head has the last word on each.</summary>
[Collection(PostgresCollection.Name)]
public class LatePenaltyTests(PostgresFixture db)
{
    private const decimal Rate = 1_000m;
    private static readonly DateOnly Today = PhilippineTime.Today;
    private static readonly DateOnly ThisMonth = new(Today.Year, Today.Month, 1);
    private static readonly DateOnly Oldest = ThisMonth.AddMonths(-3);
    private static readonly BillingPolicy Penalised = BillingPolicy.StallTrackDefaults with { PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid, PenaltyValue = 2m, CountsMonthsFrom = Oldest };

    private sealed class Caller(Guid tenant, string role = "SuperAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? MunicipalityId => tenant;
        public string? Username => "penalty-test";
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

    private static LatePenaltyAssessor Assessor(AppDbContext context, Guid tenant)
    {
        var clock = new SystemClock();
        var reports = new FacilityReportsRepository(context);
        return new(context, reports, new(new(context, new Caller(tenant), new Pinned(tenant), clock), reports, context, clock), new Pinned(tenant), clock);
    }

    private static FineAssessmentWorkflow Fines(AppDbContext context, Guid tenant, string role = "SuperAdmin") =>
        new(context, new Caller(tenant, role), new Pinned(tenant), new SystemClock());

    private async Task<(Guid Tenant, Guid Stall)> SeedAsync(string code, BillingPolicy? rules, DateOnly? effectiveFrom = null)
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
        tenant.AddRange(facility, stall, Contract.Create(stall.Id, "Maria Santos", "Maria Santos", Oldest, 5, Rate));
        await tenant.SaveChangesAsync();
        if (rules is not null)
        {
            // The rules were set before the months in question fell due, which a real Head's future-dated version would be by the time they do.
            var version = BillingPolicyVersion.Create(municipality.Id, effectiveFrom ?? Oldest, effectiveFrom ?? Oldest, rules, "Ordinance 12", Guid.NewGuid(), DateTime.UtcNow);
            tenant.Add(version);
            await tenant.SaveChangesAsync();
        }
        return (municipality.Id, stall.Id);
    }

    private async Task<List<FineAssessment>> AssessedAsync(Guid tenant)
    {
        await using var read = db.CreateContext(tenant);
        return await read.FineAssessments.AsNoTracking().OrderBy(a => a.RelatedPeriodStart).ToListAsync();
    }

    private async Task<int> RunAsync(Guid tenant, Guid stall)
    {
        await using var work = db.CreateContext(tenant);
        return await Assessor(work, tenant).AssessStallAsync(stall);
    }

    [SkippableFact]
    public async Task EachLateMonth_GetsOneCharge_OnItsOwnUnpaidRent_AndNoCash()
    {
        var seeded = await SeedAsync("LAT-A", Penalised);

        await RunAsync(seeded.Tenant, seeded.Stall);

        var fines = await AssessedAsync(seeded.Tenant);
        Assert.Equal(3, fines.Count);   // three elapsed months; this one is not late until it ends
        Assert.All(fines, f => Assert.Equal(20m, f.AssessedAmount));
        Assert.Equal(Oldest, fines[0].RelatedPeriodStart);
        await using var read = db.CreateContext(seeded.Tenant);
        Assert.Empty(await read.Collections.ToListAsync());
        Assert.Empty(await read.PaymentRecords.ToListAsync());
    }

    [SkippableFact]
    public async Task AMonthFromBeforeTheRulesBegin_IsNeverCharged()
    {
        var lastMonth = ThisMonth.AddMonths(-1);
        var seeded = await SeedAsync("LAT-M", Penalised with { CountsMonthsFrom = lastMonth });

        await RunAsync(seeded.Tenant, seeded.Stall);

        Assert.Equal(lastMonth, Assert.Single(await AssessedAsync(seeded.Tenant)).RelatedPeriodStart);
    }

    [SkippableFact]
    public async Task RunningAgain_CreatesNothing_AndAWaivedChargeDoesNotComeBack()
    {
        var seeded = await SeedAsync("LAT-B", Penalised);
        await RunAsync(seeded.Tenant, seeded.Stall);
        var first = (await AssessedAsync(seeded.Tenant))[0];
        await using (var head = db.CreateContext(seeded.Tenant))
            Assert.True((await Fines(head, seeded.Tenant).AdjustAsync(first.Id, new(0m, "Office error", first.Version))).IsSuccess);

        Assert.Equal(0, await RunAsync(seeded.Tenant, seeded.Stall));

        var fines = await AssessedAsync(seeded.Tenant);
        Assert.Equal(3, fines.Count);
        Assert.Equal(0m, fines[0].AmountDue);
    }

    [SkippableFact]
    public async Task NoRate_NoPenalty()
    {
        var seeded = await SeedAsync("LAT-C", BillingPolicy.StallTrackDefaults);

        Assert.Equal(0, await RunAsync(seeded.Tenant, seeded.Stall));
    }

    [SkippableFact]
    public async Task AHeadTurningItOff_StopsNewCharges()
    {
        var seeded = await SeedAsync("LAT-D", Penalised);
        await using (var write = db.CreateContext(seeded.Tenant))
        {
            write.Add(BillingPolicyVersion.Create(seeded.Tenant, Today, Today, BillingPolicy.StallTrackDefaults, null, Guid.NewGuid(), DateTime.UtcNow));
            await write.SaveChangesAsync();
        }
        await using var work = db.CreateContext(seeded.Tenant);

        Assert.Empty(await Assessor(work, seeded.Tenant).StallsToReviewAsync());
        Assert.Equal(0, await RunAsync(seeded.Tenant, seeded.Stall));
    }

    [SkippableFact]
    public async Task APolicyTakingEffectToday_DoesNotReachBackOverEarlierMonths()
    {
        var seeded = await SeedAsync("LAT-E", Penalised, Today);

        Assert.Equal(0, await RunAsync(seeded.Tenant, seeded.Stall));
    }

    [SkippableFact]
    public async Task APolicyTakingEffectAfterAMonthsDueDate_DoesNotChargeThatMonth()
    {
        // In force before the month ends, but after its due day had passed: the lessee could not have known this rate on that day.
        var seeded = await SeedAsync("LAT-M", Penalised with { DueDay = 5 }, Oldest.AddDays(14));

        await RunAsync(seeded.Tenant, seeded.Stall);

        var charged = (await AssessedAsync(seeded.Tenant)).Select(f => f.RelatedPeriodStart).ToList();
        Assert.DoesNotContain(Oldest, charged);
        Assert.Contains(Oldest.AddMonths(1), charged);
    }

    [SkippableFact]
    public async Task TwoPassesAtOnce_LeaveOneChargePerMonth()
    {
        var seeded = await SeedAsync("LAT-N", Penalised);

        // The service treats the loser of this race as done, and the database may end it either way: a unique violation, or a deadlock (40P01) that EF wraps as transient.
        // So the only thing that matters is what is left.
        await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await RunAsync(seeded.Tenant, seeded.Stall); }
            catch (Exception e) when (e is DbUpdateException or InvalidOperationException { InnerException: DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "40P01" } } }) { }
        }));

        Assert.Equal(3, (await AssessedAsync(seeded.Tenant)).Count);
    }

    [SkippableFact]
    public async Task TheCeiling_StopsRepeatedCharges()
    {
        var seeded = await SeedAsync("LAT-F", Penalised with { PenaltyRepeatsMonthly = true, PenaltyMonthCap = 30m });

        await RunAsync(seeded.Tenant, seeded.Stall);

        var fines = await AssessedAsync(seeded.Tenant);
        Assert.All(fines.GroupBy(f => f.RelatedPeriodStart), month => Assert.True(month.Sum(f => f.AssessedAmount) <= 30m));
        Assert.Equal(30m, fines.Where(f => f.RelatedPeriodStart == Oldest).Sum(f => f.AssessedAmount));   // ₱20, then the ₱10 left under the ceiling
    }

    [SkippableFact]
    public async Task APaidMonth_AndAMonthHeldForTheOffice_AreNotCharged()
    {
        var seeded = await SeedAsync("LAT-G", Penalised);
        var paid = PaymentRecord.Create(seeded.Stall, Oldest.Year, Oldest.Month, Rate, "test");
        paid.UpdateStatus(PaymentStatus.Paid, 0m, null, "test", null);
        var held = PaymentRecord.Create(seeded.Stall, Oldest.AddMonths(1).Year, Oldest.AddMonths(1).Month, Rate, "test");
        held.MarkSettlementPendingCutover();
        await using (var write = db.CreateContext(seeded.Tenant))
        {
            write.AddRange(paid, held);
            await write.SaveChangesAsync();
        }

        await RunAsync(seeded.Tenant, seeded.Stall);

        var fine = Assert.Single(await AssessedAsync(seeded.Tenant));
        Assert.Equal(Oldest.AddMonths(2), fine.RelatedPeriodStart);
    }

    [SkippableFact]
    public async Task AnAutomaticPenalty_CanBePaidAtTheOffice_EvenInAnOfficeWithNoFinesSourceYet()
    {
        var seeded = await SeedAsync("LAT-Q", Penalised);
        await RunAsync(seeded.Tenant, seeded.Stall);
        await RunAsync(seeded.Tenant, seeded.Stall);

        var fine = (await AssessedAsync(seeded.Tenant))[0];
        await using var read = db.CreateContext(seeded.Tenant);
        var facts = await new FineAssessmentCollectionSource(read).LoadAsync(seeded.Tenant, fine.Id, Today, tracked: false);

        Assert.Equal(RevenueInstrumentType.OfficialReceipt, facts!.Policy.PermittedInstrumentType);
        Assert.Equal(fine.AmountDue, facts.Quote.Remaining);
        Assert.Single(await read.RevenueClassificationPolicies.AsNoTracking().ToListAsync());
    }

    [SkippableFact]
    public async Task AFinesSourceTheOfficeAlreadyHas_IsLeftAsItIs()
    {
        var seeded = await SeedAsync("LAT-R", Penalised);
        var source = RevenueClassification.Create(Domain.Constants.RevenueClassificationCodes.PenaltiesAndFines, seeded.Tenant);
        await using (var write = db.CreateContext(seeded.Tenant))
        {
            write.Add(source);
            write.Add(RevenueClassificationPolicy.Create(source.Id, Oldest, "Office fines", RevenueInstrumentType.CashTicket, seeded.Tenant));
            await write.SaveChangesAsync();
        }

        await RunAsync(seeded.Tenant, seeded.Stall);

        await using var read = db.CreateContext(seeded.Tenant);
        var policy = Assert.Single(await read.RevenueClassificationPolicies.AsNoTracking().ToListAsync());
        Assert.Equal(("Office fines", RevenueInstrumentType.CashTicket), (policy.DisplayName, policy.PermittedInstrumentType));
    }

    [SkippableFact]
    public async Task ThePenalty_IsOnTheStatement_AndThePortalStatesTheSameTotal_WithoutOfferingItOnline()
    {
        var seeded = await SeedAsync("LAT-H", Penalised);
        var payor = PayorUser.Create("Maria Santos", "09170000021", new HashedPassword("hash"));
        await using (var write = db.CreateContext(seeded.Tenant))
        {
            write.AddRange(payor, PayorStallLink.Create(payor.Id, seeded.Stall));
            await write.SaveChangesAsync();
        }
        await RunAsync(seeded.Tenant, seeded.Stall);

        await using var read = db.CreateContext(seeded.Tenant);
        var statement = await new FacilityReportsRepository(read).GetStallStatementAsync(seeded.Stall, CancellationToken.None);
        var portal = new PayorRepository(read, null!);
        var balance = Assert.Single(await portal.GetBalancesAsync(payor.Id));
        var row = Assert.Single(await portal.GetPayableItemsAsync(payor.Id), i => i.Kind == PayorPayableKind.Penalty);

        Assert.Equal(60m, statement!.PenaltiesDue);
        Assert.Equal(statement.TotalDue, balance.OutstandingBalance);
        Assert.Equal(60m, row.BalanceDue);
        Assert.False(row.PayableOnline);
    }

    [SkippableFact]
    public async Task TheHeadsUpdate_ChangesWhatIsOwedEverywhere_AndKeepsTheOriginal()
    {
        var seeded = await SeedAsync("LAT-I", Penalised);
        await RunAsync(seeded.Tenant, seeded.Stall);
        var fine = (await AssessedAsync(seeded.Tenant))[0];

        await using var head = db.CreateContext(seeded.Tenant);
        var result = await Fines(head, seeded.Tenant).AdjustAsync(fine.Id, new(5m, "Reviewed with lessee", fine.Version));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal((5m, 5m), (result.Value!.Assessed, result.Value.Remaining));
        await using var read = db.CreateContext(seeded.Tenant);
        var stored = await read.FineAssessments.AsNoTracking().SingleAsync(a => a.Id == fine.Id);
        Assert.Equal(20m, stored.AssessedAmount);
        var @event = await read.FineAssessmentEvents.AsNoTracking().SingleAsync(e => e.AssessmentId == fine.Id && e.EventKind == "Adjusted");
        Assert.Equal((20m, 5m, "Reviewed with lessee"), (@event.PreviousAmount, @event.ResultingAmount, @event.Evidence));
        Assert.Equal(45m, (await new FacilityReportsRepository(read).GetStallStatementAsync(seeded.Stall, CancellationToken.None))!.PenaltiesDue);
    }

    [SkippableFact]
    public async Task OnlyTheHead_Updates_AndAStaleCopy_IsRefused()
    {
        var seeded = await SeedAsync("LAT-J", Penalised);
        await RunAsync(seeded.Tenant, seeded.Stall);
        var fine = (await AssessedAsync(seeded.Tenant))[0];

        await using var work = db.CreateContext(seeded.Tenant);
        Assert.Equal(ResultStatus.Forbidden, (await Fines(work, seeded.Tenant, "Admin").AdjustAsync(fine.Id, new(5m, "No", fine.Version))).Status);
        Assert.Equal(ResultStatus.Conflict, (await Fines(work, seeded.Tenant).AdjustAsync(fine.Id, new(5m, "Stale", fine.Version + 1))).Status);
        Assert.Equal(ResultStatus.Invalid, (await Fines(work, seeded.Tenant).AdjustAsync(fine.Id, new(5m, " ", fine.Version))).Status);
    }

    [SkippableFact]
    public async Task AnotherOffices_Penalty_CannotBeReachedOrUpdated()
    {
        var seeded = await SeedAsync("LAT-K", Penalised);
        await RunAsync(seeded.Tenant, seeded.Stall);
        var fine = (await AssessedAsync(seeded.Tenant))[0];
        var other = Municipality.Create("LAT-L", "Other", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: "lat-l");
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(other);
            await setup.SaveChangesAsync();
        }

        await using var theirs = db.CreateContext(other.Id);
        // The other office charges penalties too, so only the tenant boundary can keep it off this stall.
        theirs.Add(BillingPolicyVersion.Create(other.Id, Oldest, Oldest, Penalised, "Their ordinance", Guid.NewGuid(), DateTime.UtcNow));
        await theirs.SaveChangesAsync();

        Assert.Equal(ResultStatus.NotFound, (await Fines(theirs, other.Id).AdjustAsync(fine.Id, new(0m, "Reach across", fine.Version))).Status);
        Assert.Equal(0, await Assessor(theirs, other.Id).AssessStallAsync(seeded.Stall));
        Assert.DoesNotContain(seeded.Stall, await Assessor(theirs, other.Id).StallsToReviewAsync());
        Assert.Equal(3, (await AssessedAsync(seeded.Tenant)).Count);
    }
}
