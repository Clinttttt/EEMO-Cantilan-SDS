using EEMOCantilanSDS.Application.Dtos.Facilities;
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
            var (elecCharge, elecPaid) = UtilityPosition(b.ElecCurrentReading, b.ElecPreviousReading, b.ElecRatePerKwh, b.ElecStatus, b.ElecPartialAmount);
            var (waterCharge, waterPaid) = UtilityPosition(b.WaterCurrentReading, b.WaterPreviousReading, b.WaterRatePerCubicMeter, b.WaterStatus, b.WaterPartialAmount);

            elecCollected += elecPaid;
            waterCollected += waterPaid;
            outstanding += Math.Max(0m, elecCharge - elecPaid) + Math.Max(0m, waterCharge - waterPaid);
        }

        return (elecCollected, waterCollected, outstanding);
    }

    /// <summary>
    /// What one utility on one bill charged, and what was collected against it: consumption times the rate on the
    /// bill, and the whole charge when settled, the stated part when part-settled, nothing otherwise.
    ///
    /// <para>
    /// Stated once and used by both utility reads, so the office's totals and its per-payor sheet cannot come to
    /// different figures. A reading that went backwards charges nothing rather than a negative.
    /// </para>
    /// </summary>
    private static (decimal Charge, decimal Paid) UtilityPosition(
        decimal current, decimal previous, decimal rate, PaymentStatus status, decimal partialAmount)
    {
        var charge = Math.Max(0m, current - previous) * rate;
        var paid = status == PaymentStatus.Paid ? charge
                 : status == PaymentStatus.Partial ? partialAmount
                 : 0m;
        return (charge, paid);
    }

    /// <summary>
    /// The month's electricity and water bill for every space that has one, for the sheet the office files: what each
    /// utility charged, what was collected against it, and what is still owed, per payor.
    ///
    /// <para>
    /// The facility-wide totals answer "how much"; this answers "from whom", which is what a filed sheet has to show.
    /// Both compute a charge through the same helper above.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<MonthEndUtilityRowDto>> GetNpmUtilityRowsAsync(
        int year, int month, CancellationToken ct = default)
    {
        var bills = await context.UtilityBills
            .AsNoTracking()
            .Where(b => b.BillingYear == year && b.BillingMonth == month)
            .Select(b => new
            {
                b.StallId,
                b.ElecPreviousReading, b.ElecCurrentReading, b.ElecRatePerKwh, b.ElecStatus, b.ElecPartialAmount, b.ElecORNumber,
                b.WaterPreviousReading, b.WaterCurrentReading, b.WaterRatePerCubicMeter, b.WaterStatus, b.WaterPartialAmount, b.WaterORNumber
            })
            .ToListAsync(ct);

        if (bills.Count == 0) return Array.Empty<MonthEndUtilityRowDto>();

        var stallIds = bills.Select(b => b.StallId).ToList();

        // The space's number, and who occupies it, from the same source the rest of the sheet names payors by.
        var spaces = await context.Stalls
            .AsNoTracking()
            .Where(s => stallIds.Contains(s.Id))
            .Select(s => new { s.Id, s.StallNo })
            .ToListAsync(ct);
        var spaceNoById = spaces.ToDictionary(s => s.Id, s => s.StallNo);

        var occupants = await context.Contracts
            .AsNoTracking()
            .Where(c => stallIds.Contains(c.StallId) && c.IsActive)
            .Select(c => new { c.StallId, c.ActualOccupant })
            .ToListAsync(ct);
        var occupantByStall = occupants
            .GroupBy(o => o.StallId)
            .ToDictionary(g => g.Key, g => g.First().ActualOccupant);

        var rows = new List<MonthEndUtilityRowDto>(bills.Count);
        foreach (var b in bills)
        {
            var (elecCharge, elecPaid) = UtilityPosition(b.ElecCurrentReading, b.ElecPreviousReading, b.ElecRatePerKwh, b.ElecStatus, b.ElecPartialAmount);
            var (waterCharge, waterPaid) = UtilityPosition(b.WaterCurrentReading, b.WaterPreviousReading, b.WaterRatePerCubicMeter, b.WaterStatus, b.WaterPartialAmount);

            // A bill with nothing charged on either utility is not a line on a sheet.
            if (elecCharge <= 0m && waterCharge <= 0m) continue;

            var or = !string.IsNullOrWhiteSpace(b.ElecORNumber) ? b.ElecORNumber
                   : !string.IsNullOrWhiteSpace(b.WaterORNumber) ? b.WaterORNumber
                   : null;

            rows.Add(new MonthEndUtilityRowDto(
                spaceNoById.GetValueOrDefault(b.StallId) ?? string.Empty,
                occupantByStall.GetValueOrDefault(b.StallId) ?? string.Empty,
                elecCharge, elecPaid,
                waterCharge, waterPaid,
                or));
        }

        return rows;
    }
}
