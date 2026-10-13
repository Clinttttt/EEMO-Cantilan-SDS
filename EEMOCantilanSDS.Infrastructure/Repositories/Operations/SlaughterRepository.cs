using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Slaughterhouse;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class SlaughterRepository(
    AppDbContext context,
    IFeeRateResolver feeRateResolver,
    IClock clock,
    ISlaughterAnimalLabelProvider animalLabelProvider) : ISlaughterRepository
{
    /// <summary>Test/non-DI convenience, matching the other repositories: reads the real clock and this office's rates.</summary>
    public SlaughterRepository(AppDbContext context) : this(
        context, new FeeRateResolver(context), new SystemClock(), new SlaughterAnimalLabelProvider(context)) { }
    // ── Canonical slaughterhouse collections ───────────────────────────────────────────────────────────────
    // From the Head's enablement date a Collector's slaughterhouse collection is a canonical Collection (SRC) and the legacy transaction
    // writer is closed to Collectors, so those payments never reach SlaughterTransactions. The workspace reads both: the legacy history
    // and the canonical collections (a reversed one is excluded; a replacement stands on its own SRC). One payment lives on one path only.
    private sealed record CanonicalSlaughter(Guid CollectionId, string Src, DateOnly Date, DateTime RecordedAtUtc, string Owner, AnimalType Animal,
        string? Custom, int Heads, decimal Rate, decimal Total)
    {
        public SlaughterTransactionDto ToDto() => new(CollectionId, Owner, Animal, Custom, Heads, Rate, Total, Src, Date) { ReferenceCode = Src };
    }

    private async Task<List<CanonicalSlaughter>> CanonicalSlaughterAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        var reversed = context.CollectionCorrections.AsNoTracking().Where(x => x.FinancialEffectAmount < 0m).Select(x => x.OriginalCollectionId);
        var rows = await (from l in context.CollectionLines.AsNoTracking()
                          join c in context.Collections.AsNoTracking() on l.CollectionId equals c.Id
                          join k in context.RevenueClassifications.AsNoTracking() on l.RevenueClassificationId equals k.Id
                          where k.SemanticCode == RevenueClassificationCodes.Slaughterhouse && l.SourceKind == CollectionSourceKind.GovernedService
                              && c.BusinessDate >= start && c.BusinessDate <= end && !reversed.Contains(c.Id)
                          select new { c.Id, c.ReferenceCode, c.BusinessDate, c.RecordedAtUtc, c.PayerName, l.Amount, l.CalculationSnapshot })
            .ToListAsync(ct);
        return rows.Select(r =>
        {
            string owner = r.PayerName ?? "—"; var animal = AnimalType.Other; string? custom = null; var heads = 1; var rate = r.Amount;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(r.CalculationSnapshot ?? "{}");
                if (doc.RootElement.TryGetProperty("source", out var source))
                {
                    if (source.TryGetProperty("ownerName", out var o) && o.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(o.GetString())) owner = o.GetString()!;
                    if (source.TryGetProperty("animal", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.String && Enum.TryParse<AnimalType>(a.GetString(), true, out var parsed)) animal = parsed;
                    if (source.TryGetProperty("customAnimalName", out var cn) && cn.ValueKind == System.Text.Json.JsonValueKind.String) custom = cn.GetString();
                    if (source.TryGetProperty("heads", out var h) && h.TryGetInt32(out var hc) && hc > 0) heads = hc;
                    if (source.TryGetProperty("ratePerHead", out var rp) && rp.TryGetDecimal(out var rv)) rate = rv;
                }
            }
            catch (System.Text.Json.JsonException) { /* an unreadable snapshot still counts at the posted amount */ }
            return new CanonicalSlaughter(r.Id, r.ReferenceCode, r.BusinessDate, r.RecordedAtUtc, owner, animal, custom, heads, rate, r.Amount);
        }).ToList();
    }

    private static DateOnly MonthEnd(int year, int month) => new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);

    private static SlaughterTransactionDto ToDto(SlaughterTransaction x) => new(
        x.Id, x.OwnerName, x.AnimalType, x.CustomAnimalType, x.NumberOfHeads, x.RatePerHead, x.RatePerHead * x.NumberOfHeads, x.ORNumber, x.TransactionDate,
        new SlaughterFeeComponentsDto(x.SlaughterFee, x.SlaughterPermit, x.AntemortemFee, x.PostmortemFee, x.TableCharge, x.EntranceFee, x.LivestockFee));

    public async Task<IReadOnlyList<SlaughterAnimalRateDto>> GetApprovedCustomAnimalsAsync(CancellationToken ct = default) =>
        await context.SlaughterAnimalRates.AsNoTracking().Where(x => x.IsActive)
            .Select(x => new SlaughterAnimalRateDto(x.Id, x.AnimalName, x.RatePerHead, x.IsActive)).ToListAsync(ct);

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
                x.TransactionDate,
                new SlaughterFeeComponentsDto(x.SlaughterFee, x.SlaughterPermit, x.AntemortemFee, x.PostmortemFee, x.TableCharge, x.EntranceFee, x.LivestockFee)
            ))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SlaughterTransactionDto>> GetTransactionsByYearAsync(int year, CancellationToken ct = default)
    {
        return await context.SlaughterTransactions
            .AsNoTracking()
            .Where(x => x.TransactionDate.Year == year)
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
                x.TransactionDate,
                new SlaughterFeeComponentsDto(x.SlaughterFee, x.SlaughterPermit, x.AntemortemFee, x.PostmortemFee, x.TableCharge, x.EntranceFee, x.LivestockFee)
            ))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SlaughterTransactionDto>> GetUnreceiptedTransactionsAllTimeAsync(CancellationToken ct = default)
    {
        return await context.SlaughterTransactions
            .AsNoTracking()
            .Where(x => x.ORNumber == null || x.ORNumber == "")
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
                x.TransactionDate,
                new SlaughterFeeComponentsDto(x.SlaughterFee, x.SlaughterPermit, x.AntemortemFee, x.PostmortemFee, x.TableCharge, x.EntranceFee, x.LivestockFee)
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
                x.TransactionDate,
                new SlaughterFeeComponentsDto(x.SlaughterFee, x.SlaughterPermit, x.AntemortemFee, x.PostmortemFee, x.TableCharge, x.EntranceFee, x.LivestockFee)
            ))
            .ToListAsync(ct);

        // Distinct owner names across all transactions — feeds the mobile "Owner name" picker.
        var knownOwners = (await context.SlaughterTransactions
            .AsNoTracking()
            .Select(x => x.OwnerName)
            .ToListAsync(ct))
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(PersonName.Canonical)
            .Distinct(PersonName.Comparer)
            .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // The rates the COLLECTOR's own office charges, as of the day being collected.
        //
        // These were the reference municipality's constants, handed to every LGU: a Carrascal collector recording a hog
        // was shown Cantilan's ₱250 while its own ordinance says ₱400. The stored amount was safe — the recording
        // handler resolves the rate itself and refuses one the office has not stated — but the figure on the collector's
        // screen, and the total they reconcile at the end of the day, were another municipality's.
        //
        // Resolve, not ResolveOrNull: the field is a plain decimal on a payload that devices already in the field
        // deserialize, so an office that has stated nothing sends 0 rather than null. Zero shows as nothing to charge and
        // the recording handler refuses it, which is the honest answer; hiding an unpriced animal on the device is
        // recorded as the remaining mobile work.
        var rates = await feeRateResolver.GetSnapshotAsync(ct);
        var labels = await animalLabelProvider.GetAsync(ct);
        return new MobileSlaughterCollectionDto(
            date,
            transactions.Count,
            transactions.Sum(t => t.NumberOfHeads),
            transactions.Sum(t => t.TotalAmount),
            rates.Resolve(FeeRateKey.SlhHogPerHead, date),
            rates.Resolve(FeeRateKey.SlhLargePerHead, date),
            transactions,
            knownOwners,
            new SlaughterAnimalLabelsDto(labels.Hog, labels.Carabao, labels.Cow),
            await GetApprovedCustomAnimalsAsync(ct));
    }

    public async Task<IReadOnlyList<OwnerTransactionGroupDto>> GetGroupedTransactionsByMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var legacy = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(x => x.TransactionDate.Year == year && x.TransactionDate.Month == month)
            .ToListAsync(ct);
        var canonical = await CanonicalSlaughterAsync(new DateOnly(year, month, 1), MonthEnd(year, month), ct);

        var allTransactions = legacy.Select(x => (Dto: ToDto(x), Created: x.CreatedAt))
            .Concat(canonical.Select(x => (Dto: x.ToDto(), Created: x.RecordedAtUtc)))
            .OrderByDescending(x => x.Dto.TransactionDate).ThenByDescending(x => x.Created).ThenBy(x => x.Dto.OwnerName)
            .ToList();

        // Grouped by person: one client is one row in the list, whatever capitalisation each entry happened to use.
        // The key keeps a typed name so the office still reads the spelling it entered.
        var grouped = allTransactions
            .GroupBy(x => PersonName.Canonical(x.Dto.OwnerName), PersonName.Comparer)
            .Select(ownerGroup =>
            {
                // Group by OR number first (a canonical collection's own reference stands in for it)
                var orGroups = ownerGroup.GroupBy(x => x.Dto.ORNumber).ToList();

                // Get the latest OR number group (most recent transaction)
                var latestORGroup = orGroups
                    .OrderByDescending(g => g.Max(t => t.Dto.TransactionDate))
                    .ThenByDescending(g => g.Max(t => t.Created))
                    .First();

                var latestDate = latestORGroup.Max(x => x.Dto.TransactionDate);

                // Get ALL transactions for this OR number (not just one)
                var latestTransactions = latestORGroup
                    .OrderBy(x => x.Dto.AnimalType)
                    .ThenBy(x => x.Dto.CustomAnimalType)
                    .Select(x => x.Dto)
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
        // Matched by person, not by exact spelling: a client's month must total everything they paid, however the name
        // happened to be capitalised on each entry. Trim/lower is what PostgreSQL can do; stored names are canonical on write.
        var ownerKey = PersonName.MatchKey(ownerName);
        var transactions = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(x => x.OwnerName.Trim().ToLower() == ownerKey && x.TransactionDate.Year == year && x.TransactionDate.Month == month)
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
        var canonicalOwn = (await CanonicalSlaughterAsync(new DateOnly(year, month, 1), MonthEnd(year, month), ct))
            .Where(x => PersonName.MatchKey(x.Owner) == ownerKey).ToList();
        var combined = transactions.Select(x => (Dto: ToDto(x), Created: x.CreatedAt))
            .Concat(canonicalOwn.Select(x => (Dto: x.ToDto(), Created: x.RecordedAtUtc))).ToList();

        // Group by OR number (not by date)
        var orGroups = combined
            .GroupBy(x => x.Dto.ORNumber)
            .Select(g => new
            {
                ORNumber = g.Key,
                LatestDate = g.Max(t => t.Dto.TransactionDate),
                LatestCreated = g.Max(t => t.Created),
                Transactions = g.Select(t => t.Dto).ToList()
            })
            .OrderByDescending(g => g.LatestDate)
            .ThenByDescending(g => g.LatestCreated)
            .Select(g => new TransactionDateGroupDto(
                g.LatestDate,
                g.ORNumber,
                g.Transactions
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
        var canonical = await CanonicalSlaughterAsync(new DateOnly(year, month, 1), MonthEnd(year, month), ct);

        return new SlaughterOverviewDto(
            transactions.Count + canonical.Count,
            transactions.Sum(x => x.NumberOfHeads) + canonical.Sum(x => x.Heads),
            transactions.Sum(x => x.RatePerHead * x.NumberOfHeads) + canonical.Sum(x => x.Total),
            transactions.Where(x => x.AnimalType == AnimalType.Hog).Sum(x => x.NumberOfHeads) + canonical.Where(x => x.Animal == AnimalType.Hog).Sum(x => x.Heads),
            transactions.Where(x => x.AnimalType == AnimalType.Carabao).Sum(x => x.NumberOfHeads) + canonical.Where(x => x.Animal == AnimalType.Carabao).Sum(x => x.Heads),
            transactions.Where(x => x.AnimalType == AnimalType.Cow).Sum(x => x.NumberOfHeads) + canonical.Where(x => x.Animal == AnimalType.Cow).Sum(x => x.Heads),
            transactions.Where(x => x.AnimalType == AnimalType.Other).Sum(x => x.NumberOfHeads) + canonical.Where(x => x.Animal == AnimalType.Other).Sum(x => x.Heads)
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
        var today = clock.PhilippineToday;
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
        rows.AddRange((await CanonicalSlaughterAsync(new DateOnly(firstYear, 1, 1), new DateOnly(year, 12, 31), ct))
            .Select(x => new HistoryRow(x.Owner, x.Animal, x.Custom, x.Heads, x.Total, x.Src, x.Date)));

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
            .GroupBy(r => SlaughterAnimalNames.Normalize(
                string.IsNullOrWhiteSpace(r.CustomAnimalType) ? "Other" : r.CustomAnimalType))
            .Select(g => new CustomAnimalTallyDto(
                string.IsNullOrWhiteSpace(g.First().CustomAnimalType) ? "Other" : g.First().CustomAnimalType!.Trim(),
                g.Sum(r => r.NumberOfHeads),
                g.Sum(r => r.Amount)))
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


    public async Task<bool> IsORNumberAvailableForReceiptAsync(string orNumber, string ownerName, DateOnly transactionDate, CancellationToken ct = default)
    {
        // Unique across all modules, except the same OR may recur within one SLH receipt (same owner + date).
        return await OrNumberRegistry.IsAvailableAsync(context, orNumber, ct, allowSlaughterReceipt: (ownerName, transactionDate));
    }

    public async Task<IReadOnlyList<SlaughterTransaction>> GetTransactionsByOwnerDateORAsync(string ownerName, DateOnly date, string orNumber, CancellationToken ct = default)
    {
        var ownerKey = PersonName.MatchKey(ownerName);
        return await context.SlaughterTransactions
            .Where(x => x.OwnerName.Trim().ToLower() == ownerKey && x.TransactionDate == date && x.ORNumber == orNumber)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SlaughterTransaction>> GetUnreceiptedByOwnerDateAsync(string ownerName, DateOnly transactionDate, CancellationToken ct = default)
    {
        var ownerKey = PersonName.MatchKey(ownerName);
        return await context.SlaughterTransactions
            .Where(x => x.OwnerName.Trim().ToLower() == ownerKey && x.TransactionDate == transactionDate
                     && (x.ORNumber == null || x.ORNumber == ""))
            .ToListAsync(ct);
    }

    public Task RemoveAsync(SlaughterTransaction transaction, CancellationToken ct = default)
    {
        context.SlaughterTransactions.Remove(transaction);
        return Task.CompletedTask;
    }

    public async Task<ClientProfileDto?> GetClientProfileAsync(string ownerName, CancellationToken ct = default)
    {
        var ownerKey = PersonName.MatchKey(ownerName);
        var transactions = await context.SlaughterTransactions
            .AsNoTracking()
            .Where(x => x.OwnerName.Trim().ToLower() == ownerKey)
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

        if (!transactions.Any())
            return null;

        var labels = await animalLabelProvider.GetAsync(ct);
        var transactionDtos = transactions.Select(x => new ClientTransactionDto(
            x.TransactionDate,
            x.AnimalType == AnimalType.Other ? x.CustomAnimalType ?? "Other" : labels.For(x.AnimalType),
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
