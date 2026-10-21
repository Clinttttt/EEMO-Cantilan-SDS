using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueSourcePerformance;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// READ-ONLY audit of the authorized localhost preview through the real readers. Skipped unless the same environment gate as the
/// activation runner is set. It never saves: the context is used for queries only, and the connection must be the loopback preview.
/// </summary>
public sealed class PreviewReconciliationAudit(ITestOutputHelper output)
{
    private sealed class Actor(Guid userId, Guid tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "audit";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "preview";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class NowClock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    [SkippableFact]
    public async Task Audit_the_preview_bases_and_totals_without_writing()
    {
        var connection = Environment.GetEnvironmentVariable("STALLTRACK_PREVIEW_CONNECTION");
        var tenantText = Environment.GetEnvironmentVariable("STALLTRACK_PREVIEW_TENANT");
        Skip.If(string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(tenantText), "Preview audit not requested.");
        Assert.Empty(EEMOCantilanSDS.Application.Common.Revenue.PreviewMonthlyRentGate.DatabaseRefusals(connection, "stalltrack_preview", 55489));
        var tenantId = Guid.Parse(tenantText!);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(new MunicipalityStampInterceptor())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;
        await using var db = new AppDbContext(options, new Tenant(tenantId));
        var today = PhilippineTime.Today;
        var reader = new CollectionActivityReader(db);

        var collections = await db.Collections.OrderBy(x => x.ReferenceNumber).ToListAsync();
        var corrections = await db.CollectionCorrections.ToListAsync();
        foreach (var c in collections)
            output.WriteLine($"collection {c.ReferenceCode} {c.TotalAmount} recorded {c.RecordedAtUtc:O}");
        foreach (var k in corrections)
            output.WriteLine($"correction {k.CorrectionType} {k.FinancialEffectAmount} original {collections.Single(x => x.Id == k.OriginalCollectionId).ReferenceCode} " +
                $"effective {k.CorrectionEffectiveDate} recorded {k.RecordedAtUtc:O}");

        var reversed = corrections.Single();
        var original = collections.Single(x => x.Id == reversed.OriginalCollectionId);
        var between = original.RecordedAtUtc.AddSeconds(1);
        Assert.True(between < reversed.RecordedAtUtc, "The cutoff must fall between posting and reversal.");

        output.WriteLine("== AS ORIGINALLY KNOWN (cutoff between post and reversal)");
        var known = await reader.GetReconciliationAsync(tenantId, today, today, between, false);
        foreach (var e in known) output.WriteLine($"  {e.ReferenceCode} amount {e.Amount} effect {e.CorrectionEffect} net {e.NetAmount} corrections {e.Corrections.Count}");
        output.WriteLine($"  total net {known.Sum(x => x.NetAmount)}");
        output.WriteLine("== LATEST CORRECTED");
        var latest = await reader.GetReconciliationAsync(tenantId, today, today, DateTime.UtcNow, false);
        foreach (var e in latest) output.WriteLine($"  {e.ReferenceCode} amount {e.Amount} effect {e.CorrectionEffect} net {e.NetAmount} corrections {e.Corrections.Count}");
        output.WriteLine($"  total net {latest.Sum(x => x.NetAmount)}");
        output.WriteLine("== CORRECTION ACTIVITY");
        var activity = await reader.GetReconciliationAsync(tenantId, reversed.CorrectionEffectiveDate, reversed.CorrectionEffectiveDate, DateTime.UtcNow, true);
        foreach (var e in activity) output.WriteLine($"  {e.ReferenceCode} original {e.Amount} (context) effects {string.Join(",", e.Corrections.Select(x => x.FinancialEffect))}");

        output.WriteLine("== COLLECTION ACTIVITY (authoritative feed, each real collection once)");
        var feed = await reader.GetAsync(tenantId, today, today);
        foreach (var g in feed.GroupBy(x => x.Authority))
            output.WriteLine($"  {g.Key}: events {g.Count()} gross {g.Sum(x => x.Amount)} correction {g.Sum(x => x.CorrectionEffect)} net {g.Sum(x => x.NetAmount)}");
        output.WriteLine($"  all: gross {feed.Sum(x => x.Amount)} net {feed.Sum(x => x.NetAmount)}");

        var actor = new Actor(Guid.NewGuid(), tenantId); var tenant = new Tenant(tenantId); var clock = new NowClock();
        var report = new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), actor, tenant, clock);
        var statement = (await report.Handle(new(today.Year, today.Month), default)).Value!;
        output.WriteLine("== OFFICIAL MONTHLY INCOME (rows with any amount)");
        foreach (var row in statement.Groups.SelectMany(g => g.Rows))
        {
            var cell = row.Months[today.Month - 1];
            if (cell.SystemAmount != 0m || cell.AdjustmentAmount != 0m)
                output.WriteLine($"  {row.Key}: legacy {cell.Legacy} canonical {cell.Canonical} system {cell.SystemAmount} adjustment {cell.AdjustmentAmount} official {cell.OfficialAmount}");
        }
        output.WriteLine($"  grand: system {statement.GrandTotal.SystemAmount}");
        var performance = (await new GetRevenueSourcePerformanceQueryHandler(report, db, tenant).Handle(new(today.Year, today.Month), default)).Value!;
        output.WriteLine($"== REVENUE PERFORMANCE / OVERVIEW: collected (cash basis) {performance.TotalCollected}");
        foreach (var row in performance.Rows.Where(x => x.Collected != 0m || x.AdjustmentAmount != 0m))
            output.WriteLine($"  {row.Key}: collected {row.Collected} adjustment {row.AdjustmentAmount} official {row.OfficialAmount}");
    }
}
