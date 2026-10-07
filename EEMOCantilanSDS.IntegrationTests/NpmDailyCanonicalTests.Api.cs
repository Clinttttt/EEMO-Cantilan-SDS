using System.Net.Http.Json;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class NpmDailyCanonicalTests
{
    [SkippableFact]
    public async Task Api_NPM_source_offers_daily_and_whole_then_daily_edit_preserves_the_server_charge()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        await db.ResetAsync(); var w = await SeedAsync(true);
        await using var verify = db.CreateContext(w.TenantId);
        var occupancy = await verify.Contracts.SingleAsync(x => x.StallId == w.StallA);
        Assert.Null(occupancy.PayorId); Assert.Empty(await verify.Payors.ToListAsync());
        var identity = new CollectionSourceIdentity(SourceIdentityKind.Occupancy, occupancy.Id);
        var charge = (await Batch(verify, w).SourcesAsync()).Value!.Single(x => x.StallId == w.StallA).EffectiveCharge;
        var fault = new CollectorApiHost.Fault();
        await using var host = await CollectorApiHost.StartAsync(db, w.CollectorId, w.TenantId, fault);
        async Task<T> Post<T>(string route, object request)
        {
            var response = await host.Client.PostAsJsonAsync(route, request, CollectorApiHost.Json);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<T>(CollectorApiHost.Json))!;
        }
        var available = await Post<CollectionSessionDiscovery>("/api/mobile/collection-session/source-eligible", identity);
        Assert.Equal(Today, available.BusinessDate); Assert.Equal(identity, available.SourceIdentity);
        var daily = Assert.Single(available.Operations.Single(x => x.Kind == CollectionSessionItemKind.NpmDaily).Choices!);
        Assert.Equal(w.StallA, daily.Identity.StallId); Assert.Equal(occupancy.Id, daily.Identity.OccupancyId);
        Assert.Equal(CollectionSessionAmountRule.FixedAmount, daily.AmountRule);
        Assert.Equal(charge, daily.ServerAmount);
        Assert.Contains(available.Operations, x => x.Kind == CollectionSessionItemKind.NpmWholePayment);
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.NpmDaily, charge, NpmDaily: new(w.StallA))], SourceIdentity: identity);
        var quote = await Post<CollectionSessionQuote>("/api/mobile/collection-session/quote", intent);
        Assert.True(quote.CanRecord);
        var result = await Post<CollectionSessionResult>("/api/mobile/collection-session/record", new RecordCollectionSessionRequest(intent, quote.QuoteFingerprint!));
        var original = Assert.Single(result.Collections);
        var after = await Post<CollectionSessionDiscovery>("/api/mobile/collection-session/source-eligible", identity);
        Assert.DoesNotContain(after.Operations, x => x.Kind == CollectionSessionItemKind.NpmDaily);
        Assert.Contains(after.Operations, x => x.Kind == CollectionSessionItemKind.NpmWholePayment);
        var remaining = Assert.Single(after.Operations.Single(x => x.Kind == CollectionSessionItemKind.NpmWholePayment).Choices!).ServerAmount;
        var editId = Guid.NewGuid();
        var edit = new EditMobileCollectionIntent(editId, original.CollectionId, new(MobileCorrectionReason.WrongAmount),
            intent with { ClientCollectionSessionId = editId, Items = [intent.Items[0] with { ClientItemId = Guid.NewGuid() }] });
        var reviewed = await Post<CollectionSessionQuote>("/api/mobile/collections/edit/quote", edit);
        Assert.True(reviewed.CanRecord);
        var editRequest = new RecordMobileCollectionEditRequest(edit, reviewed.QuoteFingerprint!);
        fault.FailAfterPostingOperation = CollectionSessionWorkflow.ChildOperationId(w.TenantId, editId, edit.Replacement.Items[0].ClientItemId);
        var failed = await host.Client.PostAsJsonAsync("/api/mobile/collections/edit", editRequest, CollectorApiHost.Json);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, failed.StatusCode);
        verify.ChangeTracker.Clear();
        var unchangedDay = await verify.DailyCollections.SingleAsync();
        Assert.True(unchangedDay.IsPaid); Assert.Equal(original.CollectionId, unchangedDay.CanonicalCollectionId);
        Assert.Empty(await verify.CollectionCorrections.ToListAsync()); Assert.Single(await verify.Collections.ToListAsync());
        var afterFailure = await Post<CollectionSessionDiscovery>("/api/mobile/collection-session/source-eligible", identity);
        Assert.Equal(remaining, Assert.Single(afterFailure.Operations.Single(x => x.Kind == CollectionSessionItemKind.NpmWholePayment).Choices!).ServerAmount);
        fault.FailAfterPostingOperation = null;
        var corrected = await Post<MobileCollectionCorrectionResult>("/api/mobile/collections/edit", editRequest);
        Assert.NotEqual(original.ReferenceCode, corrected.Replacement!.ReferenceCode);
        verify.ChangeTracker.Clear();
        var day = await verify.DailyCollections.SingleAsync();
        Assert.True(day.IsPaid); Assert.Equal(charge, day.DailyFee);
        Assert.Equal(corrected.Replacement.CollectionId, day.CanonicalCollectionId);
        Assert.Equal(2, await verify.Collections.CountAsync()); Assert.Empty(await verify.Payors.ToListAsync());
        var afterEdit = await Post<CollectionSessionDiscovery>("/api/mobile/collection-session/source-eligible", identity);
        Assert.Equal(remaining, Assert.Single(afterEdit.Operations.Single(x => x.Kind == CollectionSessionItemKind.NpmWholePayment).Choices!).ServerAmount);
        var replay = await Post<MobileCollectionCorrectionResult>("/api/mobile/collections/edit", new RecordMobileCollectionEditRequest(edit, reviewed.QuoteFingerprint!));
        Assert.True(replay.ExistingOutcome); Assert.Equal(corrected.Replacement.CollectionId, replay.Replacement!.CollectionId);
        Assert.Equal(corrected.Replacement.ReferenceCode, replay.Replacement.ReferenceCode);
    }
}
