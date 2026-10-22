using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class EcfCollectionWorkflowTests
{
    [SkippableFact]
    public async Task OfficeCash_filters_scope_every_figure_pending_selection_and_register_without_splitting_turnovers()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var c = await ArrangeChannelsAsync();
        await using var ctx = db.CreateContext(c.Seed.TenantId);
        var alice = AdminUser.Create("Office Cashier", "office-a", "a@synthetic.test", new HashedPassword("synthetic-hash"), AdminRole.SuperAdmin, c.Seed.TenantId);
        var bob = AdminUser.Create("Office Cashier", "office-b", "b@synthetic.test", new HashedPassword("synthetic-hash"), AdminRole.Admin, c.Seed.TenantId);
        var unused = AdminUser.Create("No Office receipts", "office-unused", "unused@synthetic.test", new HashedPassword("synthetic-hash"), AdminRole.Admin, c.Seed.TenantId);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.MarketFees, c.Seed.TenantId);
        var orPolicy = RevenueClassificationPolicy.Create(classification.Id, new(2000, 1, 1), "Market Fees", RevenueInstrumentType.OfficialReceipt, c.Seed.TenantId);
        var ctPolicy = RevenueClassificationPolicy.Create(classification.Id, new(2001, 1, 1), "Market Fees", RevenueInstrumentType.CashTicket, c.Seed.TenantId);
        Collection Receipt(AdminUser user, RevenueClassificationPolicy policy, decimal amount, Guid? collector = null) =>
            Collection.Post(PhilippineTime.Today, DateTime.UtcNow, user.Id.ToString("N"), user.FullName!, user.Role.ToString(),
                [new CollectionLineDraft(classification, policy, amount, null, null)], collectorId: collector);
        var first = Receipt(alice, orPolicy, 100m);
        var second = Receipt(alice, orPolicy, 25m);
        var cashTicket = Receipt(alice, ctPolicy, 40m);
        var other = Receipt(bob, orPolicy, 200m);
        ctx.AddRange(alice, bob, unused, classification, orPolicy, ctPolicy, first, second, cashTicket, other,
            Receipt(alice, orPolicy, 300m, c.Collector));
        await ctx.SaveChangesAsync();
        var workflow = Turnover(ctx, c);
        var today = PhilippineTime.Today;
        var filter = new OfficeCashFilterRequest(today, today, alice.Id, RevenueInstrumentType.OfficialReceipt);
        var before = await workflow.GetWorkspaceAsync(filter);
        Assert.True(before.IsSuccess, before.Error);
        Assert.Equal(125m, before.Value!.Position.Collected);
        Assert.Equal(125m, before.Value.Position.Pending);
        Assert.Equal(125m, before.Value.Review.ExpectedAmount);
        Assert.Equal(new[] { alice.Id, bob.Id }.Order(), before.Value.Authorities.Select(x => x.Id).Order());
        Assert.All(before.Value.Authorities, x => Assert.Equal("Office Cashier", x.Name));
        Assert.Equal(new[] { first.Id, second.Id }.Order(), before.Value.Review.Collections.Select(x => x.CollectionId).Order());

        var recorded = await workflow.RecordAsync(Request(Guid.NewGuid(), [first.Id], 90m, remarks: "Synthetic shortfall"));
        Assert.True(recorded.IsSuccess, recorded.Error);
        var after = (await workflow.GetWorkspaceAsync(filter)).Value!;
        Assert.Equal((125m, 100m, 25m, 1), (after.Position.Collected, after.Position.Covered, after.Position.Pending, after.Position.NeedsReviewCount));
        Assert.Equal((90m, 10m), (after.Position.TurnedOverAmount, after.Position.NetDifference));
        Assert.Equal(second.Id, Assert.Single(after.Review.Collections).CollectionId);
        Assert.Equal(recorded.Value!.Row.Id, Assert.Single(after.Register).Id);
        var ct = (await workflow.GetWorkspaceAsync(filter with { Instrument = RevenueInstrumentType.CashTicket })).Value!;
        Assert.Equal((40m, 0m, 40m, 0), (ct.Position.Collected, ct.Position.Covered, ct.Position.Pending, ct.Position.NeedsReviewCount));
        Assert.Equal(cashTicket.Id, Assert.Single(ct.Review.Collections).CollectionId);
        Assert.Empty(ct.Register);
        Assert.Equal(365m, (await workflow.GetWorkspaceAsync(new(today, today))).Value!.Position.Collected);
        Assert.Equal(ResultStatus.Invalid, (await workflow.GetWorkspaceAsync(filter with { AuthorityId = Guid.NewGuid() })).Status);
        Assert.Equal(ResultStatus.Invalid, (await workflow.GetWorkspaceAsync(filter with { Instrument = (RevenueInstrumentType)999 })).Status);
        Assert.Equal(ResultStatus.Forbidden, (await Turnover(ctx, c, "Collector").GetWorkspaceAsync(filter)).Status);
        var foreignId = Guid.NewGuid();
        await using (var foreign = db.CreateContext(foreignId))
        {
            var foreignWorkflow = Turnover(foreign, c, tenant: foreignId);
            Assert.Empty((await foreignWorkflow.GetWorkspaceAsync(new(today, today))).Value!.Review.Collections);
            Assert.Equal(ResultStatus.Invalid, (await foreignWorkflow.GetWorkspaceAsync(filter)).Status);
        }

        var mixed = await workflow.RecordAsync(Request(Guid.NewGuid(), [second.Id, other.Id], 225m));
        Assert.True(mixed.IsSuccess, mixed.Error);
        var split = await workflow.GetWorkspaceAsync(filter);
        Assert.Equal(ResultStatus.Conflict, split.Status);
        Assert.Contains("splits a turnover", split.Error);
        var allOr = await workflow.GetWorkspaceAsync(filter with { AuthorityId = null });
        Assert.True(allOr.IsSuccess, allOr.Error);
        Assert.Equal((325m, 325m, 0m, 1), (allOr.Value!.Position.Collected, allOr.Value.Position.Covered, allOr.Value.Position.Pending, allOr.Value.Position.NeedsReviewCount));
        Assert.Empty(await ctx.CollectionRemittances.ToListAsync());
        Assert.Equal(5, await ctx.Collections.CountAsync());
        ctx.Add(Collection.Post(today, DateTime.UtcNow, alice.Id.ToString("N"), alice.FullName!, "SuperAdmin",
            [new CollectionLineDraft(classification, orPolicy, 5m, null, null), new CollectionLineDraft(classification, ctPolicy, 7m, null, null)]));
        await ctx.SaveChangesAsync();
        var mixedInstrument = await workflow.GetWorkspaceAsync(filter);
        Assert.Equal(ResultStatus.Conflict, mixedInstrument.Status);
        Assert.Contains("mixed or unresolved instruments", mixedInstrument.Error);
        Assert.Equal(377m, (await workflow.GetWorkspaceAsync(new(today, today))).Value!.Position.Collected);
    }
}
