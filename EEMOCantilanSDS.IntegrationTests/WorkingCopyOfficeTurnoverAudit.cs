using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// READ-ONLY audit of the local-main WORKING COPY through the real workflows. Skipped unless STALLTRACK_WORKCOPY_CONNECTION and
/// STALLTRACK_WORKCOPY_TENANT are set; the connection must be the loopback working-copy database (the municipal database is refused).
/// Nothing is saved: only query methods and the read-only turnover reads are called.
/// </summary>
public sealed class WorkingCopyOfficeTurnoverAudit(ITestOutputHelper output)
{
    private sealed class Actor(Guid userId, Guid tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "audit";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "workcopy";
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
    public async Task Audit_which_collections_are_truly_Office_origin_and_how_the_turnover_reads_agree()
    {
        var connection = Environment.GetEnvironmentVariable("STALLTRACK_WORKCOPY_CONNECTION");
        var tenantText = Environment.GetEnvironmentVariable("STALLTRACK_WORKCOPY_TENANT");
        Skip.If(string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(tenantText), "Working-copy audit not requested.");
        Assert.Empty(PreviewMonthlyRentGate.DatabaseRefusals(connection, "stalltrack_workcopy_main", 55491));
        var tenantId = Guid.Parse(tenantText!);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(new MunicipalityStampInterceptor())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;
        await using var db = new AppDbContext(options, new Tenant(tenantId));
        var today = PhilippineTime.Today; var from = today.AddDays(-31);
        var head = await db.AdminUsers.Where(x => x.MunicipalityId == tenantId && !x.IsDeleted).Select(x => x.Id).FirstAsync();
        var actor = new Actor(head, tenantId); var tenant = new Tenant(tenantId);

        var all = await db.Collections.OrderBy(x => x.ReferenceNumber).Select(x => new { x.Id, x.ReferenceCode, x.BusinessDate, x.TotalAmount, x.CollectorId, x.ActorName }).ToListAsync();
        output.WriteLine($"canonical Collections: {all.Count}; Office-origin (no collector): {all.Count(x => x.CollectorId == null)}; collector: {all.Count(x => x.CollectorId != null)}");
        foreach (var x in all.Where(x => x.CollectorId == null)) output.WriteLine($"  OFFICE {x.ReferenceCode} {x.BusinessDate} {x.TotalAmount} by {x.ActorName}");
        output.WriteLine($"corrections: {await db.CollectionCorrections.CountAsync()}; remittance coverage active: {await db.CollectionRemittanceCoverages.CountAsync(x => x.IsActive)}; office coverage: {await db.OfficeCashTurnoverCoverages.CountAsync()}");

        var turnover = new OfficeCashTurnoverWorkflow(db, actor, tenant, new OfficeCashTurnoverOptions(true, new HashSet<Guid> { tenantId }), new NowClock());
        Assert.True((await turnover.IsAvailableAsync()).Value);
        var review = (await turnover.GetReviewAsync(new(from, today))).Value!;
        var position = (await turnover.GetPositionAsync(from, today)).Value!;
        output.WriteLine($"turnover review {from}..{today}: eligible {review.Collections.Count}, expected {review.ExpectedAmount}; " + string.Join(", ", review.Collections.Select(x => x.ReferenceCode)));
        output.WriteLine($"office position: collected {position.Collected} covered {position.Covered} pending {position.Pending} count {position.CollectionCount}");
        Assert.All(review.Collections, x => Assert.Contains(all, a => a.Id == x.CollectionId && a.CollectorId == null));   // collector cash is never offered

        var remit = new RemittanceWorkflow(db, actor, tenant);
        var collectors = (await remit.GetPositionAsync(from, today)).Value!;
        output.WriteLine($"collector position: collected {collectors.Collected} remitted {collectors.Remitted} unremitted {collectors.Unremitted}; office-origin note {collectors.OfficeOriginCollected}/{collectors.OfficeOriginCount}");
        Assert.Equal(position.Collected, collectors.OfficeOriginCollected);                                                   // two reads, one answer

        // Read-only: what the Head will see in Record direct fine, and that a quote (never a post) resolves with the evidence the authority demands.
        var direct = new DirectFineCollectionWorkflow(db, actor, tenant, new NowClock());
        var categories = (await direct.OptionsAsync()).Value!.Categories;
        output.WriteLine($"direct-fine categories offered: {categories.Count}" + string.Concat(categories.Select(c => $" [{c.Name}; payer={c.RequiresPayerParticulars} reason={c.RequiresReason} reference={c.RequiresSupportingReference}; {c.GoverningReference}]")));
        if (categories.Count == 1)
        {
            var quote = await direct.QuoteAsync(new(Guid.NewGuid(), categories[0].AuthorityId, today, 100m, "Synthetic acceptance test", "SYNTH-FINE-001", "SYNTHETIC OFFICE-TURNOVER TEST"));
            output.WriteLine($"quote (no post): success={quote.IsSuccess} instrument={quote.Value?.Instrument} error={quote.Error}");
            var incomplete = await direct.QuoteAsync(new(Guid.NewGuid(), categories[0].AuthorityId, today, 100m));
            output.WriteLine($"quote without the required evidence refused: {!incomplete.IsSuccess}");
            Assert.True(quote.IsSuccess); Assert.False(incomplete.IsSuccess);
        }

        var feed = await new CollectionActivityReader(db).GetAsync(tenantId, from, today);
        output.WriteLine($"activity: legacy {feed.Where(x => x.Authority == "Legacy").Sum(x => x.NetAmount)}, canonical {feed.Where(x => x.Authority == "Canonical").Sum(x => x.NetAmount)}");
        var report = new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), actor, tenant, new NowClock());
        var statement = (await report.Handle(new(today.Year, today.Month), default)).Value!;
        output.WriteLine($"official monthly income {today:yyyy-MM} system {statement.GrandTotal.SystemAmount}");
    }
}
