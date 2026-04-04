using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class FacilityRepository(AppDbContext context) : IFacilityRepository
{
    public async Task<FacilitySummaryDto> GetSummaryAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct)
    {
        var facility = await context.Facilities
            .Include(f => f.Stalls)
            .ThenInclude(s => s.PaymentRecords)
            .FirstOrDefaultAsync(f => f.Code == facilityCode, ct);

        if (facility == null)
            return new FacilitySummaryDto(0, 0, 0, 0);

        var activeStalls = facility.Stalls.Where(s => s.Status == StallStatus.Active).ToList();
        var totalStalls = activeStalls.Count;

        var payments = activeStalls
            .SelectMany(s => s.PaymentRecords)
            .Where(p => p.BillingYear == year && p.BillingMonth == month)
            .ToList();

        var totalCollected = payments
            .Where(p => p.Status == PaymentStatus.Paid || p.Status == PaymentStatus.Partial)
            .Sum(p => p.Status == PaymentStatus.Paid 
                ? p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + (p.FishKilos.HasValue ? p.FishKilos.Value * 1.0m : 0)
                : p.PartialAmount);

        var totalPending = payments
            .Where(p => p.Status == PaymentStatus.Unpaid || p.Status == PaymentStatus.Partial)
            .Sum(p =>
            {
                var total = p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + (p.FishKilos.HasValue ? p.FishKilos.Value * 1.0m : 0);
                return p.Status == PaymentStatus.Partial ? total - p.PartialAmount : total;
            });

        var collectionRate = totalStalls > 0 
            ? (decimal)payments.Count(p => p.Status == PaymentStatus.Paid) / totalStalls * 100 
            : 0;

        return new FacilitySummaryDto(totalCollected, totalPending, collectionRate, totalStalls);
    }
}
