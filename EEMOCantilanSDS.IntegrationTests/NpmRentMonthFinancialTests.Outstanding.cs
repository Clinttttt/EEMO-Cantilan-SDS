using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class NpmRentMonthFinancialTests
{
    private async Task EnableOutstanding(OpeningWorld world)
    {
        await using var db = fixture.Create(world.World.Tenant);
        var service = GovernedService.Create(world.World.Tenant, CollectorOperationCodes.NpmDaily, "Synthetic Head");
        db.AddRange(service, GovernedServiceSetting.Create(world.World.Tenant, service.Id, new(2020, 1, 1),
            GovernedServiceBasis.DirectApprovedAmount, null, null, true, true, "Synthetic Head"));
        await db.SaveChangesAsync();
    }
    private NpmOutstandingRentWorkflow Outstanding(AppDbContext db, OpeningWorld world, bool prospective = false, bool partials = true)
    {
        var clock = new OpeningClock(new(2026, 10, 20)); var rates = new FeeRateResolver(db);
        var month = OpeningWorkflow(db, world, approved: prospective);
        var dayRepository = new DailyCollectionRepository(db); var collectors = new CollectorRepository(db);
        var legacy = new NpmMonthSettlementService(dayRepository, new NpmMarketClosureRepository(db), rates, clock);
        var settlement = new NpmMonthSettlementBridge(legacy, month, clock);
        var stallRepository = new StallRepository(db, rates, clock, npmRentMonths: month);
        var actor = new Actor(world.World); var tenant = new Tenant(world.World.Tenant); var cache = new OpeningCache(); var code = new OpeningTenantCode();
        var poster = new NpmDailyCanonicalPoster(db, actor, tenant, new GovernedCanonicalAuthority(db, tenant), month);
        var whole = new NpmWholePaymentWorkflow(stallRepository, collectors, dayRepository, settlement, poster, actor, clock, cache, code, month);
        var part = new NpmPartPaymentWorkflow(stallRepository, collectors, dayRepository, settlement, poster, actor, clock, cache, code, new(partials), month);
        return new(stallRepository, collectors, dayRepository, settlement, whole, part, poster, month, actor, tenant, clock, cache, code,
            NullLogger<NpmOutstandingRentWorkflow>.Instance);
    }
    private async Task<OpeningWorld> SeedLegacyOutstanding()
    {
        var world = await SeedOpening(new(2026, 6, 1)); await EnableOutstanding(world);
        await using var db = fixture.Create(world.World.Tenant);
        foreach (var (month, paidDays) in new[] { (6, 22), (7, 4), (8, 30), (10, 13) })
            for (var day = 1; day <= paidDays; day++)
            {
                var date = new DateOnly(2026, month, day);
                var receipt = DailyCollection.Create(world.World.Stall, date, "Historical synthetic cash", 30);
                receipt.MarkPaid($"LEGACY-{month}-{day}", null); db.DailyCollections.Add(receipt);
            }
        await db.SaveChangesAsync(); return world;
    }
    [SkippableTheory]
    [InlineData(2430, 0)]
    [InlineData(1000, 1430)]
    public async Task Outstanding_rent_posts_one_SRC_oldest_first_and_preserves_legacy_cash(decimal amount, decimal remaining)
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var world = await SeedLegacyOutstanding();
        await using var db = fixture.Create(world.World.Tenant); var flow = Outstanding(db, world);
        var preview = await flow.QuoteAsync(world.World.Stall, world.Contract, amount);
        Assert.True(preview.IsSuccess, preview.Error); var q = preview.Value!;
        Assert.Equal(2430, q.TotalOutstanding); Assert.Equal(remaining, q.RemainingAfter);
        Assert.Equal(240, q.Periods[0].Applied);
        if (amount == 1000) { Assert.Equal(760, q.Periods[1].Applied); Assert.Equal(20, q.Periods[1].Remaining); }
        var request = new NpmOutstandingRentPayment(Guid.NewGuid(), q.StallId, q.ContractId, q.BusinessDate, amount, q.QuoteToken);
        var result = await flow.PostAsync(request); Assert.True(result.IsSuccess, result.Error);
        var replay = await flow.PostAsync(request); Assert.True(replay.IsSuccess, replay.Error); Assert.True(replay.Value!.Existing);
        Assert.Equal(result.Value!.ReferenceCode, replay.Value.ReferenceCode);
        Assert.Single(await db.Collections.ToListAsync()); Assert.Equal(amount, await db.CollectionAllocations.SumAsync(x => x.Amount));
        var receiptLines = await db.CollectionLines.Include(x => x.Allocations).OrderBy(x => x.Amount).ToListAsync();
        Assert.All(receiptLines, line => Assert.Single(line.Allocations.Select(x =>
            System.Text.Json.JsonDocument.Parse(x.SourceSnapshot!).RootElement.GetProperty("periodStart").GetString()).Distinct()));
        Assert.Equal(69, await db.DailyCollections.CountAsync(x => x.ORNumber.StartsWith("LEGACY-")));
        var after = await flow.QuoteAsync(world.World.Stall, world.Contract);
        Assert.True(after.IsSuccess, after.Error); Assert.Equal(remaining, after.Value!.TotalOutstanding);
        if (amount == 1000) Assert.Equal(20, after.Value.Periods.Single(x => x.PeriodStart.Month == 7).Outstanding);
        var tenant = new Tenant(world.World.Tenant);
        var poster = new NpmDailyCanonicalPoster(db, new Actor(world.World), tenant, new GovernedCanonicalAuthority(db, tenant), OpeningWorkflow(db, world, approved: false));
        var reversed = await poster.VoidAsync(result.Value.CollectionId, "Synthetic cross-period correction");
        Assert.True(reversed.IsSuccess, reversed.Error);
        Assert.Equal(2430, (await flow.QuoteAsync(world.World.Stall, world.Contract)).Value!.TotalOutstanding);
        Assert.Single(await db.Collections.ToListAsync());
        Assert.Equal(result.Value.ReferenceCode, (await db.Collections.SingleAsync()).ReferenceCode);
    }
    [SkippableFact]
    public async Task Outstanding_fifty_is_a_month_allocation_not_a_changed_daily_tariff()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var world = await SeedOpening(new(2026, 10, 1)); await EnableOutstanding(world);
        await using var db = fixture.Create(world.World.Tenant);
        var month = await PrepareAndActivateOpening(db, world, OpeningWorkflow(db, world));
        var flow = Outstanding(db, world, prospective: true);
        var preview = await flow.QuoteAsync(world.World.Stall, world.Contract, 50);
        Assert.True(preview.IsSuccess, preview.Error); var q = preview.Value!;
        Assert.Equal(850, q.RemainingAfter);
        var posted = await flow.PostAsync(new(Guid.NewGuid(), q.StallId, q.ContractId, q.BusinessDate, 50, q.QuoteToken));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Single(await db.Collections.ToListAsync()); Assert.Empty(await db.DailyCollections.ToListAsync());
        var allocation = Assert.Single(await db.CollectionAllocations.ToListAsync());
        Assert.Equal(month.Id, allocation.NpmRentMonthId); Assert.Equal(50, allocation.Amount);
        Assert.Equal(30, (await OpeningWorkflow(db, world).ReviewOpeningMonthAsync(q.StallId, q.ContractId, month.PeriodStart)).Value!.Authority.ApprovedDailyInstallment);
        Assert.Equal(850, (await OpeningWorkflow(db, world).ReadPositionAsync(q.StallId, month.PeriodStart))!.Outstanding);
    }

    [SkippableFact]
    public async Task Outstanding_midmonth_payments_reconcile_with_Head_earned_ledger()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var world = await SeedOpening(new(2026, 10, 20)); await EnableOutstanding(world);
        await using var db = fixture.Create(world.World.Tenant);
        var monthly = OpeningWorkflow(db, world);
        await PrepareAndActivateOpening(db, world, monthly);
        var flow = Outstanding(db, world, prospective: true);
        foreach (var amount in new[] { 30m, 870m })
        {
            var preview = await flow.QuoteAsync(world.World.Stall, world.Contract, amount);
            Assert.True(preview.IsSuccess, preview.Error); var q = preview.Value!;
            var posted = await flow.PostAsync(new(Guid.NewGuid(), q.StallId, q.ContractId, q.BusinessDate, amount, q.QuoteToken));
            Assert.True(posted.IsSuccess, posted.Error);
        }
        var ledger = await new PaymentRepository(db, new FeeRateResolver(db), new OpeningClock(new(2026, 10, 20)), monthly)
            .GetStallLedgerSummaryAsync(world.World.Stall, default);
        Assert.Equal(900, ledger.TotalCollected); Assert.Equal(0, ledger.TotalOutstanding);
        Assert.Equal(0, (await flow.QuoteAsync(world.World.Stall, world.Contract)).Value!.TotalOutstanding);
    }

    [SkippableFact]
    public async Task Outstanding_mixed_legacy_and_month_sources_share_one_receipt_and_reverse_together()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var world = await SeedLegacyOutstanding();
        await using var db = fixture.Create(world.World.Tenant);
        var stall = await db.Stalls.Include(x => x.Facility).Include(x => x.Contracts).SingleAsync();
        var original = await db.DailyCollections.AsNoTracking().Where(x => x.CollectionDate.Month == 10).ToListAsync();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(original.OrderBy(x => x.Id).Select(x => new { x.Id, x.CollectionDate, x.CollectedFee }).ToArray(), Json))));
        var utc = new OpeningClock(new(2026, 10, 20)).UtcNow.AddMinutes(-1);
        var source = NpmRentMonth.Prepare(stall.Facility!, stall, stall.Contracts.Single(), new(2026, 10, 1), new(2026, 10, 20),
            new(900, 0, NpmMonthBasis.RentGoal, new(2026, 10, 31), "Synthetic reviewed mixed-source cutover",
                "{\"dailyInstallmentsApproved\":true}", "{\"credit\":0}"), world.World.Actor, "Synthetic Head", "SuperAdmin", utc).Month;
        source.Reconcile(1, world.World.Actor, "Synthetic Head", "SuperAdmin", utc,
            System.Text.Json.JsonSerializer.Serialize(new NpmRentMonthOpeningEvidence(1, original.Select(x => x.Id).ToArray(), [], hash), Json));
        source.ActivateSettlement(2, world.World.Actor, "Synthetic Head", "SuperAdmin", utc, "Synthetic mixed-source acceptance");
        db.NpmRentMonths.Add(source); await db.SaveChangesAsync();
        var flow = Outstanding(db, world);
        var preview = await flow.QuoteAsync(stall.Id, world.Contract);
        Assert.True(preview.IsSuccess, preview.Error); var q = preview.Value!;
        Assert.Equal(2430, q.TotalOutstanding);
        Assert.Contains(q.Periods, x => x.SourceKind == CollectionSourceKind.NpmRentMonth);
        Assert.Contains(q.Periods, x => x.SourceKind == CollectionSourceKind.DailyCollection);
        var paid = await flow.PostAsync(new(Guid.NewGuid(), q.StallId, q.ContractId, q.BusinessDate, q.Amount, q.QuoteToken));
        Assert.True(paid.IsSuccess, paid.Error); Assert.Single(await db.Collections.ToListAsync());
        Assert.Equal(2430, await db.CollectionAllocations.SumAsync(x => x.Amount));
        var tenant = new Tenant(world.World.Tenant);
        var poster = new NpmDailyCanonicalPoster(db, new Actor(world.World), tenant, new GovernedCanonicalAuthority(db, tenant), OpeningWorkflow(db, world, approved: false));
        var reversed = await poster.VoidAsync(paid.Value!.CollectionId, "Synthetic whole-receipt reversal");
        Assert.True(reversed.IsSuccess, reversed.Error);
        Assert.Equal(2430, (await flow.QuoteAsync(stall.Id, world.Contract)).Value!.TotalOutstanding);
        Assert.Single(await db.Collections.ToListAsync()); Assert.Single(await db.CollectionCorrections.ToListAsync());
    }
    [SkippableFact]
    public async Task Outstanding_partial_policy_is_not_bypassed_and_stale_quotes_create_no_cash()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var world = await SeedLegacyOutstanding();
        await using var db = fixture.Create(world.World.Tenant);
        var disabled = await Outstanding(db, world, partials: false).QuoteAsync(world.World.Stall, world.Contract, 1000);
        Assert.False(disabled.IsSuccess); Assert.Contains("not enabled", disabled.Error);
        Assert.Empty(await db.Collections.ToListAsync());
        var flow = Outstanding(db, world); var q = (await flow.QuoteAsync(world.World.Stall, world.Contract, 1000)).Value!;
        var bad = await flow.PostAsync(new(Guid.NewGuid(), q.StallId, q.ContractId, q.BusinessDate, 1000, "stale-token"));
        Assert.False(bad.IsSuccess); Assert.Empty(await db.Collections.ToListAsync());
        Assert.Empty(await db.PostingOperations.ToListAsync());
    }

    [SkippableFact]
    public async Task Outstanding_two_reviewed_payments_cannot_collect_the_same_balance()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var world = await SeedLegacyOutstanding();
        await using var first = fixture.Create(world.World.Tenant);
        await using var second = fixture.Create(world.World.Tenant);
        var a = Outstanding(first, world); var b = Outstanding(second, world);
        var q = (await a.QuoteAsync(world.World.Stall, world.Contract)).Value!;
        var reviewed = (await b.QuoteAsync(world.World.Stall, world.Contract)).Value!;
        var results = await Task.WhenAll(a.PostAsync(new(Guid.NewGuid(), q.StallId, q.ContractId, q.BusinessDate, q.Amount, q.QuoteToken)),
            b.PostAsync(new(Guid.NewGuid(), reviewed.StallId, reviewed.ContractId, reviewed.BusinessDate, reviewed.Amount, reviewed.QuoteToken)));
        Assert.Single(results.Where(x => x.IsSuccess));
        await using var verify = fixture.Create(world.World.Tenant);
        Assert.Equal(2430, await verify.Collections.SumAsync(x => x.TotalAmount));
        Assert.Single(await verify.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Outstanding_wrong_tenant_and_wrong_contract_are_refused_without_cash()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var world = await SeedLegacyOutstanding();
        await using var other = fixture.Create(Guid.NewGuid());
        Assert.False((await Outstanding(other, world).QuoteAsync(world.World.Stall, world.Contract)).IsSuccess);
        await using var own = fixture.Create(world.World.Tenant);
        Assert.False((await Outstanding(own, world).QuoteAsync(world.World.Stall, Guid.NewGuid())).IsSuccess);
        Assert.Empty(await own.Collections.ToListAsync());
    }

    [Fact]
    public void Outstanding_synthetic_partial_approval_does_not_enable_other_contracts()
    {
        var approved = Guid.NewGuid(); var options = new NpmPartPaymentOptions(false, new HashSet<Guid> { approved });
        Assert.True(options.Allows(approved)); Assert.False(options.Allows(Guid.NewGuid())); Assert.False(options.Allows(Guid.Empty));
        Assert.False(new NpmPartPaymentOptions().Allows(approved));
    }

    private sealed class RefuseOutstandingSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData data,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<Collection>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Synthetic failure before cash commit");
            return ValueTask.FromResult(result);
        }
    }

    [SkippableFact]
    public async Task Outstanding_failed_cross_period_commit_leaves_no_cash_or_partly_paid_days()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var world = await SeedLegacyOutstanding();
        await using (var db = fixture.Create(world.World.Tenant, new RefuseOutstandingSave()))
        {
            var flow = Outstanding(db, world); var q = (await flow.QuoteAsync(world.World.Stall, world.Contract, 1000)).Value!;
            Assert.False((await flow.PostAsync(new(Guid.NewGuid(), q.StallId, q.ContractId, q.BusinessDate, q.Amount, q.QuoteToken))).IsSuccess);
        }
        await using var verify = fixture.Create(world.World.Tenant);
        Assert.Empty(await verify.Collections.ToListAsync()); Assert.Empty(await verify.PostingOperations.ToListAsync());
        Assert.Equal(69, await verify.DailyCollections.CountAsync());
        Assert.Equal(2430, (await Outstanding(verify, world).QuoteAsync(world.World.Stall, world.Contract)).Value!.TotalOutstanding);
    }
}
