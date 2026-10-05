using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.Revenue;

/// <summary>Weighing alone: no rent projection and no mutation/repricing of legacy weighing rows.</summary>
internal sealed class WeighingCollectionSource(AppDbContext db)
{
    internal sealed record Facts(Guid PayorId, string PayerName, string Context, decimal Amount,
        RevenueClassification Classification, RevenueClassificationPolicy Policy, string Snapshot);
    public async Task<Facts?> QuoteAsync(Guid tenant, Guid collector, DateOnly date, Guid? payer, SessionWeighingIntent intent, CancellationToken ct)
    {
        if (!Enum.IsDefined(intent.Type) || intent.Kilograms <= 0 || intent.Kilograms > 1_000_000m
            || decimal.Round(intent.Kilograms, 2) != intent.Kilograms || !payer.HasValue) return null;
        if (!await db.CollectorUsers.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenant && x.Id == collector && x.IsActive
            && x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM), ct)) return null;
        var stall = await db.Stalls.AsNoTracking().Include(x => x.Facility).Include(x => x.Contracts).ThenInclude(x => x.Payor)
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenant && x.Id == intent.StallId && x.Facility!.Code == FacilityCode.NPM
                && (x.Section == MarketSection.FishSection || x.Section == MarketSection.MeatSection), ct);
        var contract = stall?.OccupancyAnsweringForMonth(date.Year, date.Month, date)?.Contract;
        if (contract?.PayorId != payer || contract.Payor?.MunicipalityId != tenant) return null;
        var key = intent.Type == WeighingType.Fish ? FeeRateKey.NpmFishPerKilo : FeeRateKey.NpmMeatPerKilo;
        var entry = (await new FeeRateResolver(db).GetSnapshotAsync(ct)).ResolveEntryOrNull(key, date);
        if (entry is not { Amount: > 0m }) return null;
        var classification = await db.RevenueClassifications.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenant
            && x.SemanticCode == "WEIGHT_AND_MEASURE" && x.IsActive, ct);
        if (classification is null) return null;
        var policy = await db.RevenueClassificationPolicies.AsNoTracking().Where(x => x.MunicipalityId == tenant
            && x.RevenueClassificationId == classification.Id && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= date)
            .OrderByDescending(x => x.EffectiveDate).FirstOrDefaultAsync(ct);
        if (policy?.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt) return null;
        var rate = await db.FacilityRates.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenant
            && x.FacilityCode == entry.Value.Facility && x.RateKey == key && x.EffectiveDate == entry.Value.EffectiveDate, ct);
        if (rate is null) return null;
        var amount = decimal.Round(intent.Kilograms * entry.Value.Amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0 || amount > Collection.MaximumMoneyAmount) return null;
        var snapshot = JsonSerializer.Serialize(new { SchemaVersion = 1, StallId = stall!.Id, stall.StallNo, PayorId = payer,
            BusinessDate = date, intent.Type, intent.Kilograms, RateId = rate.Id, rate.UpdatedAt, Rate = entry.Value.Amount,
            RateEffectiveDate = entry.Value.EffectiveDate, Amount = amount, PolicyId = policy.Id });
        return new(payer.Value, contract.Payor.DisplayName, stall!.StallNo, amount, classification, policy, snapshot);
    }
    public Task<Collection> PostAsync(Guid tenant, Guid collector, string username, DateOnly date, Guid operation,
        SessionWeighingIntent intent, Facts facts, CancellationToken ct)
    {
        var normalized = JsonSerializer.Serialize(new { Version = 1, date, facts.PayorId, intent });
        var line = new CollectionLineDraft(facts.Classification, facts.Policy, facts.Amount,
            CollectionSourceKind.NpmWeighing, intent.StallId, null, facts.Snapshot);
        return new CanonicalCollectionPostingCoordinator(db).PostAsync(tenant, operation, 1, normalized,
            "MobileWeighing", collector.ToString("N"), username, "Collector", date, username, [line], null,
            collectorId: collector, payorId: facts.PayorId, payerName: facts.PayerName, ct: ct);
    }
}
