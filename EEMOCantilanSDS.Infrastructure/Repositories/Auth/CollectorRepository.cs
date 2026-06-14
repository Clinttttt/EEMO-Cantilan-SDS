using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
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
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<CollectorUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await context.CollectorUsers
            .FirstOrDefaultAsync(c => c.Username == username, cancellationToken);
    }

    public async Task<CollectorUser?> GetByUsernameOrEmployeeIdAsync(string usernameOrEmployeeId, CancellationToken cancellationToken = default)
    {
        var normalized = usernameOrEmployeeId.Trim();
        return await context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .FirstOrDefaultAsync(c =>
                c.Username == normalized || c.EmployeeId == normalized,
                cancellationToken);
    }

    public async Task<List<CollectorListDto>> GetAllCollectorsWithStatsAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        var collectors = await context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .ToListAsync(cancellationToken);

        var collectorIds = collectors.Select(c => c.Id).ToList();

        // Aggregate payment stats per collector in ONE query (was N queries in a loop).
        var paymentStats = await context.PaymentRecords
            .Where(p => p.CollectorId != null && collectorIds.Contains(p.CollectorId.Value)
                        && p.BillingYear == year && p.BillingMonth == month)
            .GroupBy(p => p.CollectorId!.Value)
            .Select(g => new
            {
                CollectorId = g.Key,
                Total = g.Sum(p => p.Status == PaymentStatus.Paid
                    ? p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + (p.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo
                    : p.Status == PaymentStatus.Partial ? p.PartialAmount : 0m),
                Count = g.Count()
            })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        // Aggregate daily-collection stats per collector in ONE query, keyed by the
        // CollectorId FK (the previous CreatedBy == Username join could never match).
        var dailyStats = await context.DailyCollections
            .Where(d => d.CollectorId != null && collectorIds.Contains(d.CollectorId.Value)
                        && d.CollectionDate.Year == year && d.CollectionDate.Month == month)
            .GroupBy(d => d.CollectorId!.Value)
            .Select(g => new
            {
                CollectorId = g.Key,
                Total = g.Sum(d => d.DailyFee + (d.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo),
                Count = g.Count()
            })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        // SLH (per-head slaughter), TRM (per-trip), TPM (per-vendor Friday) collections also carry
        // CollectorId — without these, collectors assigned to those facilities show ₱0 / 0 here.
        var slaughterStats = await context.SlaughterTransactions
            .Where(s => s.CollectorId != null && collectorIds.Contains(s.CollectorId.Value)
                        && s.TransactionDate.Year == year && s.TransactionDate.Month == month)
            .GroupBy(s => s.CollectorId!.Value)
            .Select(g => new { CollectorId = g.Key, Total = g.Sum(s => s.RatePerHead * s.NumberOfHeads), Count = g.Count() })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        var (trmStartUtc, trmEndUtc) = PhilippineTime.MonthUtcRange(year, month);
        var tripStats = await context.TrmTrips
            .Where(t => t.CollectorId != null && collectorIds.Contains(t.CollectorId.Value)
                        && t.RecordedAt >= trmStartUtc && t.RecordedAt < trmEndUtc)
            .GroupBy(t => t.CollectorId!.Value)
            .Select(g => new { CollectorId = g.Key, Total = g.Sum(t => t.Fee), Count = g.Count() })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        var tpmStats = await context.TpmAttendances
            .Where(a => a.CollectorId != null && collectorIds.Contains(a.CollectorId.Value) && a.IsPaid
                        && a.MarketDate.Year == year && a.MarketDate.Month == month)
            .GroupBy(a => a.CollectorId!.Value)
            .Select(g => new { CollectorId = g.Key, Total = g.Sum(a => a.Fee), Count = g.Count() })
            .ToDictionaryAsync(x => x.CollectorId, cancellationToken);

        var result = new List<CollectorListDto>();

        foreach (var collector in collectors)
        {
            paymentStats.TryGetValue(collector.Id, out var payment);
            dailyStats.TryGetValue(collector.Id, out var daily);
            slaughterStats.TryGetValue(collector.Id, out var slaughter);
            tripStats.TryGetValue(collector.Id, out var trip);
            tpmStats.TryGetValue(collector.Id, out var tpm);

            result.Add(new CollectorListDto(
                collector.Id,
                collector.FullName!,
                collector.Email!,
                collector.EmployeeId!,
                collector.FacilityAssignments.Select(fa => fa.FacilityCode).ToList(),
                (payment?.Total ?? 0m) + (daily?.Total ?? 0m) + (slaughter?.Total ?? 0m) + (trip?.Total ?? 0m) + (tpm?.Total ?? 0m),
                (payment?.Count ?? 0) + (daily?.Count ?? 0) + (slaughter?.Count ?? 0) + (trip?.Count ?? 0) + (tpm?.Count ?? 0),
                collector.LastActiveAt,
                collector.IsActive));
        }

        return result.OrderByDescending(c => c.LastActiveAt).ToList();
    }

    public async Task<CollectorActivityDto?> GetCollectorActivityAsync(Guid collectorId, int year, int month, CancellationToken cancellationToken = default)
    {
        var collector = await context.CollectorUsers
            .Include(c => c.FacilityAssignments)
            .FirstOrDefaultAsync(c => c.Id == collectorId, cancellationToken);

        if (collector is null)
            return null;

        var collectedThisMonth = await context.PaymentRecords
            .Where(p => p.CollectorId == collector.Id && 
                        p.BillingYear == year && 
                        p.BillingMonth == month)
            .SumAsync(p => p.Status == PaymentStatus.Paid
                          ? p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + ((p.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo)
                          : p.Status == PaymentStatus.Partial ? p.PartialAmount : 0m, cancellationToken) +
            await context.DailyCollections
            .Where(d => d.CollectorId == collector.Id && 
                        d.CollectionDate.Year == year && 
                        d.CollectionDate.Month == month)
            .SumAsync(d => d.DailyFee + ((d.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo), cancellationToken);

        var (mStartUtc, mEndUtc) = PhilippineTime.MonthUtcRange(year, month);

        collectedThisMonth +=
            await context.SlaughterTransactions
                .Where(s => s.CollectorId == collector.Id && s.TransactionDate.Year == year && s.TransactionDate.Month == month)
                .SumAsync(s => s.RatePerHead * s.NumberOfHeads, cancellationToken) +
            await context.TrmTrips
                .Where(t => t.CollectorId == collector.Id && t.RecordedAt >= mStartUtc && t.RecordedAt < mEndUtc)
                .SumAsync(t => t.Fee, cancellationToken) +
            await context.TpmAttendances
                .Where(a => a.CollectorId == collector.Id && a.IsPaid && a.MarketDate.Year == year && a.MarketDate.Month == month)
                .SumAsync(a => a.Fee, cancellationToken);

        var transactions = await context.PaymentRecords
            .CountAsync(p => p.CollectorId == collector.Id && 
                            p.BillingYear == year && 
                            p.BillingMonth == month, cancellationToken) +
            await context.DailyCollections
            .CountAsync(d => d.CollectorId == collector.Id && 
                            d.CollectionDate.Year == year && 
                            d.CollectionDate.Month == month, cancellationToken) +
            await context.SlaughterTransactions
            .CountAsync(s => s.CollectorId == collector.Id && s.TransactionDate.Year == year && s.TransactionDate.Month == month, cancellationToken) +
            await context.TrmTrips
            .CountAsync(t => t.CollectorId == collector.Id && t.RecordedAt >= mStartUtc && t.RecordedAt < mEndUtc, cancellationToken) +
            await context.TpmAttendances
            .CountAsync(a => a.CollectorId == collector.Id && a.IsPaid && a.MarketDate.Year == year && a.MarketDate.Month == month, cancellationToken);

        var recentPayments = await context.PaymentRecords
            .Where(p => p.CollectorId == collector.Id && p.Status != PaymentStatus.Unpaid)
            .OrderByDescending(p => p.PaidAt ?? p.UpdatedAt)
            .Take(10)
            .Select(p => new RecentTransactionDto(
                p.ORNumber ?? "—",
                p.Stall!.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault() ?? "—",
                p.Stall.Facility!.Code,
                "Stall Rental",
                p.Status == PaymentStatus.Paid
                    ? p.BaseRentalAmount + (p.ElecAmount ?? 0) + (p.WaterAmount ?? 0) + ((p.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo)
                    : p.Status == PaymentStatus.Partial ? p.PartialAmount : 0m,
                p.Status.ToString(),
                p.PaidAt ?? p.UpdatedAt ?? p.CreatedAt))
            .ToListAsync(cancellationToken);

        // NPM collectors record daily collections (not monthly PaymentRecords), so these must be
        // merged in or the Recent Transactions list would be empty for them.
        var recentDaily = await context.DailyCollections
            .Where(d => d.CollectorId == collector.Id && d.IsPaid)
            .OrderByDescending(d => d.UpdatedAt ?? d.CreatedAt)
            .Take(10)
            .Select(d => new RecentTransactionDto(
                d.ORNumber ?? "—",
                d.Stall!.Contracts.Where(c => c.IsActive).Select(c => c.ActualOccupant).FirstOrDefault() ?? "—",
                d.Stall.Facility!.Code,
                "Daily Fee",
                d.DailyFee + ((d.FishKilos ?? 0) * FeeRates.NpmFishFeePerKilo),
                "Paid",
                d.UpdatedAt ?? d.CreatedAt))
            .ToListAsync(cancellationToken);

        // Per-transaction facilities (SLH/TRM/TPM) — these never produce PaymentRecords or
        // DailyCollections, so their recorded activity must be merged in explicitly.
        var recentSlaughter = await context.SlaughterTransactions
            .Where(s => s.CollectorId == collector.Id)
            .OrderByDescending(s => s.UpdatedAt ?? s.CreatedAt)
            .Take(10)
            .Select(s => new RecentTransactionDto(
                s.ORNumber ?? "—",
                s.OwnerName,
                FacilityCode.SLH,
                "Slaughter",
                s.RatePerHead * s.NumberOfHeads,
                "Paid",
                s.UpdatedAt ?? s.CreatedAt))
            .ToListAsync(cancellationToken);

        var recentTrips = await context.TrmTrips
            .Where(t => t.CollectorId == collector.Id)
            .OrderByDescending(t => t.RecordedAt)
            .Take(10)
            .Select(t => new RecentTransactionDto(
                t.ORNumber ?? "—",
                t.DriverName,
                FacilityCode.TRM,
                "Terminal Trip",
                t.Fee,
                "Paid",
                t.RecordedAt))
            .ToListAsync(cancellationToken);

        var recentTpm = await context.TpmAttendances
            .Where(a => a.CollectorId == collector.Id && a.IsPaid)
            .OrderByDescending(a => a.PaidAt ?? a.UpdatedAt ?? a.CreatedAt)
            .Take(10)
            .Select(a => new RecentTransactionDto(
                a.ORNumber ?? "—",
                a.Vendor!.VendorName,
                FacilityCode.TPM,
                "Market Day",
                a.Fee,
                "Paid",
                a.PaidAt ?? a.UpdatedAt ?? a.CreatedAt))
            .ToListAsync(cancellationToken);

        var recentTransactions = recentPayments
            .Concat(recentDaily)
            .Concat(recentSlaughter)
            .Concat(recentTrips)
            .Concat(recentTpm)
            .OrderByDescending(t => t.TransactionDate)
            .Take(10)
            .ToList();

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
            recentTransactions);
    }

    public async Task AddAsync(CollectorUser collector, CancellationToken cancellationToken = default)
    {
        await context.CollectorUsers.AddAsync(collector, cancellationToken);
    }

    public async Task<bool> IsEmployeeIdUniqueAsync(string employeeId, CancellationToken cancellationToken = default)
    {
        // Uniqueness must consider soft-deleted users too (their rows still exist), so bypass the global filter.
        return !await context.CollectorUsers.IgnoreQueryFilters().AnyAsync(c => c.EmployeeId == employeeId, cancellationToken);
    }

    public async Task<bool> IsUsernameUniqueAsync(string username, CancellationToken cancellationToken = default)
    {
        return !await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Username == username, cancellationToken);
    }

    public async Task<bool> IsEmailUniqueAsync(string email, CancellationToken cancellationToken = default)
    {
        return !await context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, cancellationToken);
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

    public async Task ReplaceFacilityAssignmentsAsync(Guid collectorId, List<FacilityCode> facilityCodes, CancellationToken cancellationToken = default)
    {
        var existing = await context.CollectorFacilityAssignments
            .Where(a => a.CollectorId == collectorId)
            .ToListAsync(cancellationToken);

        // Diff so unchanged assignments are left intact (avoids unique-index conflicts on re-add).
        context.CollectorFacilityAssignments.RemoveRange(existing.Where(a => !facilityCodes.Contains(a.FacilityCode)));

        var existingCodes = existing.Select(a => a.FacilityCode).ToHashSet();
        var toAdd = facilityCodes.Where(c => !existingCodes.Contains(c)).ToList();
        await AddFacilityAssignmentsAsync(collectorId, toAdd, cancellationToken);
    }

    public async Task<string> GenerateNextEmployeeIdAsync(CancellationToken cancellationToken = default)
    {
        var currentYear = PhilippineTime.Now.Year;
        var prefix = $"EEMO-{currentYear}-";

        var lastEmployeeId = await context.CollectorUsers
            .IgnoreQueryFilters()
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
