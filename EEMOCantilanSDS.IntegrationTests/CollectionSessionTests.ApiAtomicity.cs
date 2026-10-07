using System.Net;
using System.Net.Http.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.IntegrationTests;
public sealed partial class CollectionSessionTests
{
    private static async Task<T> ApiPost<T>(HttpClient client, string route, object request)
    {
        var response = await client.PostAsJsonAsync(route, request, CollectorApiHost.Json);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{route}: {(int)response.StatusCode} {body}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(body, CollectorApiHost.Json)!;
    }
    [SkippableTheory]
    [InlineData(CollectorOperationCodes.Terminal)]
    [InlineData(CollectorOperationCodes.FishMeatVendorFee)]
    [InlineData(CollectorOperationCodes.WeightAndMeasure)]
    public async Task Api_runtime_edit_is_atomic_and_replays_the_canonical_replacement(string operation)
    {
        var date = PhilippineTime.Today; var w = await SeedAsync(linked: false); Guid? vendor = null;
        await using (var seed = database.CreateContext(w.TenantId))
        {
            await EnableOffice(seed, w);
            if (operation == CollectorOperationCodes.WeightAndMeasure) vendor = (await Office(seed, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(),
                date.Year, FishMeatVendorType.Meat, VendorRegistrationKind.New, "Registered source"))).Value!.Id;
        }
        await using var host = await CollectorApiHost.StartAsync(database, w.CollectorId, w.TenantId);
        var charge = operation == CollectorOperationCodes.Terminal ? new SourceNativeChargeIntent(operation, Section: TerminalSection.Tricycad)
            : new SourceNativeChargeIntent(operation, vendor, Kilograms: vendor.HasValue ? 3m : null);
        var request = new SourceNativeCollectionRequest(Guid.NewGuid(), date, 100m, charge, "Walk-up frozen payer");
        var quote = await ApiPost<SourceNativeChargeQuote>(host.Client, "/api/office-sources/quote", request);
        var original = await ApiPost<EEMOCantilanSDS.Application.Dtos.Revenue.GovernedServiceOutcomeDto>(host.Client, "/api/office-sources/record", request with { AmountReceived = quote.Amount });
        var editId = Guid.NewGuid(); var replacementCharge = vendor.HasValue ? charge with { Kilograms = 2m } : charge;
        var replacementAmount = vendor.HasValue ? decimal.Round(2m * quote.Rate!.Value, 2) : 75m;
        var intent = new EditMobileCollectionIntent(editId, original.CollectionId, new(MobileCorrectionReason.WrongAmount),
            new(editId, date, null, [new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, replacementAmount, Native: replacementCharge)],
                SourceIdentity: vendor is { } id ? new(SourceIdentityKind.FishMeatVendorRegistration, id) : null, PayerSnapshot: "Corrected snapshot"));
        var reviewed = await ApiPost<CollectionSessionQuote>(host.Client, "/api/mobile/collections/edit/quote", intent);
        Assert.True(reviewed.CanRecord);
        var corrected = await ApiPost<MobileCollectionCorrectionResult>(host.Client, "/api/mobile/collections/edit", new RecordMobileCollectionEditRequest(intent, reviewed.QuoteFingerprint!));
        Assert.NotEqual(original.CollectionId, corrected.Replacement!.CollectionId); Assert.NotEqual(original.ReferenceCode, corrected.Replacement.ReferenceCode);
        await using var verify = database.CreateContext(w.TenantId);
        Assert.Equal(quote.Amount, (await verify.Collections.SingleAsync(x => x.Id == original.CollectionId)).TotalAmount);
        Assert.Equal(replacementAmount, (await verify.Collections.SingleAsync(x => x.Id == corrected.Replacement.CollectionId)).TotalAmount);
        var reversal = await verify.CollectionCorrections.SingleAsync(); Assert.Equal(-quote.Amount, reversal.FinancialEffectAmount);
        Assert.Equal(corrected.Replacement.CollectionId, reversal.ReplacementCollectionId);
        Assert.Equal(2, await verify.Collections.CountAsync());
        var replay = await ApiPost<MobileCollectionCorrectionResult>(host.Client, "/api/mobile/collections/edit", new RecordMobileCollectionEditRequest(intent, reviewed.QuoteFingerprint!));
        Assert.True(replay.ExistingOutcome); Assert.Equal(corrected.Replacement.CollectionId, replay.Replacement!.CollectionId);
        Assert.Equal(corrected.Replacement.ReferenceCode, replay.Replacement.ReferenceCode);
        Assert.Equal(corrected.Replacement.Amount, replay.Replacement.Amount);
        Assert.Equal(corrected.Replacement.ClientItemIds, replay.Replacement.ClientItemIds);
        var recent = (await host.Client.GetFromJsonAsync<MobileRecentCollections>("/api/mobile/collections/recent", CollectorApiHost.Json))!;
        Assert.Equal(0m, recent.Rows.Single(x => x.Collection.CollectionId == original.CollectionId).Collection.NetAmount);
        Assert.Equal(replacementAmount, recent.Rows.Single(x => x.Collection.CollectionId == corrected.Replacement.CollectionId).Collection.NetAmount);
        using var scope = host.Services.CreateScope();
        Assert.Same(scope.ServiceProvider.GetRequiredService<AppDbContext>(), scope.ServiceProvider.GetRequiredService<IAppDbContext>());
    }
    [SkippableFact]
    public async Task Api_runtime_unexpected_failure_after_replacement_save_rolls_back_all_money_and_retry_succeeds()
    {
        var date = PhilippineTime.Today; var w = await SeedAsync();
        await using (var seed = database.CreateContext(w.TenantId)) await EnableOffice(seed, w);
        var fault = new CollectorApiHost.Fault();
        await using var host = await CollectorApiHost.StartAsync(database, w.CollectorId, w.TenantId, fault);
        var original = await ApiPost<EEMOCantilanSDS.Application.Dtos.Revenue.GovernedServiceOutcomeDto>(host.Client, "/api/office-sources/record",
            new SourceNativeCollectionRequest(Guid.NewGuid(), date, 100m, new(CollectorOperationCodes.FishMeatVendorFee), "Walk-up"));
        var operation = Guid.NewGuid(); var item = Guid.NewGuid();
        var intent = new EditMobileCollectionIntent(operation, original.CollectionId, new(MobileCorrectionReason.WrongAmount),
            new(operation, date, null, [new(item, CollectionSessionItemKind.SourceNative, 50m, Native: new(CollectorOperationCodes.FishMeatVendorFee))], PayerSnapshot: "Walk-up"));
        var reviewed = await ApiPost<CollectionSessionQuote>(host.Client, "/api/mobile/collections/edit/quote", intent);
        var request = new RecordMobileCollectionEditRequest(intent, reviewed.QuoteFingerprint!);
        fault.FailAfterPostingOperation = CollectionSessionWorkflow.ChildOperationId(w.TenantId, operation, item);
        var failed = await host.Client.PostAsJsonAsync("/api/mobile/collections/edit", request, CollectorApiHost.Json);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        await using (var check = database.CreateContext(w.TenantId))
        {
            Assert.Empty(await check.CollectionCorrections.ToListAsync()); Assert.Single(await check.Collections.ToListAsync());
            Assert.Equal(100m, (await check.Collections.SingleAsync()).TotalAmount); Assert.Single(await check.CollectionLines.ToListAsync());
            Assert.Single(await check.PostingOperations.ToListAsync());
        }
        var recent = (await host.Client.GetFromJsonAsync<MobileRecentCollections>("/api/mobile/collections/recent", CollectorApiHost.Json))!;
        Assert.Equal(100m, recent.Rows.Single().Collection.NetAmount);
        fault.FailAfterPostingOperation = null;
        var retry = await ApiPost<MobileCollectionCorrectionResult>(host.Client, "/api/mobile/collections/edit", request);
        Assert.NotNull(retry.Replacement);
    }
}
