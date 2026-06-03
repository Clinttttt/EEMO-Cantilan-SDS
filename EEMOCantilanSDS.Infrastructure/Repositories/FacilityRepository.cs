using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class FacilityRepository(AppDbContext context) : IFacilityRepository
{
    public async Task<Facility?> GetByCodeAsync(FacilityCode facilityCode, CancellationToken ct)
    {
        return await context.Facilities.FirstOrDefaultAsync(f => f.Code == facilityCode, ct);
    }
    public async Task<FacilitySummaryDto> GetSummaryAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct)
    {
        var facility = await context.Facilities
            .AsNoTracking()
            .Include(f => f.Stalls.Where(s => !s.IsDeleted))
                .ThenInclude(s => s.PaymentRecords.Where(p => p.BillingYear == year && p.BillingMonth == month && !p.IsDeleted))
            .FirstOrDefaultAsync(f => f.Code == facilityCode, ct);

        if (facility == null)
            return new FacilitySummaryDto(0, 0, 0, 0);

        var activeStalls = facility.Stalls.Where(s => s.Status == StallStatus.Active).ToList();
        var totalStalls = activeStalls.Count;

        var payments = activeStalls.SelectMany(s => s.PaymentRecords).ToList();

        var totalCollected = payments.Sum(p => p.AmountPaid);
        var totalPending = payments.Sum(p => p.BalanceDue);

        var collectionRate = totalStalls > 0 
            ? (decimal)payments.Count(p => p.Status == PaymentStatus.Paid) / totalStalls * 100 
            : 0;

        return new FacilitySummaryDto(totalCollected, totalPending, collectionRate, totalStalls);
    }

    public async Task<IReadOnlyList<FacilitySidebarSummaryDto>> GetSidebarSummariesAsync(int year, int month, CancellationToken ct)
    {
        // One server-side query. Unpaid = active, occupied stalls with no Paid record for the month.
        // Soft-deleted rows are excluded by the global query filters.
        return await context.Facilities
            .AsNoTracking()
            .OrderBy(f => f.Code)
            .Select(f => new FacilitySidebarSummaryDto(
                f.Code,
                f.Name,
                f.ShortName,
                f.Stalls.Count(s => s.Status == StallStatus.Active
                    && s.Contracts.Any(c => c.IsActive)
                    && !s.PaymentRecords.Any(p => p.BillingYear == year
                        && p.BillingMonth == month
                        && p.Status == PaymentStatus.Paid))))
            .ToListAsync(ct);
    }
}
