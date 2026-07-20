using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public partial class FacilityReportsRepository
{
    /// <summary>
    /// NPM electricity + water collection totals for the period. Computed from utility bills the same way
    /// the mobile report does (readings × rate → charge; paid per status), but facility-wide (no collector
    /// filter). Kept SEPARATE from the facility collection totals so Collected/Unpaid are never affected.
    /// </summary>
    public async Task<(decimal ElecCollected, decimal WaterCollected, decimal Outstanding)> GetNpmUtilityTotalsAsync(
        int year, int? month, CancellationToken ct = default)
    {
        var query = context.UtilityBills.AsNoTracking().Where(b => b.BillingYear == year);
        if (month is int m)
            query = query.Where(b => b.BillingMonth == m);

        var bills = await query
            .Select(b => new
            {
                b.ElecPreviousReading,
                b.ElecCurrentReading,
                b.ElecRatePerKwh,
                b.ElecStatus,
                b.ElecPartialAmount,
                b.WaterPreviousReading,
                b.WaterCurrentReading,
                b.WaterRatePerCubicMeter,
                b.WaterStatus,
                b.WaterPartialAmount
            })
            .ToListAsync(ct);

        decimal elecCollected = 0m, waterCollected = 0m, outstanding = 0m;
        foreach (var b in bills)
        {
            var elecCharge = Math.Max(0m, b.ElecCurrentReading - b.ElecPreviousReading) * b.ElecRatePerKwh;
            var waterCharge = Math.Max(0m, b.WaterCurrentReading - b.WaterPreviousReading) * b.WaterRatePerCubicMeter;

            var elecPaid = b.ElecStatus == PaymentStatus.Paid ? elecCharge
                         : b.ElecStatus == PaymentStatus.Partial ? b.ElecPartialAmount
                         : 0m;
            var waterPaid = b.WaterStatus == PaymentStatus.Paid ? waterCharge
                          : b.WaterStatus == PaymentStatus.Partial ? b.WaterPartialAmount
                          : 0m;

            elecCollected += elecPaid;
            waterCollected += waterPaid;
            outstanding += Math.Max(0m, elecCharge - elecPaid) + Math.Max(0m, waterCharge - waterPaid);
        }

        return (elecCollected, waterCollected, outstanding);
    }
}
