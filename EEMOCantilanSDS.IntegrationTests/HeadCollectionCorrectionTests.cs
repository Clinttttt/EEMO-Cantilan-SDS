using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>Head-only reversal of an Office-recorded canonical rent receipt, on a real disposable PostgreSQL database.</summary>
[Collection(PostgresCollection.Name)]
public sealed class HeadCollectionCorrectionTests(PostgresFixture db)
{
    private sealed class Actor(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role.ToLowerInvariant();
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "head-test";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class TestClock(DateOnly today) : IClock
    {
        public DateOnly PhilippineToday { get; set; } = today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => DateTime.SpecifyKind(PhilippineToday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
    }

    private static readonly DateOnly Today = PhilippineTime.Today;
    private sealed record World(Guid TenantId, Guid FacilityId, Guid HeadId, PreviewMonthlyRentTarget Target, string Connection,
        PreviewRentActivation.Outcome Source, Guid FirstSrcId, Guid SecondSrcId, string FirstSrc, string SecondSrc);

    private HeadCollectionCorrectionWorkflow Head(AppDbContext context, World w, string role = "SuperAdmin", Guid? tenant = null, DateOnly? today = null) =>
        new(context, new CollectionSessionStore(context), new Actor(w.HeadId, tenant ?? w.TenantId, role), new Tenant(tenant ?? w.TenantId), new TestClock(today ?? Today));

    private async Task<World> ArrangeAsync()
    {
        await db.ResetAsync();
        var municipality = Municipality.Create($"HCC-{Guid.NewGuid():N}"[..24], "Head Correction Test", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"hcc-{Guid.NewGuid():N}"[..32]);
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Municipalities.Add(municipality); await setup.SaveChangesAsync(); }
        var facility = Facility.Create(FacilityCode.NCC, "NCC", "NCC", municipalityId: municipality.Id);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.ForMonthlyRental(FacilityCode.NCC), municipality.Id);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1),
            "Permanent Stall Rent", RevenueInstrumentType.OfficialReceipt, municipality.Id);
        await using (var context = db.CreateContext(municipality.Id)) { context.AddRange(facility, classification, policy); await context.SaveChangesAsync(); }
        var connection = db.ApiConnectionString;
        var b = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        var target = new PreviewMonthlyRentTarget(b.Database!, b.Port, municipality.Id, FacilityCode.NCC);
        var head = Guid.NewGuid();

        await using var main = db.CreateContext(municipality.Id);
        var source = await PreviewRentActivation.RunAsync(main, target, connection, head, Today.Year, Today.Month);
        var options = PreviewMonthlyRentGate.OptionsFor(true, target, connection, out _);
        var flow = new CollectionComposerWorkflow(main, new Actor(head, municipality.Id, "SuperAdmin"), new Tenant(municipality.Id), rentOptions: options);
        var quote = (await flow.GetRentObligationAsync(source.StallId, Today.Year, Today.Month)).Value!;
        var first = await flow.PostOfficeRentAsync(new(Guid.NewGuid(), source.StallId, Today.Year, Today.Month, 600, quote.SourceVersion, Today, source.ContractId));
        Assert.True(first.IsSuccess, first.Error);
        var rest = (await flow.GetRentObligationAsync(source.StallId, Today.Year, Today.Month)).Value!;
        var second = await flow.PostOfficeRentAsync(new(Guid.NewGuid(), source.StallId, Today.Year, Today.Month, 600, rest.SourceVersion, Today, source.ContractId));
        Assert.True(second.IsSuccess, second.Error);
        return new(municipality.Id, facility.Id, head, target, connection, source, first.Value!.CollectionId, second.Value!.CollectionId,
            first.Value.ReferenceCode, second.Value.ReferenceCode);
    }

    private async Task<(decimal Outstanding, PaymentStatus Status)> MonthAsync(World w)
    {
        await using var read = db.CreateContext(w.TenantId);
        var quote = (await new CollectionComposerWorkflow(read, new Actor(w.HeadId, w.TenantId, "SuperAdmin"), new Tenant(w.TenantId))
            .GetRentObligationAsync(w.Source.StallId, Today.Year, Today.Month)).Value!;
        var record = await read.PaymentRecords.AsNoTracking().SingleAsync(x => x.Id == w.Source.PaymentRecordId);
        return (quote.OutstandingAmount, record.Status);
    }

    [SkippableFact]
    public async Task The_Head_reverses_one_receipt_once_the_SRC_is_kept_and_the_month_reopens_exactly_once()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var w = await ArrangeAsync();
        Assert.Equal((0m, PaymentStatus.Paid), await MonthAsync(w));
        await using var context = db.CreateContext(w.TenantId);
        var operation = Guid.NewGuid();

        var review = (await Head(context, w).ReviewAsync(w.SecondSrcId)).Value!;
        Assert.True(review.Eligibility.CanCorrect); Assert.Equal(w.SecondSrc, review.ReferenceCode);

        var done = await Head(context, w).ReverseAsync(new(operation, w.SecondSrcId, "Entered on the wrong month"));
        Assert.True(done.IsSuccess, done.Error); Assert.Equal(600, done.Value!.ReopenedAmount); Assert.False(done.Value.ExistingOutcome);

        // The original is untouched and no replacement Collection exists to hide it.
        await using var read = db.CreateContext(w.TenantId);
        var originals = await read.Collections.AsNoTracking().OrderBy(x => x.ReferenceNumber).ToListAsync();
        Assert.Equal([w.FirstSrc, w.SecondSrc], originals.Select(x => x.ReferenceCode).ToArray());
        Assert.Equal([600m, 600m], originals.Select(x => x.TotalAmount).ToArray());
        var correction = await read.CollectionCorrections.Include(x => x.Lines).SingleAsync();
        Assert.Equal((w.SecondSrcId, CollectionCorrectionType.Reversal, -600m, "Entered on the wrong month"),
            (correction.OriginalCollectionId, correction.CorrectionType, correction.FinancialEffectAmount, correction.Reason));
        Assert.Null(correction.ReplacementCollectionId); Assert.Equal(w.HeadId.ToString("N"), correction.ActorId);
        Assert.Equal((600m, PaymentStatus.Partial), await MonthAsync(w));

        // A retry returns the original outcome; a different operation on the same receipt is refused; the balance moves once.
        var replay = await Head(context, w).ReverseAsync(new(operation, w.SecondSrcId, "Entered on the wrong month"));
        Assert.True(replay.IsSuccess); Assert.True(replay.Value!.ExistingOutcome); Assert.Equal(done.Value.CorrectionId, replay.Value.CorrectionId);
        var again = await Head(context, w).ReverseAsync(new(Guid.NewGuid(), w.SecondSrcId, "Second try"));
        Assert.False(again.IsSuccess); Assert.Equal("AlreadyCorrected", again.Error);
        var changed = await Head(context, w).ReverseAsync(new(operation, w.SecondSrcId, "A different reason"));
        Assert.Equal("CorrectionIntentConflict", changed.Error);
        Assert.Equal(1, await db.CreateContext(w.TenantId).CollectionCorrections.CountAsync());
        // The server is the authority for a stale tab: the review now reports the correction and offers nothing.
        var after = (await Head(context, w).ReviewAsync(w.SecondSrcId)).Value!;
        Assert.True(after.HasCorrection); Assert.False(after.Eligibility.CanCorrect); Assert.Equal("AlreadyCorrected", after.Eligibility.BlockCode);
        Assert.Equal((600m, PaymentStatus.Partial), await MonthAsync(w));
    }

    [SkippableFact]
    public async Task Only_the_Head_may_correct_and_another_tenant_cannot_see_or_touch_the_receipt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var w = await ArrangeAsync();
        await using var context = db.CreateContext(w.TenantId);

        var admin = await Head(context, w, "Admin").ReverseAsync(new(Guid.NewGuid(), w.SecondSrcId, "Not allowed"));
        Assert.False(admin.IsSuccess); Assert.Equal(ResultStatus.Forbidden, admin.Status);
        var collector = await Head(context, w, "Collector").ReverseAsync(new(Guid.NewGuid(), w.SecondSrcId, "Not allowed"));
        Assert.Equal(ResultStatus.Forbidden, collector.Status);
        var adminReview = (await Head(context, w, "Admin").ReviewAsync(w.SecondSrcId)).Value!;
        Assert.False(adminReview.Eligibility.CanCorrect); Assert.Equal("HeadOnly", adminReview.Eligibility.BlockCode);

        var other = Guid.NewGuid();
        await using var foreign = db.CreateContext(other);
        var cross = await Head(foreign, w, tenant: other).ReverseAsync(new(Guid.NewGuid(), w.SecondSrcId, "Cross tenant"));
        Assert.Equal("CollectionNotFound", cross.Error);
        Assert.Equal(ResultStatus.NotFound, (await Head(foreign, w, tenant: other).ReviewAsync(w.SecondSrcId)).Status);

        Assert.Empty(await context.CollectionCorrections.ToArrayAsync());
        Assert.Equal((0m, PaymentStatus.Paid), await MonthAsync(w));
    }

    [SkippableFact]
    public async Task Remitted_earlier_day_unknown_and_invalid_requests_are_refused_without_a_write()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var w = await ArrangeAsync();
        await using var context = db.CreateContext(w.TenantId);

        // An earlier business day (closed period) is refused.
        var tomorrow = await Head(context, w, today: Today.AddDays(1)).ReverseAsync(new(Guid.NewGuid(), w.SecondSrcId, "Too late"));
        Assert.Equal("OutsideCurrentBusinessDate", tomorrow.Error);
        // Missing reason, empty operation, unknown collection.
        Assert.Equal("InvalidReason", (await Head(context, w).ReverseAsync(new(Guid.NewGuid(), w.SecondSrcId, "  "))).Error);
        Assert.Equal("InvalidClientOperationId", (await Head(context, w).ReverseAsync(new(Guid.Empty, w.SecondSrcId, "x"))).Error);
        Assert.Equal("CollectionNotFound", (await Head(context, w).ReverseAsync(new(Guid.NewGuid(), Guid.NewGuid(), "x"))).Error);

        // Remitted cash is refused.
        var collector = CollectorUser.Create("Remitter", "R-01", "r-" + Guid.NewGuid().ToString("N")[..8], null, null, new HashedPassword("test-hash"), w.TenantId);
        context.CollectorUsers.Add(collector); await context.SaveChangesAsync();
        var remittance = CollectionRemittance.Record(w.TenantId, collector.Id, Today, Today, Today, null, 600, 600, 1, "Synthetic turnover", null,
            Guid.NewGuid(), "synthetic-intent", "Head", w.HeadId.ToString(), DateTime.UtcNow);
        context.Add(remittance); context.Add(CollectionRemittanceCoverage.Cover(w.TenantId, remittance.Id, w.SecondSrcId, 600));
        await context.SaveChangesAsync();
        var remitted = await Head(context, w).ReverseAsync(new(Guid.NewGuid(), w.SecondSrcId, "Already remitted"));
        Assert.Equal("Remitted", remitted.Error);
        var review = (await Head(context, w).ReviewAsync(w.SecondSrcId)).Value!;
        Assert.True(review.Remitted); Assert.False(review.Eligibility.CanCorrect); Assert.Equal("Remitted", review.Eligibility.BlockCode);
        // The other, unremitted receipt is still eligible.
        Assert.True((await Head(context, w).ReviewAsync(w.FirstSrcId)).Value!.Eligibility.CanCorrect);

        Assert.Empty(await context.CollectionCorrections.ToArrayAsync());
        Assert.Equal((0m, PaymentStatus.Paid), await MonthAsync(w));
    }

    [SkippableFact]
    public async Task Two_simultaneous_reversals_of_one_receipt_produce_one_correction_and_one_reopening()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var w = await ArrangeAsync();
        await using var a = db.CreateContext(w.TenantId);
        await using var b = db.CreateContext(w.TenantId);
        var results = await Task.WhenAll(
            Head(a, w).ReverseAsync(new(Guid.NewGuid(), w.SecondSrcId, "Racer one")),
            Head(b, w).ReverseAsync(new(Guid.NewGuid(), w.SecondSrcId, "Racer two")));

        Assert.Equal(1, results.Count(x => x.IsSuccess));
        Assert.Contains(results.Single(x => !x.IsSuccess).Error, new[] { "AlreadyCorrected", "ConcurrentCorrection" });
        await using var read = db.CreateContext(w.TenantId);
        Assert.Single(await read.CollectionCorrections.ToArrayAsync());
        Assert.Equal((600m, PaymentStatus.Partial), await MonthAsync(w));
    }

    [SkippableFact]
    public async Task Reports_follow_the_reversal_as_originally_known_versus_latest_and_cash_stays_apart_from_head_adjustments()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var w = await ArrangeAsync();
        await using var context = db.CreateContext(w.TenantId);
        await Task.Delay(50);
        var beforeCorrection = DateTime.UtcNow;
        await Task.Delay(50);
        var done = await Head(context, w).ReverseAsync(new(Guid.NewGuid(), w.SecondSrcId, "Wrong month"));
        Assert.True(done.IsSuccess, done.Error);

        await using var read = db.CreateContext(w.TenantId);
        var reader = new CollectionActivityReader(read);
        var known = (await reader.GetReconciliationAsync(w.TenantId, Today, Today, beforeCorrection, false)).Single(x => x.CollectionId == w.SecondSrcId);
        Assert.Equal((600m, 0m, 600m), (known.Amount, known.CorrectionEffect, known.NetAmount)); Assert.Empty(known.Corrections);
        var latest = (await reader.GetReconciliationAsync(w.TenantId, Today, Today, DateTime.UtcNow.AddMinutes(1), false)).Single(x => x.CollectionId == w.SecondSrcId);
        Assert.Equal((600m, -600m, 0m), (latest.Amount, latest.CorrectionEffect, latest.NetAmount)); Assert.Equal(w.SecondSrc, latest.ReferenceCode);
        var feed = (await reader.GetAsync(w.TenantId, Today, Today)).Single(x => x.CollectionId == w.SecondSrcId);
        Assert.Equal(-600m, Assert.Single(feed.Corrections).FinancialEffect); Assert.Equal(0m, feed.NetAmount);

        // Actual cash and the Head-only report adjustment stay separate.
        var actor = new Actor(w.HeadId, w.TenantId, "SuperAdmin"); var tenant = new Tenant(w.TenantId); var clock = new TestClock(Today);
        var report = new GetOfficialMonthlyIncomeQueryHandler(read, new LegacyMonthlyIncomeReader(read), actor, tenant, clock);
        var statement = (await report.Handle(new(Today.Year, Today.Month), default)).Value!;
        var cell = statement.Groups.SelectMany(g => g.Rows).Single(r => r.Key == "RENT_NCC").Months[Today.Month - 1];
        Assert.Equal(600m, cell.SystemAmount);   // 1,200 received - 600 reversed
        var adjusted = await new ReportGovernanceWorkflow(read, actor, tenant, clock, new LegacyMonthlyIncomeReader(read))
            .AdjustAsync(new(Guid.NewGuid(), "RENT_NCC", Today.Year, Today.Month, 600, 654, "Report-only revision"));
        Assert.True(adjusted.IsSuccess, adjusted.Error);
        var after = (await report.Handle(new(Today.Year, Today.Month), default)).Value!
            .Groups.SelectMany(g => g.Rows).Single(r => r.Key == "RENT_NCC").Months[Today.Month - 1];
        Assert.Equal((600m, 54m, 654m), (after.SystemAmount, after.AdjustmentAmount, after.OfficialAmount));
        Assert.Equal(2, await read.Collections.CountAsync()); Assert.Single(await read.CollectionCorrections.ToArrayAsync());
    }

}
