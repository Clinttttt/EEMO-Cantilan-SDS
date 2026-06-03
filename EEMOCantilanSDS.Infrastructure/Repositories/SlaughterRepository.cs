using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class SlaughterRepository(AppDbContext context) : ISlaughterRepository
{
    public async Task<SlaughterTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.SlaughterTransactions.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<SlaughterTransactionDto>> GetTransactionsByMonthAsync(int year, int month, CancellationToken ct = default)
    {
        return await context.SlaughterTransactions
            .Where(x => x.TransactionDate.Year == year && x.TransactionDate.Month == month)
            .OrderByDescending(x => x.TransactionDate)
            .Select(x => new SlaughterTransactionDto(
                x.Id,
                x.OwnerName,
                x.AnimalType,
                x.CustomAnimalType,
                x.NumberOfHeads,
                x.RatePerHead,
                x.RatePerHead * x.NumberOfHeads,
                x.ORNumber,
                x.TransactionDate
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OwnerTransactionGroupDto>> GetGroupedTransactionsByMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var allTransactions = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(x => x.TransactionDate.Year == year && x.TransactionDate.Month == month)
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.CreatedAt)
            .ThenBy(x => x.OwnerName)
            .ToListAsync(ct);

        var grouped = allTransactions
            .GroupBy(x => x.OwnerName)
            .Select(ownerGroup =>
            {
                // Group by OR number first
                var orGroups = ownerGroup.GroupBy(x => x.ORNumber).ToList();
                
                // Get the latest OR number group (most recent transaction)
                var latestORGroup = orGroups
                    .OrderByDescending(g => g.Max(t => t.TransactionDate))
                    .ThenByDescending(g => g.Max(t => t.CreatedAt))
                    .First();

                var latestDate = latestORGroup.Max(x => x.TransactionDate);
                
                // Get ALL transactions for this OR number (not just one)
                var latestTransactions = latestORGroup
                    .OrderBy(x => x.AnimalType)
                    .ThenBy(x => x.CustomAnimalType)
                    .Select(x => new SlaughterTransactionDto(
                        x.Id,
                        x.OwnerName,
                        x.AnimalType,
                        x.CustomAnimalType,
                        x.NumberOfHeads,
                        x.RatePerHead,
                        x.RatePerHead * x.NumberOfHeads,
                        x.ORNumber,
                        x.TransactionDate
                    ))
                    .ToList();

                // Count distinct OR numbers for this owner
                var distinctORCount = orGroups.Count;
                var orNumber = latestTransactions.FirstOrDefault()?.ORNumber;

                return new OwnerTransactionGroupDto(
                    ownerGroup.Key,
                    latestDate,
                    orNumber,
                    distinctORCount,
                    latestTransactions
                );
            })
            .OrderByDescending(x => x.LatestTransactionDate)
            .ThenByDescending(x => x.ORNumber)
            .ToList();

        return grouped;
    }

    public async Task<OwnerTransactionHistoryDto> GetOwnerTransactionHistoryAsync(string ownerName, int year, int month, CancellationToken ct = default)
    {
        var transactions = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(x => x.OwnerName == ownerName && x.TransactionDate.Year == year && x.TransactionDate.Month == month)
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        // Group by OR number (not by date)
        var orGroups = transactions
            .GroupBy(x => x.ORNumber)
            .Select(g => new
            {
                ORNumber = g.Key,
                LatestDate = g.Max(t => t.TransactionDate),
                LatestCreated = g.Max(t => t.CreatedAt),
                Transactions = g.ToList()
            })
            .OrderByDescending(g => g.LatestDate)
            .ThenByDescending(g => g.LatestCreated)
            .Select(g => new TransactionDateGroupDto(
                g.LatestDate,
                g.ORNumber,
                g.Transactions.Select(x => new SlaughterTransactionDto(
                    x.Id,
                    x.OwnerName,
                    x.AnimalType,
                    x.CustomAnimalType,
                    x.NumberOfHeads,
                    x.RatePerHead,
                    x.RatePerHead * x.NumberOfHeads,
                    x.ORNumber,
                    x.TransactionDate
                )).ToList()
            ))
            .ToList();

        return new OwnerTransactionHistoryDto(ownerName, orGroups);
    }

    public async Task<SlaughterOverviewDto> GetOverviewAsync(int year, int month, CancellationToken ct = default)
    {
        var transactions = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(x => x.TransactionDate.Year == year && x.TransactionDate.Month == month)
            .ToListAsync(ct);

        return new SlaughterOverviewDto(
            transactions.Count,
            transactions.Sum(x => x.NumberOfHeads),
            transactions.Sum(x => x.RatePerHead * x.NumberOfHeads),
            transactions.Where(x => x.AnimalType == AnimalType.Hog).Sum(x => x.NumberOfHeads),
            transactions.Where(x => x.AnimalType == AnimalType.Carabao).Sum(x => x.NumberOfHeads),
            transactions.Where(x => x.AnimalType == AnimalType.Cow).Sum(x => x.NumberOfHeads),
            transactions.Where(x => x.AnimalType == AnimalType.Other).Sum(x => x.NumberOfHeads)
        );
    }

    public async Task AddAsync(SlaughterTransaction transaction, CancellationToken ct = default)
        => await context.SlaughterTransactions.AddAsync(transaction, ct);

    public async Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct = default)
    {
        var existsInSlaughter = await context.SlaughterTransactions.AnyAsync(x => x.ORNumber == orNumber, ct);
        if (existsInSlaughter) return false;

        var existsInPayments = await context.PaymentRecords.AnyAsync(x => x.ORNumber == orNumber, ct);
        if (existsInPayments) return false;

        var existsInDaily = await context.DailyCollections.AnyAsync(x => x.ORNumber == orNumber, ct);
        if (existsInDaily) return false;

        var existsInTpm = await context.TpmAttendances.AnyAsync(x => x.ORNumber == orNumber, ct);
        if (existsInTpm) return false;

        var existsInTrm = await context.TrmTrips.AnyAsync(x => x.ORNumber == orNumber, ct);
        return !existsInTrm;
    }

    public async Task<IReadOnlyList<SlaughterTransaction>> GetTransactionsByOwnerDateORAsync(string ownerName, DateOnly date, string orNumber, CancellationToken ct = default)
        => await context.SlaughterTransactions
            .Where(x => x.OwnerName == ownerName && x.TransactionDate == date && x.ORNumber == orNumber)
            .ToListAsync(ct);

    public Task RemoveAsync(SlaughterTransaction transaction, CancellationToken ct = default)
    {
        context.SlaughterTransactions.Remove(transaction);
        return Task.CompletedTask;
    }

    public async Task<ClientProfileDto?> GetClientProfileAsync(string ownerName, CancellationToken ct = default)
    {
        var transactions = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(x => x.OwnerName == ownerName)
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        if (!transactions.Any())
            return null;

        var transactionDtos = transactions.Select(x => new ClientTransactionDto(
            x.TransactionDate,
            x.AnimalType == AnimalType.Other ? x.CustomAnimalType ?? "Other" : x.AnimalType.ToString(),
            x.NumberOfHeads,
            x.RatePerHead,
            x.RatePerHead * x.NumberOfHeads,
            x.ORNumber,
            x.CreatedBy
        )).ToList();

        var totalTransactions = transactions.Count;
        var totalHeads = transactions.Sum(x => x.NumberOfHeads);
        var totalCollected = transactions.Sum(x => x.RatePerHead * x.NumberOfHeads);
        var averagePerTransaction = totalTransactions > 0 ? totalCollected / totalTransactions : 0;

        var summary = new ClientCollectionSummaryDto(
            totalTransactions,
            totalHeads,
            averagePerTransaction,
            totalCollected
        );

        return new ClientProfileDto(
            ownerName,
            totalTransactions,
            totalHeads,
            totalCollected,
            transactionDtos,
            summary
        );
    }
}
