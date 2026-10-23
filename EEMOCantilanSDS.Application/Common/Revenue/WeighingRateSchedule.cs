using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Independent weighing policy. Explicit revisions win; legacy tenant-owned NPM rates are
/// read only for dates before adoption, preserving Cantilan and older municipalities without cross-tenant defaults.</summary>
public static class WeighingRateSchedule
{
    public sealed record Quote(Guid Id, decimal RatePerKilo, DateOnly EffectiveDate, bool Legacy, DateTime? UpdatedAt = null);

    public static async Task<Quote?> ResolveAsync(IAppDbContext db, IFeeRateResolver rates,
        Guid tenantId, FishMeatVendorType vendorType, DateOnly day, CancellationToken ct)
    {
        var rows = await db.WeighingRateVersions.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.VendorType == vendorType && x.EffectiveDate <= day)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.RecordedAtUtc)
            .ThenByDescending(x => x.Id).Take(1).ToListAsync(ct);
        if (rows.Count > 0)
        {
            var row = rows[0];
            return row.IsEnabled && row.RatePerKilo is > 0m
                ? new(row.Id, row.RatePerKilo.Value, row.EffectiveDate, false) : null;
        }

        // Compatibility only: existing tenant-owned NPM rate rows remain valid before this
        // municipality adopts independent weighing. No global Cantilan fallback is possible.
        var key = vendorType == FishMeatVendorType.Fish ? FeeRateKey.NpmFishPerKilo : FeeRateKey.NpmMeatPerKilo;
        var entry = (await rates.GetSnapshotAsync(ct)).ResolveEntryOrNull(key, day);
        if (entry is not { Amount: > 0m }) return null;
        var oldRow = await db.FacilityRates.AsNoTracking().Where(x => x.MunicipalityId == tenantId
            && x.FacilityCode == entry.Value.Facility && x.RateKey == key
            && x.EffectiveDate == entry.Value.EffectiveDate)
            .Select(x => new { x.Id, x.UpdatedAt }).FirstOrDefaultAsync(ct);
        return oldRow is null ? null : new(oldRow.Id, entry.Value.Amount, entry.Value.EffectiveDate, true, oldRow.UpdatedAt);
    }

    public static async Task<bool> HasAvailableTypeAsync(IAppDbContext db, Guid tenantId, DateOnly day, CancellationToken ct)
    {
        var versions = await db.WeighingRateVersions.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.EffectiveDate <= day)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.RecordedAtUtc)
            .ThenByDescending(x => x.Id).ToListAsync(ct);
        foreach (var type in new[] { FishMeatVendorType.Fish, FishMeatVendorType.Meat })
        {
            var explicitRate = versions.FirstOrDefault(x => x.VendorType == type);
            if (explicitRate is not null)
            {
                if (explicitRate.IsEnabled && explicitRate.RatePerKilo is > 0m) return true;
                continue; // Explicitly disabled; never fall back to NPM.
            }
            var key = type == FishMeatVendorType.Fish ? FeeRateKey.NpmFishPerKilo : FeeRateKey.NpmMeatPerKilo;
            var legacy = await db.FacilityRates.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && x.FacilityCode == FacilityCode.NPM
                    && x.RateKey == key && x.EffectiveDate <= day)
                .OrderByDescending(x => x.EffectiveDate).Select(x => (decimal?)x.Amount)
                .FirstOrDefaultAsync(ct);
            if (legacy is > 0m) return true;
        }
        return false;
    }

    public static Task<bool> HasAdoptedIndependentRatesAsync(IAppDbContext db, Guid tenantId, DateOnly day, CancellationToken ct) =>
        db.WeighingRateVersions.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenantId && x.EffectiveDate <= day, ct);

    public static async Task<bool> IsExplicitlyDisabledAsync(IAppDbContext db, Guid tenantId,
        FishMeatVendorType type, DateOnly day, CancellationToken ct) =>
        await db.WeighingRateVersions.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.VendorType == type && x.EffectiveDate <= day)
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.RecordedAtUtc)
            .ThenByDescending(x => x.Id).Select(x => (bool?)!x.IsEnabled).FirstOrDefaultAsync(ct) == true;
}
