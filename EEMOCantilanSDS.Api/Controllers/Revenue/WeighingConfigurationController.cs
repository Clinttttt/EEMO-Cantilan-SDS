using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Api.Controllers;

[ApiController]
[Route("api/weight-and-measure")]
[Authorize(Roles = "SuperAdmin,Admin")]
public sealed class WeighingConfigurationController(
    IAppDbContext db, ICurrentUserService user, ICurrentMunicipalityAccessor tenant,
    IClock clock, IFeeRateResolver rates) : ControllerBase
{
    private Guid Tenant => tenant.MunicipalityId;
    private bool Valid => Tenant != Guid.Empty && (!user.MunicipalityId.HasValue || user.MunicipalityId == Tenant);

    [HttpGet("configuration")]
    public async Task<ActionResult<WeighingConfigurationDto>> Configuration(CancellationToken ct)
    {
        if (!Valid) return Forbid();
        var today = clock.PhilippineToday;
        var tomorrow = today.AddDays(1);
        async Task<WeighingTypeConfigurationDto> Read(FishMeatVendorType type, DateOnly day)
        {
            var q = await WeighingRateSchedule.ResolveAsync(db, rates, Tenant, type, day, ct);
            return new(type, q is not null, q?.RatePerKilo, q?.EffectiveDate, q?.Legacy ?? false);
        }
        return Ok(new WeighingConfigurationDto(today, tomorrow,
            [await Read(FishMeatVendorType.Fish, today), await Read(FishMeatVendorType.Meat, today)],
            [await Read(FishMeatVendorType.Fish, tomorrow), await Read(FishMeatVendorType.Meat, tomorrow)]));
    }

    [HttpPut("configuration")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<ActionResult<bool>> SetConfiguration(SetWeighingConfigurationRequest request, CancellationToken ct)
    {
        if (!Valid) return Forbid();
        if (!ValidAmount(request.FishEnabled, request.FishRatePerKilo)
            || !ValidAmount(request.MeatEnabled, request.MeatRatePerKilo))
            return BadRequest("An enabled weighing type needs an approved rate greater than zero with at most two decimal places. Disabled types cannot carry a rate.");
        var effective = clock.PhilippineToday.AddDays(1);
        // A batch of both types is saved atomically by EF. Existing and pending revisions are append-only.
        var fish = WeighingRateVersion.Create(Tenant, FishMeatVendorType.Fish,
            request.FishEnabled, request.FishEnabled ? request.FishRatePerKilo : null, effective, user.Username ?? "Head");
        var meat = WeighingRateVersion.Create(Tenant, FishMeatVendorType.Meat,
            request.MeatEnabled, request.MeatEnabled ? request.MeatRatePerKilo : null, effective, user.Username ?? "Head");
        db.WeighingRateVersions.AddRange(fish, meat);
        await db.SaveChangesAsync(ct);
        return Ok(true);
    }

    private static bool ValidAmount(bool enabled, decimal? rate) =>
        !enabled ? rate is null : rate is > 0m and <= 999999m && decimal.Round(rate.Value, 2) == rate;

    [HttpGet("source-native-report")]
    public async Task<ActionResult<WeighingCollectionReportDto>> SourceNativeReport(int year, int month, CancellationToken ct)
    {
        if (!Valid) return Forbid();
        if (year is < 2000 or > 2200 || month is < 1 or > 12) return BadRequest("Choose a valid reporting month.");
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);
        var classification = await db.RevenueClassifications.AsNoTracking()
            .Where(c => c.MunicipalityId == Tenant && c.SemanticCode == "WEIGHT_AND_MEASURE")
            .Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct);
        if (classification is null) return Ok(new WeighingCollectionReportDto(year, month, []));
        var lines = await (from line in db.CollectionLines.AsNoTracking()
            join collection in db.Collections.AsNoTracking()
                on new { line.MunicipalityId, Id = line.CollectionId }
                equals new { collection.MunicipalityId, collection.Id }
            where line.MunicipalityId == Tenant && line.RevenueClassificationId == classification
                && line.SourceKind == CollectionSourceKind.FishMeatVendorRegistration
                && collection.BusinessDate >= start && collection.BusinessDate < end
            orderby collection.BusinessDate, collection.Id
            select new { line.Id, line.Amount, line.CalculationSnapshot,
                collection.BusinessDate, collection.ReferenceCode, collection.PayerName }).ToListAsync(ct);
        var ids = lines.Select(x => x.Id).ToArray();
        var effects = (await db.CollectionCorrectionLines.AsNoTracking()
                .Where(x => x.MunicipalityId == Tenant && ids.Contains(x.OriginalCollectionLineId))
                .Select(x => new { x.OriginalCollectionLineId, x.FinancialEffectAmount }).ToListAsync(ct))
            .GroupBy(x => x.OriginalCollectionLineId).ToDictionary(x => x.Key, x => x.Sum(e => e.FinancialEffectAmount));
        var result = new List<WeighingCollectionRowDto>();
        foreach (var line in lines)
        {
            // Always read the posted calculation snapshot; never re-price an old record using today's rate.
            // A missing/corrupt snapshot is an audit exception, not an invitation to infer the type or rate.
            if (!TryReadFrozen(line.CalculationSnapshot, out var type, out var kg, out var rate))
                return Conflict("A posted weighing record has incomplete frozen calculation evidence. Review the collection ledger before generating this breakdown.");
            var net = line.Amount + effects.GetValueOrDefault(line.Id);
            result.Add(new(line.BusinessDate, line.ReferenceCode, line.PayerName ?? "Registered vendor",
                type, kg, rate, net, net <= 0m ? "Reversed" : net < line.Amount ? "Corrected" : "Posted"));
        }
        return Ok(new WeighingCollectionReportDto(year, month, result));
    }

    private static bool TryReadFrozen(string? snapshot, out FishMeatVendorType type, out decimal kilograms, out decimal rate)
    {
        type = default; kilograms = 0; rate = 0;
        if (string.IsNullOrWhiteSpace(snapshot)) return false;
        try
        {
            using var doc = JsonDocument.Parse(snapshot);
            if (!doc.RootElement.TryGetProperty("Calculation", out var calc)
                || !calc.TryGetProperty("Vendor", out var vendor)
                || !vendor.TryGetProperty("VendorType", out var rawType)
                || !calc.TryGetProperty("Kilograms", out var rawKg)
                || !calc.TryGetProperty("Rate", out var rawRate)
                || !rawKg.TryGetDecimal(out kilograms)
                || !rawRate.TryGetDecimal(out rate)) return false;
            type = rawType.ValueKind == JsonValueKind.Number
                ? (FishMeatVendorType)rawType.GetInt32()
                : Enum.TryParse<FishMeatVendorType>(rawType.GetString(), out var parsed) ? parsed : default;
            return Enum.IsDefined(type) && kilograms > 0m && rate > 0m;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }
}
