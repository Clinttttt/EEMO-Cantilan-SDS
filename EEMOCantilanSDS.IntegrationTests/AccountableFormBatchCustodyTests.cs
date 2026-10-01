using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Batch custody assignment and collector-to-collector transfer of physical accountable forms on PostgreSQL. A batch
/// assigns every collector's range or none; a serial is held by one collector at a time; a transfer closes one custody
/// interval and opens the next with from, to, by, at and why; an issued unit never moves and no step records money.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountableFormBatchCustodyTests(PostgresFixture db)
{
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Head(Guid userId, Guid tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "office";
        public string? Role => "Admin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "pg-custody";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed record World(Guid TenantId, Guid HeadId, Guid BookId, CollectorUser Ana, CollectorUser Ben, CollectorUser Cora);

    private async Task<World> SeedAsync(int units = 30)
    {
        var tenant = Municipality.Create($"cus-{Guid.NewGuid():N}"[..12], "Custody", "Province",
            MunicipalityStatus.Active, tenantCode: $"custody-{Guid.NewGuid():N}"[..28]);
        CollectorUser Collector(string name, string id) =>
            CollectorUser.Create(name, id, $"pg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        var (ana, ben, cora) = (Collector("Ana Reyes", "C-01"), Collector("Ben Cruz", "C-02"), Collector("Cora Lim", "C-03"));
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, ana, ben, cora);
            await setup.SaveChangesAsync();
        }
        var headId = Guid.NewGuid();
        await using var ctx = db.CreateContext(tenant.Id);
        var book = (await Custody(ctx, tenant.Id, headId).ReceiveAsync(new ReceiveAccountableFormBookRequest(
            RevenueInstrumentType.CashTicket, "CT book", "CT", 1, units, 6))).Value!;
        return new World(tenant.Id, headId, book.BookId, ana, ben, cora);
    }

    private static AccountableFormCustodyWorkflow Custody(AppDbContext ctx, Guid tenantId, Guid headId) =>
        new(ctx, new Head(headId, tenantId), new FixedTenant(tenantId));

    private static AssignAccountableFormBatchRequest Batch(World w, params (CollectorUser Collector, long First, long Last)[] lines) =>
        new(w.BookId, RevenueInstrumentType.CashTicket,
            lines.Select(x => new AccountableFormBatchLine(x.Collector.Id, x.First, x.Last)).ToList());

    [SkippableFact]
    public async Task ABatchAssignsEachCollectorsContiguousRange_WithOneAuditedCustodyIntervalPerUnit()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);

        var result = await Custody(ctx, w.TenantId, w.HeadId).AssignBatchAsync(Batch(w, (w.Ana, 1, 10), (w.Ben, 11, 15), (w.Cora, 16, 25)));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(25, result.Value);
        var documents = await ctx.AccountableDocuments.AsNoTracking().OrderBy(x => x.SerialNumber).ToListAsync();
        Assert.All(documents.Take(10), x => Assert.Equal(w.Ana.Id, x.AssignedUserId));
        Assert.All(documents.Skip(10).Take(5), x => Assert.Equal(w.Ben.Id, x.AssignedUserId));
        Assert.All(documents.Skip(15).Take(10), x => Assert.Equal(w.Cora.Id, x.AssignedUserId));
        Assert.All(documents.Skip(25), x => Assert.Equal(AccountableDocumentState.InOffice, x.State));
        Assert.Equal(25, await ctx.AccountableFormAssignments.CountAsync(x => x.ReturnedAtUtc == null && x.TransferredFromUserId == null));
        Assert.Equal(0, await ctx.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task ABatchWithOneUnavailableRange_AssignsNothing_AndNamesWhoHoldsTheTickets()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var custody = Custody(ctx, w.TenantId, w.HeadId);
        Assert.True((await custody.AssignRangeAsync(new AssignAccountableFormRangeRequest(w.BookId, w.Ana.Id, 1, 8))).IsSuccess);

        await using var fresh = db.CreateContext(w.TenantId);
        var result = await Custody(fresh, w.TenantId, w.HeadId).AssignBatchAsync(Batch(w, (w.Ben, 9, 12), (w.Cora, 6, 8)));

        Assert.False(result.IsSuccess);
        Assert.Contains("CT000006 – CT000008 are assigned to Ana Reyes", result.Error);
        Assert.Contains("No Cash Tickets were assigned", result.Error);
        // All or nothing: Ben's otherwise valid range stays in office.
        Assert.Equal(0, await fresh.AccountableDocuments.CountAsync(x => x.AssignedUserId == w.Ben.Id || x.AssignedUserId == w.Cora.Id));
        Assert.Equal(8, await fresh.AccountableFormAssignments.CountAsync());
    }

    [SkippableFact]
    public async Task OverlappingRanges_RepeatedCollectors_AndTheWrongInstrument_AreRefusedBeforeAnyWrite()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var custody = Custody(ctx, w.TenantId, w.HeadId);

        Assert.False((await custody.AssignBatchAsync(Batch(w, (w.Ana, 1, 10), (w.Ben, 10, 12)))).IsSuccess);
        Assert.False((await custody.AssignBatchAsync(Batch(w, (w.Ana, 1, 5), (w.Ana, 6, 10)))).IsSuccess);
        Assert.False((await custody.AssignBatchAsync(Batch(w, (w.Ana, 1, 31)))).IsSuccess);
        var asReceipts = await custody.AssignBatchAsync(new AssignAccountableFormBatchRequest(w.BookId, RevenueInstrumentType.OfficialReceipt,
            [new AccountableFormBatchLine(w.Ana.Id, 1, 5)]));
        Assert.False(asReceipts.IsSuccess);
        Assert.Equal(0, await ctx.AccountableFormAssignments.CountAsync());
    }

    [SkippableFact]
    public async Task TwoConcurrentBatchesOverTheSameTickets_OnlyOneWins_AndEveryUnitHasOneCustodian()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var first = db.CreateContext(w.TenantId);
        await using var second = db.CreateContext(w.TenantId);

        var results = await Task.WhenAll(
            Custody(first, w.TenantId, w.HeadId).AssignBatchAsync(Batch(w, (w.Ana, 1, 10), (w.Ben, 11, 20))),
            Custody(second, w.TenantId, w.HeadId).AssignBatchAsync(Batch(w, (w.Cora, 5, 15))));

        Assert.Equal(1, results.Count(x => x.IsSuccess));
        await using var check = db.CreateContext(w.TenantId);
        var open = await check.AccountableFormAssignments.Where(x => x.ReturnedAtUtc == null).ToListAsync();
        Assert.Equal(open.Count, open.Select(x => x.AccountableDocumentId).Distinct().Count());
        Assert.True(open.Count is 20 or 11);
    }

    [SkippableFact]
    public async Task ATransferMovesUnusedTickets_WithFromToByAtAndWhy_AndNeverMovesAnIssuedTicket()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var custody = Custody(ctx, w.TenantId, w.HeadId);
            Assert.True((await custody.AssignBatchAsync(Batch(w, (w.Ana, 1, 10), (w.Ben, 11, 20)))).IsSuccess);
            var issued = await ctx.AccountableDocuments.SingleAsync(x => x.SerialNumber == 3);
            issued.Consume(null, Guid.NewGuid(), DateTime.UtcNow, "collector");
            await ctx.SaveChangesAsync();
        }

        await using var work = db.CreateContext(w.TenantId);
        var transfer = Custody(work, w.TenantId, w.HeadId);
        // An issued ticket in the range, a range held by two collectors, a missing reason, or the same holder: refused.
        var withIssued = await transfer.TransferAsync(new TransferAccountableFormsRequest(w.BookId, 1, 5, w.Cora.Id, "Ana on leave"));
        Assert.False(withIssued.IsSuccess);
        Assert.Contains("CT000003 is already issued", withIssued.Error);
        Assert.False((await transfer.TransferAsync(new TransferAccountableFormsRequest(w.BookId, 9, 12, w.Cora.Id, "Ana on leave"))).IsSuccess);
        Assert.False((await transfer.TransferAsync(new TransferAccountableFormsRequest(w.BookId, 4, 10, w.Cora.Id, " "))).IsSuccess);
        Assert.False((await transfer.TransferAsync(new TransferAccountableFormsRequest(w.BookId, 4, 10, w.Ana.Id, "same"))).IsSuccess);

        var moved = await transfer.TransferAsync(new TransferAccountableFormsRequest(w.BookId, 4, 10, w.Cora.Id, "Ana on leave"));
        Assert.True(moved.IsSuccess, moved.Error);
        Assert.Equal(7, moved.Value);

        await using var check = db.CreateContext(w.TenantId);
        var documents = await check.AccountableDocuments.AsNoTracking().OrderBy(x => x.SerialNumber).ToListAsync();
        Assert.Equal(AccountableDocumentState.Consumed, documents[2].State);
        Assert.NotEqual(w.Cora.Id, documents[2].AssignedUserId);
        Assert.All(documents.Skip(3).Take(7), x => Assert.Equal((AccountableDocumentState.Assigned, (Guid?)w.Cora.Id), (x.State, x.AssignedUserId)));
        var ids = documents.Skip(3).Take(7).Select(x => x.Id).ToArray();
        var history = await check.AccountableFormAssignments.AsNoTracking().Where(x => ids.Contains(x.AccountableDocumentId)).ToListAsync();
        Assert.Equal(14, history.Count);
        Assert.All(history.Where(x => x.AssignedUserId == w.Ana.Id), x => Assert.NotNull(x.ReturnedAtUtc));
        Assert.All(history.Where(x => x.AssignedUserId == w.Cora.Id), x =>
        {
            Assert.Null(x.ReturnedAtUtc);
            Assert.Equal(w.Ana.Id, x.TransferredFromUserId);
            Assert.Equal("Ana on leave", x.TransferReason);
            Assert.Equal(w.HeadId.ToString("N"), x.AssignedByActorId);
        });
        Assert.Equal(0, await check.Collections.CountAsync());
    }
}
