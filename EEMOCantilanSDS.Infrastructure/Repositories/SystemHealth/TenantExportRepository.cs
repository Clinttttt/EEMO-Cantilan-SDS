using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;

/// <summary>
/// Assembles a per-tenant data export for the CALLER'S municipality only. Every query is doubly scoped:
///  1. the AppDbContext global tenant filter (MunicipalityId == CurrentMunicipalityId), AND
///  2. an explicit <c>MunicipalityId == mid</c> predicate here (defense-in-depth for this high-stakes read).
/// If the tenant is unresolved (<see cref="Guid.Empty"/>) the export is EMPTY — it never falls back to an
/// unscoped read of the shared database. Credential material (user accounts, payor activation codes) is
/// deliberately excluded; the export covers operational/financial data + the audit trail.
/// </summary>
public class TenantExportRepository(AppDbContext context) : ITenantExportRepository
{
    public async Task<TenantExportPayload> ExportAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var mid = context.CurrentMunicipalityId;

        // Never export without a resolved tenant — a no-op filter would otherwise span every LGU.
        if (mid == Guid.Empty)
            return new TenantExportPayload("tenant", "Municipality", mid, now,
                new Dictionary<string, object>());

        var muni = await context.Municipalities.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mid, ct);

        var tables = new Dictionary<string, object>
        {
            ["Facilities"] = await context.Facilities.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["FacilityRates"] = await context.FacilityRates.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["OrSeriesConfigs"] = await context.OrSeriesConfigs.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["Stalls"] = await context.Stalls.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["Contracts"] = await context.Contracts.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["PaymentRecords"] = await context.PaymentRecords.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["DailyCollections"] = await context.DailyCollections.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["UtilityBills"] = await context.UtilityBills.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["StallMonthlyExceptions"] = await context.StallMonthlyExceptions.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["NpmMarketClosures"] = await context.NpmMarketClosures.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["OnlinePaymentTransactions"] = await context.OnlinePaymentTransactions.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["SlaughterTransactions"] = await context.SlaughterTransactions.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["SlaughterAnimalRates"] = await context.SlaughterAnimalRates.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["TpmVendors"] = await context.TpmVendors.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["TpmAttendances"] = await context.TpmAttendances.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["TrmTransporters"] = await context.TrmTransporters.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["TrmTrips"] = await context.TrmTrips.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["PayorStallLinks"] = await context.PayorStallLinks.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["CollectorFacilityAssignments"] = await context.CollectorFacilityAssignments.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
            ["AuditLogs"] = await context.AuditLogs.AsNoTracking().Where(x => x.MunicipalityId == mid).ToListAsync(ct),
        };

        return new TenantExportPayload(
            muni?.TenantCode ?? "tenant",
            muni?.Name ?? "Municipality",
            mid,
            now,
            tables);
    }
}
