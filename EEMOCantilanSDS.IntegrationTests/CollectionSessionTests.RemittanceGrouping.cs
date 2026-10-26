using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Remittance reads correlate the collections of one Mobile checkout for display only. Selection, coverage, totals and the
/// individual SRCs stay per Collection; a checkout that is not wholly in the scope is never shown as a group.
/// </summary>
public sealed partial class CollectionSessionTests
{
    [SkippableFact]
    public async Task Remittance_scope_and_review_group_one_checkout_while_every_collection_and_total_stays_individual()
    {
        var (w, flow, db) = await SeedMixedAsync();
        await using var _ = db;
        var tenant = new Tenant(w.TenantId);
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        var result = await Record(flow, MixedIntent(w, null));
        var remit = new RemittanceWorkflow(db, head, tenant);

        var scope = (await remit.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!;
        Assert.Equal(6, scope.Collections.Count);
        Assert.Equal(result.GrandTotal, scope.ExpectedAmount);
        var session = Assert.Single(scope.Sessions!);
        Assert.Equal(result.Collections.Select(c => c.CollectionId).OrderBy(x => x), session.CollectionIds.OrderBy(x => x));

        var review = (await remit.GetReviewAsync(new(Today, Today))).Value!;
        Assert.Equal((result.GrandTotal, 6), (review.Total, review.CollectionCount));
        Assert.Single(Assert.Single(review.Collectors).Sessions!);

        // An instrument filter hides part of the checkout, so it is never shown as a (smaller) group.
        var ordersOnly = (await remit.GetScopeAsync(w.CollectorId, Today, Today, EEMOCantilanSDS.Domain.Enums.RevenueInstrumentType.OfficialReceipt)).Value!;
        Assert.True(ordersOnly.Collections.Count < 6);
        Assert.Empty(ordersOnly.Sessions ?? []);
    }

    [SkippableFact]
    public async Task A_partly_remitted_checkout_is_no_longer_grouped_and_cannot_be_remitted_twice()
    {
        var (w, flow, db) = await SeedMixedAsync();
        await using var _ = db;
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        var remit = new RemittanceWorkflow(db, head, new Tenant(w.TenantId));
        await Record(flow, MixedIntent(w, null));
        var scope = (await remit.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!;
        var first = scope.Collections.Take(2).ToArray();

        var partial = await remit.RecordAsync(new RecordRemittanceRequest(Guid.NewGuid(), w.CollectorId, Today, Today, Today, null,
            first.Select(x => x.CollectionId).ToArray(), first.Sum(x => x.NetAmount), "part", null));
        Assert.True(partial.IsSuccess, partial.Error);
        Assert.Empty(partial.Value!.Sessions ?? []);                                     // only 2 of the checkout's 6 are in this remittance

        var left = (await remit.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!;
        Assert.Equal(4, left.Collections.Count);
        Assert.Empty(left.Sessions ?? []);                                              // the rest stay individual, never a partial subtotal

        var again = await remit.RecordAsync(new RecordRemittanceRequest(Guid.NewGuid(), w.CollectorId, Today, Today, Today, null,
            first.Select(x => x.CollectionId).ToArray(), first.Sum(x => x.NetAmount), "again", null));
        Assert.False(again.IsSuccess);                                                  // no double remittance
    }

    [SkippableFact]
    public async Task A_remittance_that_covers_the_whole_checkout_reports_it_as_one_group_with_every_SRC()
    {
        var (w, flow, db) = await SeedMixedAsync();
        await using var _ = db;
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        var remit = new RemittanceWorkflow(db, head, new Tenant(w.TenantId));
        var result = await Record(flow, MixedIntent(w, null));
        var scope = (await remit.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!;

        var all = await remit.RecordAsync(new RecordRemittanceRequest(Guid.NewGuid(), w.CollectorId, Today, Today, Today, null,
            scope.Sessions!.Single().CollectionIds.ToArray(), scope.ExpectedAmount, null, null));
        Assert.True(all.IsSuccess, all.Error);
        var detail = all.Value!;
        Assert.Equal(6, detail.Collections.Count);
        Assert.Equal(result.GrandTotal, detail.Collections.Sum(x => x.NetAmount));
        Assert.Equal(result.Collections.Select(c => c.ReferenceCode).OrderBy(x => x), detail.Collections.Select(c => c.ReferenceCode).OrderBy(x => x));
        Assert.Single(detail.Sessions!);
        Assert.Equal(6, await db.CollectionRemittanceCoverages.CountAsync());
    }
}
