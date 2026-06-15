using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
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
            .AsNoTracking()
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

    public async Task<MobileSlaughterCollectionDto> GetMobileSlaughterCollectionAsync(DateOnly date, CancellationToken ct = default)
    {
        var transactions = await context.SlaughterTransactions
            .Where(x => x.TransactionDate == date)
            .OrderByDescending(x => x.CreatedAt)
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

        // Distinct owner names across all transactions — feeds the mobile "Owner name" picker.
        var knownOwners = (await context.SlaughterTransactions
            .AsNoTracking()
            .Select(x => x.OwnerName)
            .ToListAsync(ct))
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MobileSlaughterCollectionDto(
            date,
            transactions.Count,
            transactions.Sum(t => t.NumberOfHeads),
            transactions.Sum(t => t.TotalAmount),
            FeeRates.SlhHogTotalPerHead,
            FeeRates.SlhLargeTotalPerHead,
            transactions,
            knownOwners);
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

    /// <summary>
    /// Collection history for the slaughterhouse: every month of <paramref name="year"/> (up to the
    /// current month for the current year, all 12 for past years) plus a rolling 5-year summary.
    /// All transactions across the 5-year window are loaded in a single query, then aggregated in
    /// memory so each period row is consistent with the per-month overview figures.
    /// </summary>
    public async Task<SlaughterHistoryDto> GetHistoryAsync(int year, CancellationToken ct = default)
    {
        var today = PhilippineTime.Today;
        var firstYear = year - 4;

        // One query for the whole 5-year window; group in memory (DateOnly math is not translatable).
        var rows = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(x => x.TransactionDate.Year >= firstYear && x.TransactionDate.Year <= year)
            .Select(x => new HistoryRow(
                x.OwnerName,
                x.AnimalType,
                x.CustomAnimalType,
                x.NumberOfHeads,
                x.RatePerHead * x.NumberOfHeads,
                x.ORNumber,
                x.TransactionDate))
            .ToListAsync(ct);

        // Current year: only months that have started. Past years: all 12. Future years: none.
        var maxMonth = year < today.Year ? 12 : year == today.Year ? today.Month : 0;

        var monthly = new List<SlaughterPeriodSummaryDto>();
        for (var m = 1; m <= maxMonth; m++)
        {
            var monthSet = rows.Where(r => r.Date.Year == year && r.Date.Month == m).ToList();
            monthly.Add(SummarizeHistory(new DateOnly(year, m, 1).ToString("MMMM"), year, m, monthSet));
        }

        var yearly = new List<SlaughterPeriodSummaryDto>();
        for (var y = firstYear; y <= year; y++)
        {
            var yearSet = rows.Where(r => r.Date.Year == y).ToList();
            yearly.Add(SummarizeHistory(y.ToString(), y, null, yearSet));
        }

        return new SlaughterHistoryDto(year, monthly, yearly);
    }

    private sealed record HistoryRow(string OwnerName, AnimalType AnimalType, string? CustomAnimalType, int NumberOfHeads, decimal Amount, string? ORNumber, DateOnly Date);

    private static SlaughterPeriodSummaryDto SummarizeHistory(string label, int year, int? month, List<HistoryRow> set)
    {
        int Heads(AnimalType t) => set.Where(r => r.AnimalType == t).Sum(r => r.NumberOfHeads);
        decimal Revenue(AnimalType t) => set.Where(r => r.AnimalType == t).Sum(r => r.Amount);

        // One receipt = one OR number; rows without an OR are counted per owner+date so they are
        // not merged with unrelated records (matches the report's transaction grouping).
        var receipts = set
            .Select(r => !string.IsNullOrEmpty(r.ORNumber) ? "OR:" + r.ORNumber : $"NX:{r.OwnerName}:{r.Date:yyyyMMdd}")
            .Distinct()
            .Count();

        // Break "Other" down by the specific custom animal name (e.g. Goat, Sheep, Chicken).
        var otherAnimals = set
            .Where(r => r.AnimalType == AnimalType.Other)
            .GroupBy(r => string.IsNullOrWhiteSpace(r.CustomAnimalType) ? "Other" : r.CustomAnimalType!.Trim())
            .Select(g => new CustomAnimalTallyDto(g.Key, g.Sum(r => r.NumberOfHeads), g.Sum(r => r.Amount)))
            .OrderByDescending(a => a.Revenue)
            .ToList();

        return new SlaughterPeriodSummaryDto(
            label, year, month,
            set.Count,
            receipts,
            set.Select(r => r.OwnerName).Distinct().Count(),
            set.Sum(r => r.NumberOfHeads),
            set.Sum(r => r.Amount),
            Heads(AnimalType.Hog),
            Heads(AnimalType.Carabao),
            Heads(AnimalType.Cow),
            Heads(AnimalType.Other),
            Revenue(AnimalType.Hog),
            Revenue(AnimalType.Carabao),
            Revenue(AnimalType.Cow),
            Revenue(AnimalType.Other),
            otherAnimals);
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

    public async Task<bool> IsORNumberAvailableForReceiptAsync(string orNumber, string ownerName, DateOnly transactionDate, CancellationToken ct = default)
    {
        // OR numbers are global across modules — an OR used elsewhere can never be reused at SLH.
        if (await context.PaymentRecords.AnyAsync(x => x.ORNumber == orNumber, ct)) return false;
        if (await context.DailyCollections.AnyAsync(x => x.ORNumber == orNumber, ct)) return false;
        if (await context.TpmAttendances.AnyAsync(x => x.ORNumber == orNumber, ct)) return false;
        if (await context.TrmTrips.AnyAsync(x => x.ORNumber == orNumber, ct)) return false;

        // Within SLH the same OR may repeat only inside one receipt (same owner + same date).
        // Reject if it already belongs to a different owner or a different transaction date.
        var usedByDifferentReceipt = await context.SlaughterTransactions
            .AnyAsync(x => x.ORNumber == orNumber
                        && (x.OwnerName != ownerName || x.TransactionDate != transactionDate), ct);
        return !usedByDifferentReceipt;
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
