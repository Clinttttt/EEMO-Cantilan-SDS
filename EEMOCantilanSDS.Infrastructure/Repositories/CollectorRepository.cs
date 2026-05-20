using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class CollectorRepository(AppDbContext context) : ICollectorRepository
{
    public async Task<CollectorUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);
    }

    public async Task<CollectorUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await context.CollectorUsers
            .FirstOrDefaultAsync(c => c.Username == username && !c.IsDeleted, cancellationToken);
    }

    public async Task<List<CollectorListDto>> GetAllCollectorsWithStatsAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var collectors = await context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .Where(c => !c.IsDeleted)
            .ToListAsync(cancellationToken);

        var result = new List<CollectorListDto>();

        foreach (var collector in collectors)
        {
            var paymentTotal = await context.PaymentRecords
                .Where(p => p.CollectorId == collector.Id && 
                            p.BillingYear == year && 
                            p.BillingMonth == month)
                .SumAsync(p => p.BaseRentalAmount + (p.ElecAmount ?? 0) + 
                              (p.WaterAmount ?? 0) + ((p.FishKilos ?? 0) * 1.0m), cancellationToken);

            var dailyTotal = await context.DailyCollections
                .Where(d => d.CreatedBy == collector.Username && 
                            d.CollectionDate.Year == year && 
                            d.CollectionDate.Month == month)
                .SumAsync(d => 30m + ((d.FishKilos ?? 0) * 1.0m), cancellationToken);

            var paymentCount = await context.PaymentRecords
                .CountAsync(p => p.CollectorId == collector.Id && 
                                p.BillingYear == year && 
                                p.BillingMonth == month, cancellationToken);

            var dailyCount = await context.DailyCollections
                .CountAsync(d => d.CreatedBy == collector.Username && 
                                d.CollectionDate.Year == year && 
                                d.CollectionDate.Month == month, cancellationToken);

            result.Add(new CollectorListDto(
                collector.Id,
                collector.FullName!,
                collector.Email!,
                collector.EmployeeId!,
                collector.FacilityAssignments.Select(fa => fa.FacilityCode).ToList(),
                paymentTotal + dailyTotal,
                paymentCount + dailyCount,
                collector.LastActiveAt,
                collector.IsActive));
        }

        return result.OrderByDescending(c => c.LastActiveAt).ToList();
    }

    public async Task<CollectorActivityDto?> GetCollectorActivityAsync(Guid collectorId, int year, int month, CancellationToken cancellationToken = default)
    {
        var collector = await context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .FirstOrDefaultAsync(c => c.Id == collectorId && !c.IsDeleted, cancellationToken);

        if (collector is null)
            return null;

        var collectedThisMonth = await context.PaymentRecords
            .Where(p => p.CollectorId == collector.Id && 
                        p.BillingYear == year && 
                        p.BillingMonth == month)
            .SumAsync(p => p.BaseRentalAmount + (p.ElecAmount ?? 0) + 
                          (p.WaterAmount ?? 0) + ((p.FishKilos ?? 0) * 1.0m), cancellationToken) +
            await context.DailyCollections
            .Where(d => d.CreatedBy == collector.Username && 
                        d.CollectionDate.Year == year && 
                        d.CollectionDate.Month == month)
            .SumAsync(d => 30m + ((d.FishKilos ?? 0) * 1.0m), cancellationToken);

        var transactions = await context.PaymentRecords
            .CountAsync(p => p.CollectorId == collector.Id && 
                            p.BillingYear == year && 
                            p.BillingMonth == month, cancellationToken) +
            await context.DailyCollections
            .CountAsync(d => d.CreatedBy == collector.Username && 
                            d.CollectionDate.Year == year && 
                            d.CollectionDate.Month == month, cancellationToken);

        var recentPayments = await context.PaymentRecords
            .Include(p => p.Stall)
                .ThenInclude(s => s!.Facility)
            .Include(p => p.Stall)
                .ThenInclude(s => s!.Contracts)
            .Where(p => p.CollectorId == collector.Id)
            .OrderByDescending(p => p.PaidAt)
            .Take(10)
            .Select(p => new RecentTransactionDto(
                p.ORNumber!,
                p.Stall!.Contracts.FirstOrDefault(c => c.IsActive)!.ActualOccupant,
                p.Stall.Facility!.Code,
                "Stall Rental",
                p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + ((p.FishKilos ?? 0) * 1.0m),
                p.Status.ToString(),
                p.PaidAt ?? p.UpdatedAt!.Value))
            .ToListAsync(cancellationToken);

        return new CollectorActivityDto(
            collector.Id,
            collector.FullName!,
            collector.EmployeeId!,
            collector.Email!,
            collector.ContactNumber!,
            collector.FacilityAssignments.Select(fa => fa.FacilityCode).ToList(),
            collectedThisMonth,
            transactions,
            collector.FacilityAssignments.Count,
            collector.LastActiveAt,
            recentPayments);
    }

    public async Task AddAsync(CollectorUser collector, CancellationToken cancellationToken = default)
    {
        await context.CollectorUsers.AddAsync(collector, cancellationToken);
    }

    public async Task<bool> IsEmployeeIdUniqueAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        return !await context.CollectorUsers.AnyAsync(c => c.EmployeeId == employeeId, cancellationToken);
    }

    public async Task<bool> IsUsernameUniqueAsync(string username, CancellationToken cancellationToken = default)
    {
        return !await context.Users.AnyAsync(u => u.Username == username, cancellationToken);
    }

    public async Task<bool> IsEmailUniqueAsync(string email, CancellationToken cancellationToken = default)
    {
        return !await context.Users.AnyAsync(u => u.Email == email, cancellationToken);
    }

    public async Task AddFacilityAssignmentsAsync(Guid collectorId, List<FacilityCode> facilityCodes, CancellationToken cancellationToken = default)
    {
        var facilities = await context.Facilities
            .Where(f => facilityCodes.Contains(f.Code))
            .ToListAsync(cancellationToken);

        foreach (var facility in facilities)
        {
            var assignment = CollectorFacilityAssignment.Create(
                collectorId,
                facility.Id,
                facility.Code);

            await context.CollectorFacilityAssignments.AddAsync(assignment, cancellationToken);
        }
    }

    public async Task<string> GenerateNextEmployeeIdAsync(CancellationToken cancellationToken = default)
    {
        var currentYear = DateTime.UtcNow.Year;
        var prefix = $"EEMO-{currentYear}-";

        var lastEmployeeId = await context.CollectorUsers
            .Where(c => c.EmployeeId!.StartsWith(prefix))
            .OrderByDescending(c => c.EmployeeId)
            .Select(c => c.EmployeeId)
            .FirstOrDefaultAsync(cancellationToken);

        int nextNumber = 1;
        if (lastEmployeeId != null)
        {
            var numberPart = lastEmployeeId.Replace(prefix, "");
            if (int.TryParse(numberPart, out int lastNumber))
            {
                nextNumber = lastNumber + 1;
            }
        }

        return $"{prefix}{nextNumber:D3}";
    }
}