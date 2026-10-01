using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Automatic allocation of physical accountable forms on PostgreSQL. The server lays out each collector's units from the
/// book's unassigned, in-office units only — never a unit another collector holds, nor an issued, consumed, spoiled or
/// review unit — so every serial keeps exactly one custodian. A preview writes nothing; a commit assigns only the plan the
/// office confirmed, all or nothing; units a collector holds move only by transfer.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AccountableFormAutoAllocationTests(PostgresFixture db)
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
        public string? MunicipalityCode => "pg-auto";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed record World(Guid TenantId, Guid HeadId, Guid BookId, CollectorUser Bobby, CollectorUser Cian, CollectorUser Dina);

    private async Task<World> SeedAsync(int units = 30)
    {
        var tenant = Municipality.Create($"aut-{Guid.NewGuid():N}"[..12], "Auto", "Province",
            MunicipalityStatus.Active, tenantCode: $"auto-{Guid.NewGuid():N}"[..28]);
        CollectorUser Collector(string name, string id) =>
            CollectorUser.Create(name, id, $"pg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        var (bobby, cian, dina) = (Collector("Bobby Mercado", "C-01"), Collector("Cian Uy", "C-02"), Collector("Dina Ong", "C-03"));
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, bobby, cian, dina);
            await setup.SaveChangesAsync();
        }
        var headId = Guid.NewGuid();
        await using var ctx = db.CreateContext(tenant.Id);
        var book = (await Custody(ctx, tenant.Id, headId).ReceiveAsync(new ReceiveAccountableFormBookRequest(
            RevenueInstrumentType.CashTicket, "CT book", "CT", 1, units, 6))).Value!;
        return new World(tenant.Id, headId, book.BookId, bobby, cian, dina);
    }

    private static AccountableFormCustodyWorkflow Custody(AppDbContext ctx, Guid tenantId, Guid headId) =>
        new(ctx, new Head(headId, tenantId), new FixedTenant(tenantId));

    private static AutoAllocateFormsRequest Request(World w, bool commit, IReadOnlyList<AutoAllocatePlanLine>? expected,
        params (CollectorUser Collector, int? Quantity)[] shares) =>
        new(w.BookId, RevenueInstrumentType.CashTicket,
            shares.Select(x => new AutoAllocateShare(x.Collector.Id, x.Quantity)).ToList(), commit, expected);

    private async Task<AutoAllocatePlanDto> PreviewAndCommitAsync(World w, params (CollectorUser Collector, int? Quantity)[] shares)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        var preview = await Custody(ctx, w.TenantId, w.HeadId).AutoAllocateAsync(Request(w, false, null, shares));
        Assert.True(preview.IsSuccess, preview.Error);
        await using var commitCtx = db.CreateContext(w.TenantId);
        var committed = await Custody(commitCtx, w.TenantId, w.HeadId).AutoAllocateAsync(Request(w, true, preview.Value!.Lines, shares));
        Assert.True(committed.IsSuccess, committed.Error);
        return committed.Value!;
    }

    private async Task AssertOneCustodianPerUnitAsync(World w)
    {
        await using var check = db.CreateContext(w.TenantId);
        var open = await check.AccountableFormAssignments.Where(x => x.ReturnedAtUtc == null).ToListAsync();
        Assert.Equal(open.Count, open.Select(x => x.AccountableDocumentId).Distinct().Count());
        Assert.Equal(0, await check.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task APreviewWritesNothing_AndAnEvenCommitSplitsEveryOfficeUnitIntoContiguousRanges()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(31);

        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var preview = await Custody(ctx, w.TenantId, w.HeadId).AutoAllocateAsync(Request(w, false, null, (w.Bobby, null), (w.Cian, null)));
            Assert.True(preview.IsSuccess, preview.Error);
            Assert.False(preview.Value!.Committed);
            Assert.Equal((31, 0), (preview.Value.InOfficeBefore, preview.Value.InOfficeAfter));
            Assert.Equal(0, await ctx.AccountableFormAssignments.CountAsync());
        }

        var plan = await PreviewAndCommitAsync(w, (w.Bobby, null), (w.Cian, null));

        Assert.True(plan.Committed);
        var bobby = plan.Lines.Single(x => x.CollectorId == w.Bobby.Id);
        var cian = plan.Lines.Single(x => x.CollectorId == w.Cian.Id);
        Assert.Equal((16, 15), (bobby.Quantity, cian.Quantity));   // the remainder goes to the first collector
        Assert.Equal(("CT000001", "CT000016"), (bobby.Ranges.Single().FirstDocumentNumber, bobby.Ranges.Single().LastDocumentNumber));
        Assert.Equal(("CT000017", "CT000031"), (cian.Ranges.Single().FirstDocumentNumber, cian.Ranges.Single().LastDocumentNumber));
        await using var check = db.CreateContext(w.TenantId);
        Assert.Equal(16, await check.AccountableDocuments.CountAsync(x => x.AssignedUserId == w.Bobby.Id && x.State == AccountableDocumentState.Assigned));
        Assert.Equal(15, await check.AccountableDocuments.CountAsync(x => x.AssignedUserId == w.Cian.Id && x.State == AccountableDocumentState.Assigned));
        await AssertOneCustodianPerUnitAsync(w);
    }

    [SkippableFact]
    public async Task CustomQuantities_SkipEveryUnitThatIsNotInOffice_AndSplitAShareAroundTheGaps()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(30);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var custody = Custody(ctx, w.TenantId, w.HeadId);
            // Dina holds 4–6; 8 is issued; 10 is spoiled; 12 awaits review.
            Assert.True((await custody.AssignRangeAsync(new AssignAccountableFormRangeRequest(w.BookId, w.Dina.Id, 4, 6))).IsSuccess);
            var documents = await ctx.AccountableDocuments.Where(x => x.SerialNumber == 8 || x.SerialNumber == 10 || x.SerialNumber == 12).ToListAsync();
            documents.Single(x => x.SerialNumber == 8).Consume(null, Guid.NewGuid(), DateTime.UtcNow, "collector");
            documents.Single(x => x.SerialNumber == 10).Void("office");
            documents.Single(x => x.SerialNumber == 12).MarkPhysicalIssueReconciliationRequired(Guid.NewGuid(), DateTime.UtcNow, "collector");
            await ctx.SaveChangesAsync();
        }

        var plan = await PreviewAndCommitAsync(w, (w.Bobby, 6), (w.Cian, 4));

        var bobby = plan.Lines.Single(x => x.CollectorId == w.Bobby.Id);
        Assert.Equal(6, bobby.Quantity);
        Assert.Equal(new[] { (1L, 3L), (7L, 7L), (9L, 9L), (11L, 11L) }, bobby.Ranges.Select(r => (r.FirstSerialNumber, r.LastSerialNumber)));
        var cian = plan.Lines.Single(x => x.CollectorId == w.Cian.Id);
        Assert.Equal(new[] { (13L, 16L) }, cian.Ranges.Select(r => (r.FirstSerialNumber, r.LastSerialNumber)));

        await using var check = db.CreateContext(w.TenantId);
        var docs = await check.AccountableDocuments.AsNoTracking().OrderBy(x => x.SerialNumber).ToListAsync();
        Assert.All(docs.Where(x => x.SerialNumber is >= 4 and <= 6), x => Assert.Equal(w.Dina.Id, x.AssignedUserId));
        Assert.Equal(AccountableDocumentState.Consumed, docs.Single(x => x.SerialNumber == 8).State);
        Assert.Equal(AccountableDocumentState.Voided, docs.Single(x => x.SerialNumber == 10).State);
        Assert.Equal(AccountableDocumentState.ReconciliationRequired, docs.Single(x => x.SerialNumber == 12).State);
        Assert.Equal(14, docs.Count(x => x.State == AccountableDocumentState.InOffice));
        await AssertOneCustodianPerUnitAsync(w);
    }

    [SkippableFact]
    public async Task WhenEveryUnusedTicketIsHeldByACollector_ThereIsNothingToAllocate_AndTransferIsTheOnlyWayToMoveThem()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(20);
        await using var ctx = db.CreateContext(w.TenantId);
        var custody = Custody(ctx, w.TenantId, w.HeadId);
        Assert.True((await custody.AssignRangeAsync(new AssignAccountableFormRangeRequest(w.BookId, w.Bobby.Id, 1, 20))).IsSuccess);

        var none = await custody.AutoAllocateAsync(Request(w, false, null, (w.Bobby, null), (w.Cian, null)));
        Assert.Equal(ResultStatus.Conflict, none.Status);
        Assert.Contains("No unassigned Cash Tickets are in office", none.Error);
        Assert.Contains("transfer", none.Error);

        // Redistribution is an accountable custody transfer: Bobby's 11–20 move to Cian with a reason; nobody shares a unit.
        Assert.True((await custody.TransferAsync(new TransferAccountableFormsRequest(w.BookId, 11, 20, w.Cian.Id, "Rebalance"))).IsSuccess);
        await AssertOneCustodianPerUnitAsync(w);
        Assert.Equal(10, await ctx.AccountableDocuments.CountAsync(x => x.AssignedUserId == w.Cian.Id));
    }

    [SkippableFact]
    public async Task MoreThanIsInOffice_OrFewerThanOneEach_IsRefused_WithNothingAssigned()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(2);
        await using var ctx = db.CreateContext(w.TenantId);
        var custody = Custody(ctx, w.TenantId, w.HeadId);

        var tooMany = await custody.AutoAllocateAsync(Request(w, true, null, (w.Bobby, 2), (w.Cian, 1)));
        Assert.Equal(ResultStatus.Conflict, tooMany.Status);
        Assert.Contains("only 2 are unassigned in office", tooMany.Error);
        var tooFew = await custody.AutoAllocateAsync(Request(w, false, null, (w.Bobby, null), (w.Cian, null), (w.Dina, null)));
        Assert.Equal(ResultStatus.Conflict, tooFew.Status);
        var mixed = await custody.AutoAllocateAsync(Request(w, false, null, (w.Bobby, null), (w.Cian, 1)));
        Assert.Equal(ResultStatus.Invalid, mixed.Status);
        var repeated = await custody.AutoAllocateAsync(Request(w, false, null, (w.Bobby, 1), (w.Bobby, 1)));
        Assert.Equal(ResultStatus.Invalid, repeated.Status);
        Assert.Equal(0, await ctx.AccountableFormAssignments.CountAsync());
    }

    [SkippableFact]
    public async Task ACommitWhoseCustodyChangedSinceThePreview_AssignsNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(20);
        IReadOnlyList<AutoAllocatePlanLine> previewed;
        await using (var ctx = db.CreateContext(w.TenantId))
            previewed = (await Custody(ctx, w.TenantId, w.HeadId).AutoAllocateAsync(Request(w, false, null, (w.Bobby, 5), (w.Cian, 5)))).Value!.Lines;
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await Custody(ctx, w.TenantId, w.HeadId).AssignRangeAsync(new AssignAccountableFormRangeRequest(w.BookId, w.Dina.Id, 3, 3))).IsSuccess);

        await using var commit = db.CreateContext(w.TenantId);
        var result = await Custody(commit, w.TenantId, w.HeadId).AutoAllocateAsync(Request(w, true, previewed, (w.Bobby, 5), (w.Cian, 5)));

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("changed since the preview", result.Error);
        Assert.Equal(1, await commit.AccountableFormAssignments.CountAsync());
        Assert.False((await Custody(commit, w.TenantId, w.HeadId).AutoAllocateAsync(Request(w, true, null, (w.Bobby, 1)))).IsSuccess);
    }

    [SkippableFact]
    public async Task TwoConcurrentCommitsOfTheSamePlan_OnlyOneAssigns_AndEveryUnitHasOneCustodian()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(20);
        IReadOnlyList<AutoAllocatePlanLine> previewed;
        await using (var ctx = db.CreateContext(w.TenantId))
            previewed = (await Custody(ctx, w.TenantId, w.HeadId).AutoAllocateAsync(Request(w, false, null, (w.Bobby, 10), (w.Cian, 10)))).Value!.Lines;

        await using var first = db.CreateContext(w.TenantId);
        await using var second = db.CreateContext(w.TenantId);
        var results = await Task.WhenAll(
            Custody(first, w.TenantId, w.HeadId).AutoAllocateAsync(Request(w, true, previewed, (w.Bobby, 10), (w.Cian, 10))),
            Custody(second, w.TenantId, w.HeadId).AutoAllocateAsync(Request(w, true, previewed, (w.Bobby, 10), (w.Cian, 10))));

        Assert.Equal(1, results.Count(x => x.IsSuccess));
        await using var check = db.CreateContext(w.TenantId);
        Assert.Equal(20, await check.AccountableFormAssignments.CountAsync(x => x.ReturnedAtUtc == null));
        await AssertOneCustodianPerUnitAsync(w);
    }
}
