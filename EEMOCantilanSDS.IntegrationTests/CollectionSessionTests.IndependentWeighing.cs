using EEMOCantilanSDS.Api.Controllers;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class CollectionSessionTests
{
    [SkippableFact]
    public async Task Independent_fish_only_rate_is_respected_by_mobile_quote_post_replay_and_frozen_head_report()
    {
        var day = PhilippineTime.Today;
        var world = await SeedAsync(linked: false);
        await using var context = database.CreateContext(world.TenantId);
        await EnableOffice(context, world);
        var register = Office(context, world, "SuperAdmin");
        var fish = (await register.RegisterAsync(new RegisterFishMeatVendorRequest(Guid.NewGuid(),
            day.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Fish Test Vendor"))).Value!;
        var meat = (await register.RegisterAsync(new RegisterFishMeatVendorRequest(Guid.NewGuid(),
            day.Year, FishMeatVendorType.Meat, VendorRegistrationKind.New, "Meat Test Vendor"))).Value!;
        var collector = Office(context, world);
        var firstRequest = new SourceNativeCollectionRequest(Guid.NewGuid(), day, 6m,
            new(CollectorOperationCodes.WeightAndMeasure, fish.Id, Kilograms: 3m));
        var legacyResult = await collector.QuoteAsync(firstRequest);
        Assert.True(legacyResult.IsSuccess, legacyResult.Error);
        var legacyQuote = legacyResult.Value!;
        Assert.Equal(6m, legacyQuote.Amount);
        var firstPosted = await collector.PostAsync(firstRequest);
        Assert.True(firstPosted.IsSuccess, firstPosted.Error);

        var staleRequest = firstRequest with { ClientOperationId = Guid.NewGuid() };
        var staleResult = await collector.QuoteAsync(staleRequest);
        Assert.True(staleResult.IsSuccess, staleResult.Error);
        var staleQuote = staleResult.Value!;
        context.WeighingRateVersions.AddRange(
            WeighingRateVersion.Create(world.TenantId, FishMeatVendorType.Fish, true, 4m, day, "Head"),
            WeighingRateVersion.Create(world.TenantId, FishMeatVendorType.Meat, false, null, day, "Head"));
        await context.SaveChangesAsync();
        Assert.Equal("QuoteStale",
            (await collector.PostAsync(staleRequest with { ExpectedSourceVersion = staleQuote.SourceVersion })).Error);

        var disabled = new SourceNativeCollectionRequest(Guid.NewGuid(), day, 66m,
            new(CollectorOperationCodes.WeightAndMeasure, meat.Id, Kilograms: 3m));
        Assert.Equal("RateNotEffective", (await collector.QuoteAsync(disabled)).Error);
        Assert.Empty(await context.Collections.Where(x => x.TotalAmount == 66m).ToListAsync());

        var next = new SourceNativeCollectionRequest(Guid.NewGuid(), day, 12m,
            new(CollectorOperationCodes.WeightAndMeasure, fish.Id, Kilograms: 3m));
        var updatedResult = await collector.QuoteAsync(next);
        Assert.True(updatedResult.IsSuccess, updatedResult.Error);
        var newQuote = updatedResult.Value!;
        Assert.Equal(12m, newQuote.Amount);
        Assert.Equal(4m, newQuote.Rate);
        var secondPosted = await collector.PostAsync(next);
        Assert.True(secondPosted.IsSuccess, secondPosted.Error);
        var replay = await collector.PostAsync(next);
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.Equal(secondPosted.Value!.CollectionId, replay.Value!.CollectionId);
        Assert.Equal(2, await context.Collections.CountAsync());

        var head = new WeighingConfigurationController(context,
            new Caller(Guid.NewGuid(), world.TenantId, "SuperAdmin"),
            new Tenant(world.TenantId), new Clock(day), new FeeRateResolver(context));
        var reportResponse = await head.SourceNativeReport(day.Year, day.Month, default);
        var report = Assert.IsType<WeighingCollectionReportDto>(
            Assert.IsType<OkObjectResult>(reportResponse.Result).Value);
        Assert.Equal(18m, report.Total);
        Assert.Equal(18m, report.FishTotal);
        Assert.Equal(0m, report.MeatTotal);
        Assert.Equal(2, report.Rows.Count);
        Assert.Equal(new[] { 2m, 4m }, report.Rows.Select(x => x.RatePerKilo).Order().ToArray());
        Assert.Equal(new[] { 6m, 12m }, report.Rows.Select(x => x.NetAmount).Order().ToArray());

        var previousMonth = day.AddMonths(-1);
        var previous = Assert.IsType<WeighingCollectionReportDto>(Assert.IsType<OkObjectResult>(
            (await head.SourceNativeReport(previousMonth.Year, previousMonth.Month, default)).Result).Value);
        Assert.Empty(previous.Rows);
        var foreignTenant = Guid.NewGuid();
        await using (var foreignContext = database.CreateContext(foreignTenant))
        {
            var foreignHead = new WeighingConfigurationController(foreignContext,
                new Caller(Guid.NewGuid(), foreignTenant, "SuperAdmin"), new Tenant(foreignTenant),
                new Clock(day), new FeeRateResolver(foreignContext));
            var foreignReport = Assert.IsType<WeighingCollectionReportDto>(Assert.IsType<OkObjectResult>(
                (await foreignHead.SourceNativeReport(day.Year, day.Month, default)).Result).Value);
            Assert.Empty(foreignReport.Rows);
        }

        var secondLine = await context.CollectionLines.SingleAsync(x => x.CollectionId == secondPosted.Value.CollectionId);
        var snapshot = secondLine.CalculationSnapshot;
        context.CollectionCorrections.Add(CollectionCorrection.Record(world.TenantId, secondPosted.Value.CollectionId,
            null, null, null, CollectionCorrectionType.Reversal, day, DateTime.UtcNow, -12m, "Test reversal",
            Guid.NewGuid().ToString(), "Head", [new CollectionCorrectionLineDraft(secondLine.Id, -12m)]));
        await context.SaveChangesAsync();
        var corrected = Assert.IsType<WeighingCollectionReportDto>(Assert.IsType<OkObjectResult>(
            (await head.SourceNativeReport(day.Year, day.Month, default)).Result).Value);
        Assert.Equal(6m, corrected.Total);
        Assert.Contains(corrected.Rows, x => x.Status == "Reversed" && x.NetAmount == 0m && x.RatePerKilo == 4m);
        Assert.Equal(snapshot, (await context.CollectionLines.SingleAsync(x => x.Id == secondLine.Id)).CalculationSnapshot);
        Assert.Equal(18m, await context.Collections.SumAsync(x => x.TotalAmount));
    }
}
