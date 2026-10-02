using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// A multi-collector remittance submission is shown as one History row, but every collector's remittance stays its own
/// record with its own collections, amount, status and idempotency; grouping is a non-financial id and moves no money.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RemittanceGroupingTests(PostgresFixture db)
{
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Caller(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "collector" : "office";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "pg-group";
        public Guid? MunicipalityId => tenantId;
    }

    private static readonly DateOnly Today = PhilippineTime.Today;

    private sealed record World(Municipality Tenant, CollectorUser[] Collectors, Guid HeadId, AccountableDocument[][] Tickets);

    /// <summary>Three collectors, each holding five Cash Tickets; Market Fees is a fixed ₱30 and each posts the given counts.</summary>
    private async Task<World> SeedAsync(params int[] collectionsPerCollector)
    {
        var tenant = Municipality.Create($"grp-{Guid.NewGuid():N}"[..12], "Group Test", "Province", MunicipalityStatus.Active,
            tenantCode: $"group-{Guid.NewGuid():N}"[..28].ToLowerInvariant());
        var names = new[] { ("Bobby Mercado", "C-01"), ("Cian Consigna", "C-02"), ("Dina Dela Cruz", "C-03") };
        var collectors = names.Select(n => CollectorUser.Create(n.Item1, n.Item2, $"pg-{Guid.NewGuid():N}"[..14], null, null,
            new HashedPassword("h"), tenant.Id)).ToArray();
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Add(tenant);
            setup.AddRange(collectors);
            await setup.SaveChangesAsync();
        }
        var head = Guid.NewGuid();
        var effective = Today.AddDays(-30);
        await using (var ctx = db.CreateContext(tenant.Id))
        {
            var classification = RevenueClassification.Create(RevenueClassificationCodes.MarketFees, tenant.Id);
            ctx.Add(classification);
            ctx.Add(RevenueClassificationPolicy.Create(classification.Id, effective, "Market Fees", RevenueInstrumentType.CashTicket, tenant.Id));
            var service = GovernedService.Create(tenant.Id, CollectorOperationCodes.MarketFees, "head");
            ctx.Add(service);
            ctx.Add(GovernedServiceSetting.Create(tenant.Id, service.Id, effective, GovernedServiceBasis.FixedAmount, 30m, null, true, true, "head"));
            foreach (var collector in collectors)
                ctx.Add(CollectorOperationAssignment.Assign(tenant.Id, collector.Id, CollectorOperationCodes.MarketFees, "head"));
            await ctx.SaveChangesAsync();
            var custody = new AccountableFormCustodyWorkflow(ctx, new Caller(head, tenant.Id, "SuperAdmin"), new FixedTenant(tenant.Id));
            var book = (await custody.ReceiveAsync(new ReceiveAccountableFormBookRequest(RevenueInstrumentType.CashTicket, "CT", "CT", 1, 15, 6))).Value!;
            for (var i = 0; i < 3; i++)
                Assert.True((await custody.AssignRangeAsync(new AssignAccountableFormRangeRequest(book.BookId, collectors[i].Id, i * 5 + 1, i * 5 + 5))).IsSuccess);
        }
        AccountableDocument[][] tickets;
        await using (var ctx = db.CreateContext(tenant.Id))
        {
            var all = await ctx.AccountableDocuments.OrderBy(x => x.SerialNumber).ToListAsync();
            tickets = Enumerable.Range(0, 3).Select(i => all.Skip(i * 5).Take(5).ToArray()).ToArray();
        }
        var world = new World(tenant, collectors, head, tickets);
        for (var i = 0; i < collectionsPerCollector.Length; i++)
            for (var n = 0; n < collectionsPerCollector[i]; n++)
            {
                await using var ctx = db.CreateContext(tenant.Id);
                var workflow = new GovernedServiceWorkflow(ctx, new Caller(collectors[i].Id, tenant.Id, "Collector"), new FixedTenant(tenant.Id));
                var doc = tickets[i][n];
                Assert.True((await workflow.PostMobileAsync(new GovernedServicePostRequest(1, Guid.NewGuid(), CollectorOperationCodes.MarketFees,
                    Today, 30m, null, "Walk-up", null, doc.Id, doc.DocumentNumber, DateTime.UtcNow.AddMinutes(-1)))).IsSuccess);
            }
        return world;
    }

    private RemittanceWorkflow Office(AppDbContext ctx, World w, string role = "Admin") =>
        new(ctx, new Caller(w.HeadId, w.Tenant.Id, role), new FixedTenant(w.Tenant.Id));

    private static RecordRemittanceRequest Request(World w, int collector, decimal amount, Guid? operationId = null, IReadOnlyList<Guid>? ids = null) => new(
        operationId ?? Guid.NewGuid(), w.Collectors[collector].Id, Today, Today.AddDays(-1), Today, RevenueInstrumentType.CashTicket, ids, amount, null, null);

    private static async Task<(int Lines, decimal Money, int Collections)> MoneyAsync(AppDbContext ctx) =>
        (await ctx.CollectionLines.CountAsync(), await ctx.CollectionLines.SumAsync(x => x.Amount), await ctx.Collections.CountAsync());

    [SkippableFact]
    public async Task OneSubmissionOfThreeCollectors_IsOneHistoryRow_InSubmissionOrder_WithTheSumOfItsIndependentRecords()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(3, 2, 1);                       // 90 + 60 + 30
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var before = await MoneyAsync(ctx);

        // Submitted deliberately out of alphabetical order: the first named collector is the one submitted first.
        var batch = (await Office(ctx, w).RecordBatchAsync(new RecordRemittanceBatchRequest(
            [Request(w, 1, 60m), Request(w, 0, 80m), Request(w, 2, 30m)]))).Value!;
        Assert.Equal(3, batch.Count);
        Assert.Single(batch.Select(x => x.Row.SubmissionId).Distinct());
        Assert.NotNull(batch[0].Row.SubmissionId);

        var history = (await Office(ctx, w).GetHistoryAsync(Today.AddDays(-1), Today, null, null, null)).Value!;
        var row = Assert.Single(history);                         // not three rows
        Assert.Equal(new[] { "Cian Consigna", "Bobby Mercado", "Dina Dela Cruz" }, row.Members.Select(x => x.CollectorName));
        Assert.Equal(batch[0].Row.SubmissionId, row.SubmissionId);
        Assert.Equal((6, 180m, 170m, 10m), (row.CollectionCount, row.ExpectedAmount, row.RemittedAmount, row.DifferenceAmount));
        Assert.Equal(row.RemittedAmount, row.Members.Sum(x => x.RemittedAmount));
        Assert.Equal("Needs review", row.StatusLabel);

        // Each collector's remittance is still its own record, covering only its own collections.
        var records = await ctx.CollectionRemittances.AsNoTracking().OrderBy(x => x.SubmissionSequence).ToListAsync();
        Assert.Equal(new[] { 1, 2, 3 }, records.Select(x => x.SubmissionSequence!.Value));
        Assert.Equal(new[] { w.Collectors[1].Id, w.Collectors[0].Id, w.Collectors[2].Id }, records.Select(x => x.CollectorId));
        Assert.Equal(new[] { 2, 3, 1 }, records.Select(x => x.CollectionCount));
        Assert.Equal(6, await ctx.CollectionRemittanceCoverages.CountAsync(x => x.IsActive));

        // Grouping created and changed no money.
        Assert.Equal(before, await MoneyAsync(ctx));
    }

    [SkippableFact]
    public async Task ASingleCollectorIsNotAGroup_AndAnOneItemBatchIsNotEither()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(2, 1);
        await using var ctx = db.CreateContext(w.Tenant.Id);

        Assert.True((await Office(ctx, w).RecordAsync(Request(w, 0, 60m))).IsSuccess);
        var batch = (await Office(ctx, w).RecordBatchAsync(new RecordRemittanceBatchRequest([Request(w, 1, 30m)]))).Value!;
        Assert.Null(batch.Single().Row.SubmissionId);

        var history = (await Office(ctx, w).GetHistoryAsync(Today.AddDays(-1), Today, null, null, null)).Value!;
        Assert.Equal(2, history.Count);
        Assert.All(history, r => { Assert.Single(r.Members); Assert.Null(r.SubmissionId); });
        Assert.Contains(history, r => r.Members[0].CollectorName == "Bobby Mercado");
    }

    [SkippableFact]
    public async Task TheSubmissionReport_ExposesEveryCollector_WithTheirOwnCollections_AndTheCombinedTotal_OnlyInsideTheTenant()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(2, 1, 0);
        var other = await SeedAsync(1);
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var batch = (await Office(ctx, w).RecordBatchAsync(new RecordRemittanceBatchRequest(
            [Request(w, 0, 60m), Request(w, 1, 20m)]))).Value!;
        var submissionId = batch[0].Row.SubmissionId!.Value;

        var report = (await Office(ctx, w).GetSubmissionAsync(submissionId)).Value!;
        Assert.Equal(new[] { "Bobby Mercado", "Cian Consigna" }, report.Remittances.Select(x => x.Row.CollectorName));
        Assert.Equal(new[] { 2, 1 }, report.Remittances.Select(x => x.Collections.Count));
        Assert.Equal((3, 90m, 80m, 10m), (report.CollectionCount, report.ExpectedAmount, report.RemittedAmount, report.DifferenceAmount));
        Assert.Equal(report.RemittedAmount, report.Remittances.Sum(x => x.Row.RemittedAmount));
        // Each collection appears under exactly one collector.
        var all = report.Remittances.SelectMany(x => x.Collections.Select(c => c.CollectionId)).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());

        await using var otherCtx = db.CreateContext(other.Tenant.Id);
        Assert.Equal(ResultStatus.NotFound, (await Office(otherCtx, other).GetSubmissionAsync(submissionId)).Status);
        Assert.Equal(ResultStatus.NotFound, (await Office(ctx, w).GetSubmissionAsync(Guid.NewGuid())).Status);
    }

    [SkippableFact]
    public async Task VoidingOneCollectorsRemittance_IsTruthfulInTheGroup_AndFreesOnlyThatCollectorsCollections()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(2, 2);
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var batch = (await Office(ctx, w).RecordBatchAsync(new RecordRemittanceBatchRequest(
            [Request(w, 0, 60m), Request(w, 1, 60m)]))).Value!;
        var before = await MoneyAsync(ctx);

        Assert.True((await Office(ctx, w).VoidAsync(batch[1].Row.Id, new VoidRemittanceRequest("Counted short"))).IsSuccess);

        var row = Assert.Single((await Office(ctx, w).GetHistoryAsync(Today.AddDays(-1), Today, null, null, null)).Value!);
        Assert.Equal("Partly voided", row.StatusLabel);
        Assert.Equal(new[] { RemittanceStatus.Recorded, RemittanceStatus.Voided }, row.Members.Select(x => x.Status));
        Assert.Equal(120m, row.RemittedAmount);                    // history keeps both records, voided included

        // Filtering by status is applied to the independent records first, so the row shows exactly what matched.
        var recordedOnly = Assert.Single((await Office(ctx, w).GetHistoryAsync(Today.AddDays(-1), Today, null, null, RemittanceStatus.Recorded)).Value!);
        Assert.Equal(new[] { "Bobby Mercado" }, recordedOnly.Members.Select(x => x.CollectorName));
        Assert.Equal(60m, recordedOnly.RemittedAmount);
        Assert.Equal("Recorded", recordedOnly.StatusLabel);

        // Only the voided collector's collections are free again; the other's stay covered.
        Assert.Equal(2, await ctx.CollectionRemittanceCoverages.CountAsync(x => x.IsActive));
        Assert.Equal(before, await MoneyAsync(ctx));
        var again = await Office(ctx, w).RecordAsync(Request(w, 1, 60m));
        Assert.True(again.IsSuccess, again.Error);
        Assert.Null(again.Value!.Row.SubmissionId);
    }

    [SkippableFact]
    public async Task ARetriedBatchReturnsTheSameRecords_NeverAnotherGroup_AndAnotherCollectorsCollectionsAreRefusedWithNothingSaved()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(1, 1);
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var ops = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var request = new RecordRemittanceBatchRequest([Request(w, 0, 30m, ops[0]), Request(w, 1, 30m, ops[1])]);

        var first = (await Office(ctx, w).RecordBatchAsync(request)).Value!;
        await using (var retryCtx = db.CreateContext(w.Tenant.Id))
        {
            var retry = (await Office(retryCtx, w).RecordBatchAsync(request)).Value!;
            Assert.Equal(first.Select(x => x.Row.Id), retry.Select(x => x.Row.Id));
            Assert.Equal(first[0].Row.SubmissionId, retry[0].Row.SubmissionId);
        }
        Assert.Equal(2, await ctx.CollectionRemittances.CountAsync());
        Assert.Single((await Office(ctx, w).GetHistoryAsync(Today.AddDays(-1), Today, null, null, null)).Value!);

        // Collector 2's remittance cannot cover collector 1's collection; the whole batch is refused.
        var w2 = await SeedAsync(1, 1);
        await using var ctx2 = db.CreateContext(w2.Tenant.Id);
        var scope = (await Office(ctx2, w2).GetScopeAsync(w2.Collectors[0].Id, Today.AddDays(-1), Today, RevenueInstrumentType.CashTicket)).Value!;
        var foreign = scope.Collections.Select(x => x.CollectionId).ToList();
        var refused = await Office(ctx2, w2).RecordBatchAsync(new RecordRemittanceBatchRequest(
            [Request(w2, 0, 30m), Request(w2, 1, 30m, ids: foreign)]));
        Assert.False(refused.IsSuccess);
        Assert.Equal(0, await ctx2.CollectionRemittances.CountAsync());
        Assert.Equal(0, await ctx2.CollectionRemittanceCoverages.CountAsync());
    }

    [SkippableFact]
    public async Task TheDatabaseRefusesTheSameCollectorTwiceInOneSubmission()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(2);
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var submission = Guid.NewGuid();
        CollectionRemittance Make(int sequence) => CollectionRemittance.Record(w.Tenant.Id, w.Collectors[0].Id, Today, Today, Today,
            RevenueInstrumentType.CashTicket, 30m, 30m, 1, null, null, Guid.NewGuid(), "fp", "office", "actor", DateTime.UtcNow, submission, sequence);
        ctx.CollectionRemittances.AddRange(Make(1), Make(2));
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
