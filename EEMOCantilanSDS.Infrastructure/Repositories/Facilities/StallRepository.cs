using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Extensions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class StallRepository(AppDbContext context, IFeeRateResolver feeRateResolver) : IStallRepository
{
    // Test/non-DI convenience: resolves fees from the context (empty rate table => ordinance constants).
    public StallRepository(AppDbContext context) : this(context, new FeeRateResolver(context)) { }

    /// <summary>
    /// Occupied stalls whose active contract is expired or expiring within <paramref name="withinMonths"/>.
    /// Expiry (= effectivity + duration years) is a domain-computed value, so the active contracts are
    /// projected then filtered in memory; expired rows sort first, then by nearest expiry.
    /// </summary>
    public async Task<IReadOnlyList<ContractAttentionDto>> GetContractAttentionAsync(int withinMonths, CancellationToken ct)
        => await GetContractAttentionAsOfCoreAsync(PhilippineTime.Today, withinMonths, ct);

    public async Task<IReadOnlyList<ContractAttentionDto>> GetContractAttentionAsOfAsync(int year, int month, int withinMonths, CancellationToken ct)
    {
        // Snapshot reference = the LAST day of the requested period.
        var asOf = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        return await GetContractAttentionAsOfCoreAsync(asOf, withinMonths, ct);
    }

    private async Task<IReadOnlyList<ContractAttentionDto>> GetContractAttentionAsOfCoreAsync(DateOnly asOf, int withinMonths, CancellationToken ct)
    {
        var horizon = asOf.AddMonths(withinMonths);

        var rows = await context.Stalls
            .AsNoTracking()
            .Where(s => s.Status == StallStatus.Active && s.Contracts.Any(c => c.IsActive))
            .Select(s => new
            {
                s.Id,
                s.StallNo,
                Code = s.Facility!.Code,
                Contract = s.Contracts
                    .Where(c => c.IsActive)
                    .OrderByDescending(c => c.EffectivityDate)
                    .Select(c => new { c.ActualOccupant, c.EffectivityDate, c.DurationYears })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var attention = new List<ContractAttentionDto>();
        foreach (var s in rows)
        {
            if (s.Contract is null) continue;
            var expiry = s.Contract.EffectivityDate.AddYears(s.Contract.DurationYears);
            var expired = asOf > expiry;
            var expiringSoon = !expired && expiry <= horizon;
            if (!expired && !expiringSoon) continue;

            attention.Add(new ContractAttentionDto(
                s.Id,
                s.Code,
                s.StallNo,
                string.IsNullOrWhiteSpace(s.Contract.ActualOccupant) ? string.Empty : s.Contract.ActualOccupant,
                s.Contract.EffectivityDate,
                expiry,
                expired));
        }

        return attention
            .OrderByDescending(a => a.IsExpired)
            .ThenBy(a => a.ExpiryDate)
            .ToList();
    }

    public async Task<MobileNpmCollectionDto> GetMobileNpmCollectionAsync(int year, int month, DateOnly collectionDate, CancellationToken ct)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var effectiveEnd = GetEffectiveCollectionEnd(monthStart, monthEnd, collectionDate);

        // Resolve the municipality's NPM rates as of the collection date (falls back to the ordinance
        // constants, so Cantilan's mobile figures are unchanged).
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var npmDailyRate = rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, collectionDate);
        var npmFishRate = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, collectionDate);

        var stalls = await context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts)
            .Include(s => s.DailyCollections.Where(d =>
                d.CollectionDate >= monthStart &&
                d.CollectionDate <= monthEnd))
            .Where(s =>
                s.Facility!.Code == FacilityCode.NPM &&
                s.Status == StallStatus.Active &&
                (s.Section.HasValue || s.CustomSectionName != null))
            .OrderBy(s => s.Section)
            .ThenBy(s => s.CustomSectionName)
            .ThenBy(s => s.StallNo)
            .ToListAsync(ct);

        // Eligibility: only stalls whose active contract actually covers this collection month. Excludes
        // expired (active-but-lapsed) contracts and stalls with no covering contract — IsActive alone is
        // not enough, since it is a manual flag that does not reflect whether the term has lapsed.
        stalls = stalls.Where(s => s.Contracts.Any(c => c.OverlapsPeriod(monthStart, effectiveEnd))).ToList();

        // The tenant's own market-section display labels (e.g. "Gulayan"), resolved once for the mobile
        // DTO's SectionName. The MarketSection enum stays the logical key; only this mobile display string
        // becomes tenant-aware. Falls back to the canonical section name when no custom label is set.
        var npmFacility = await context.Facilities.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Code == FacilityCode.NPM, ct);
        // A canonical section resolves to its tenant label (falling back to the canonical name); a custom
        // section (Section null) shows its per-stall CustomSectionName.
        string SectionDisplay(Stall s)
            => s.Section is { } sec
                ? (npmFacility?.SectionLabel(sec) ?? GetSectionName(sec))
                : (s.CustomSectionName ?? string.Empty);

        var rows = stalls.Select(s =>
        {
            // Prefer the contract that actually covers this collection month — not merely the latest
            // active one — so a future/expired sibling contract can't drive the occupant or the day math.
            var contract = s.Contracts
                .Where(c => c.OverlapsPeriod(monthStart, effectiveEnd))
                .OrderByDescending(c => c.EffectivityDate)
                .FirstOrDefault();
            var collectableToday = contract is not null && contract.IsCollectableOn(collectionDate);

            var dailyRate = s.DailyRate ?? npmDailyRate;
            var todayCollection = s.DailyCollections.FirstOrDefault(d => d.CollectionDate == collectionDate);
            var paidCollections = s.DailyCollections
                .Where(d => d.IsPaid && d.CollectionDate >= monthStart && d.CollectionDate <= effectiveEnd)
                .ToList();

            var collectableDays = CountCollectableDays(contract?.EffectivityDate, monthStart,
                contract is not null && contract.ExpiryDate < effectiveEnd ? contract.ExpiryDate : effectiveEnd);
            var daysCollected = paidCollections.Count;
            // Excused/absent days are not owed, so they leave the missed-day count.
            var absentDays = s.DailyCollections.Count(d => d.IsAbsent
                && d.CollectionDate >= monthStart && d.CollectionDate <= effectiveEnd);
            var daysMissed = Math.Max(0, collectableDays - daysCollected - absentDays);
            var monthCollectedAmount = paidCollections.Sum(d =>
                d.DailyFee + (d.FishKilos.GetValueOrDefault() * npmFishRate));

            return new MobileNpmStallCollectionDto(
                s.Id,
                s.StallNo,
                string.IsNullOrWhiteSpace(contract?.ActualOccupant) ? "No active occupant" : contract.ActualOccupant,
                contract?.NameOnContract ?? contract?.ActualOccupant ?? string.Empty,
                s.Section,
                SectionDisplay(s),
                s.Status,
                dailyRate,
                todayCollection is not null,
                todayCollection?.IsPaid == true,
                todayCollection?.ORNumber,
                todayCollection?.FishKilos,
                daysCollected,
                daysMissed,
                collectableDays,
                monthCollectedAmount,
                todayCollection?.IsAbsent == true,
                collectableToday);
        }).ToList();

        var collectedToday = rows.Where(r => r.IsCollectedToday).ToList();
        // "Pending today" = a stall whose contract covers TODAY and hasn't been collected/excused yet —
        // not merely one that has an unpaid day earlier in the month.
        var pendingToday = rows.Where(r => r.IsCollectableToday && !r.IsCollectedToday && !r.IsAbsentToday).ToList();

        return new MobileNpmCollectionDto(
            year,
            month,
            collectionDate,
            rows.Count,
            collectedToday.Count,
            pendingToday.Count,
            collectedToday.Sum(r => r.DailyRate + (r.FishKilosToday.GetValueOrDefault() * npmFishRate)),
            pendingToday.Sum(r => r.DailyRate),
            rows.Sum(r => r.DaysCollected),
            rows.Sum(r => r.DaysMissed),
            rows);
    }

    public async Task<MobileMonthlyCollectionDto> GetMobileMonthlyCollectionAsync(
        FacilityCode facilityCode, int year, int month, DateOnly collectionDate, CancellationToken ct)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var stalls = await context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts)
            .Include(s => s.PaymentRecords.Where(p =>
                p.BillingYear == year &&
                p.BillingMonth == month))
            .Where(s =>
                s.Facility!.Code == facilityCode &&
                s.Status == StallStatus.Active &&
                s.Contracts.Any(c => c.IsActive))
            .OrderBy(s => s.StallNo)
            .ToListAsync(ct);

        // Eligibility: only stalls whose active contract overlaps the billing month. Excludes expired
        // (active-but-lapsed) contracts — IsActive alone does not reflect whether the term has lapsed.
        stalls = stalls.Where(s => s.Contracts.Any(c => c.OverlapsPeriod(monthStart, monthEnd))).ToList();

        // Which of this month's records were settled online (so the collector sees "paid online" and
        // doesn't collect again). A record is online-settled if it has a Paid/Completed transaction.
        var monthRecordIds = stalls
            .Select(s => s.PaymentRecords.FirstOrDefault())
            .Where(r => r is not null)
            .Select(r => r!.Id)
            .ToList();

        var onlineTxns = monthRecordIds.Count == 0
            ? new List<(Guid PaymentRecordId, Guid Id, OnlinePaymentStatus Status)>()
            : (await context.OnlinePaymentTransactions
                .AsNoTracking()
                .Where(t => t.PaymentRecordId != null
                    && monthRecordIds.Contains(t.PaymentRecordId.Value)
                    && (t.Status == OnlinePaymentStatus.Paid || t.Status == OnlinePaymentStatus.Completed))
                .Select(t => new { PaymentRecordId = t.PaymentRecordId!.Value, t.Id, t.Status })
                .ToListAsync(ct))
                .Select(t => (t.PaymentRecordId, t.Id, t.Status))
                .ToList();

        // Record ids that were settled online (for the "Online" chip)…
        var onlinePaidRecordIds = onlineTxns.Select(t => t.PaymentRecordId).ToHashSet();
        // …and the still-Paid (not yet OR-completed) transaction per record (for in-field OR encoding).
        var awaitingOrTxnByRecord = onlineTxns
            .Where(t => t.Status == OnlinePaymentStatus.Paid)
            .GroupBy(t => t.PaymentRecordId)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var rows = stalls.Select(s =>
        {
            var contract = s.Contracts
                .Where(c => c.OverlapsPeriod(monthStart, monthEnd))
                .OrderByDescending(c => c.EffectivityDate)
                .FirstOrDefault();

            var record = s.PaymentRecords.FirstOrDefault();
            var status = record?.Status ?? PaymentStatus.Unpaid;
            // Monthly-rental facilities carry no utilities, so the bill is the flat monthly rate.
            var amountPaid = record?.AmountPaid ?? 0m;
            var balance = record is not null ? record.BalanceDue : s.MonthlyRate;

            var paidOnline = record is not null && onlinePaidRecordIds.Contains(record.Id);
            // Paid online but the staff have not yet encoded the Official Receipt (no OR on the record).
            var awaitingOr = paidOnline && string.IsNullOrWhiteSpace(record!.ORNumber);
            Guid? onlineTxnId = awaitingOr && awaitingOrTxnByRecord.TryGetValue(record!.Id, out var txnId)
                ? txnId
                : null;

            return new MobileMonthlyStallCollectionDto(
                s.Id,
                s.StallNo,
                string.IsNullOrWhiteSpace(contract?.ActualOccupant) ? "No active occupant" : contract.ActualOccupant,
                contract?.NameOnContract ?? contract?.ActualOccupant ?? string.Empty,
                GetMonthlyAreaLabel(s),
                s.MonthlyRate,
                status,
                amountPaid,
                balance,
                record?.ORNumber,
                record is not null,
                paidOnline,
                awaitingOr,
                onlineTxnId);
        }).ToList();

        // Facility display name from the seeded Facility record (single source of truth).
        var facilityName = await context.Facilities
            .AsNoTracking()
            .Where(f => f.Code == facilityCode)
            .Select(f => f.Name)
            .FirstOrDefaultAsync(ct) ?? facilityCode.ToString();

        return new MobileMonthlyCollectionDto(
            facilityCode,
            facilityName,
            year,
            month,
            collectionDate,
            rows.Count,
            rows.Count(r => r.Status == PaymentStatus.Paid),
            rows.Count(r => r.Status == PaymentStatus.Partial),
            rows.Count(r => r.Status == PaymentStatus.Unpaid),
            rows.Sum(r => r.AmountPaid),
            rows.Sum(r => r.Balance),
            rows);
    }

    private static string GetMonthlyAreaLabel(Stall s)
    {
        if (s.AreaLocation.HasValue)
            return s.AreaLocation.Value.ToString();
        if (s.Section.HasValue)
            return GetSectionName(s.Section);
        // No generic stall-type chip ("Permanent"/"Transient") — it adds noise on the collector card.
        return string.Empty;
    }

    public async Task<StallHoldersListDto> GetStallHoldersListAsync(FacilityCode facilityCode, MarketSection? section, string? searchTerm, CancellationToken ct)
    {
        var query = context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts)
            // The stallholder roster lists CURRENT holders only — closed accounts are excluded entirely
            // (they still appear in the transaction/collection history for transparency, just not here).
            .Where(s => s.Facility!.Code == facilityCode && s.Status != StallStatus.Closed);

        if (section.HasValue)
            query = query.Where(s => s.Section == section.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            query = query.Where(s =>
                s.StallNo.ToLower().Contains(term) ||
                s.Contracts.Any(c =>
                    c.ActualOccupant.ToLower().Contains(term) ||
                    (c.NameOnContract ?? "").ToLower().Contains(term)));
        }

        var stalls = await query
            .OrderBy(s => s.Section)
            .ThenBy(s => s.StallNo)
            .ToListAsync(ct);

        // Current holders only: also drop EXPIRED accounts — an (active) stall whose contract term has
        // already lapsed — as well as Closed (frozen) ones. Uses the same central rule (Stall.IsContractExpired)
        // as the closed-accounts register and the remove-inactive guard, so they can never diverge.
        // Expired/closed rows still appear in the transaction/collection history — just not on this roster.
        stalls = stalls.Where(s => s.Status != StallStatus.Closed && !s.IsContractExpired()).ToList();

        // ── The monetary columns must state what the stall is actually billed ──
        // A daily-collected facility (NPM) has no monthly contract rate. The official form's "Monthly
        // Rentals" column is the monthly EQUIVALENT of the ordinance daily fee (daily × 30), which is the
        // same figure the NPM report register prints. Previously this projection printed the stored
        // Stall.MonthlyRate — a number typed by whoever registered the stall — which only ever coincides
        // with the ordinance for a ₱30 municipality, so every other LGU was shown Cantilan's ₱900 next to
        // its own ₱40/day rate.
        //
        // The rate is resolved through Stall.ResolveDailyFee, the SAME rule billing and settlement use, so
        // a per-LGU CUSTOM section keeps its own rate and the roster can never disagree with the ledger.
        // Cantilan resolves to ₱30 × 30 = ₱900, exactly what it showed before.
        var isDailyBilled = facilityCode == FacilityCode.NPM;
        var npmDailyRate = isDailyBilled
            ? (await feeRateResolver.GetSnapshotAsync(ct))
                .Resolve(FeeRateKey.NpmDailyStall, DateOnly.FromDateTime(PhilippineTime.Now))
            : 0m;

        decimal MonthlyOf(Stall s) => isDailyBilled
            ? s.ResolveDailyFee(npmDailyRate) * DomainRules.DailyBilledMonthDays
            : s.MonthlyRate;

        // The tenant's own market-section display labels (e.g. "Gulayan") — resolved once. The MarketSection
        // enum stays the logical key; only the SHOWN label becomes tenant-aware, falling back to the canonical
        // name ("Vegetable Area"/…) when no custom label is set (so Cantilan is unchanged).
        var facility = await context.Facilities.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Code == facilityCode, ct);

        // Group stalls by section (NPM has sections, others don't)
        var sectionsWithSection = stalls
            .Where(s => s.Section.HasValue)
            .GroupBy(s => s.Section!.Value)
            .Select(g => new StallHoldersSectionDto
            {
                SectionName = facility?.SectionLabel(g.Key) ?? GetSectionName(g.Key),
                StallCount = g.Count(),
                Rows = g.Select((s, idx) =>
                {
                    var contract = s.Contracts.FirstOrDefault(c => c.IsActive);
                    var durationYears = contract?.DurationYears ?? 0;
                    return new StallHolderRowDto
                    {
                        RowNumber = idx + 1,
                        ActualOccupant = contract?.ActualOccupant ?? "",
                        NameOnContract = contract?.NameOnContract ?? "",
                        StallNo = s.StallNo,
                        EffectivityDate = contract?.EffectivityDate ?? default,
                        DurationYears = durationYears,
                        AreaSqm = s.AreaSqm,
                        MonthlyRentalRate = MonthlyOf(s),
                        ActualMonthlyRental = MonthlyOf(s),
                        WholeYearRental = MonthlyOf(s) * 12,
                        FishFeeTotal = null,   // List of Stallholders is base rental only — no fish/elec/water
                        IsClosed = s.Status == StallStatus.Closed,
                        // Space-only and extension rows print "No contract" with the contract columns left blank.
                        Arrangement = contract?.Arrangement ?? OccupancyArrangement.SignedContract
                    };
                }).ToList(),
                SectionMonthlyTotal = g.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
                SectionActualMonthly = g.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
                SectionWholeYearTotal = g.Where(s => s.Status == StallStatus.Active).Sum(s => MonthlyOf(s) * 12),
                SectionFishFeeTotal = 0   // base rental only — additional fees (fish/elec/water) are not part of this list
            }).ToList();

        // NPM per-LGU CUSTOM sections: Section is null but a CustomSectionName is set. Group each custom
        // section into its own named group (mirrors the canonical grouping) so the roster shows and counts
        // them, instead of lumping them into "All Stalls".
        foreach (var group in stalls
            .Where(s => !s.Section.HasValue && !string.IsNullOrWhiteSpace(s.CustomSectionName))
            .GroupBy(s => s.CustomSectionName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var groupStalls = group.ToList();
            sectionsWithSection.Add(new StallHoldersSectionDto
            {
                SectionName = group.Key,
                StallCount = groupStalls.Count,
                Rows = groupStalls.Select((s, idx) =>
                {
                    var contract = s.Contracts.FirstOrDefault(c => c.IsActive);
                    return new StallHolderRowDto
                    {
                        RowNumber = idx + 1,
                        ActualOccupant = contract?.ActualOccupant ?? "",
                        NameOnContract = contract?.NameOnContract ?? "",
                        StallNo = s.StallNo,
                        EffectivityDate = contract?.EffectivityDate ?? default,
                        DurationYears = contract?.DurationYears ?? 0,
                        AreaSqm = s.AreaSqm,
                        MonthlyRentalRate = MonthlyOf(s),
                        ActualMonthlyRental = MonthlyOf(s),
                        WholeYearRental = MonthlyOf(s) * 12,
                        FishFeeTotal = null,
                        IsClosed = s.Status == StallStatus.Closed,
                        Arrangement = contract?.Arrangement ?? OccupancyArrangement.SignedContract
                    };
                }).ToList(),
                SectionMonthlyTotal = groupStalls.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
                SectionActualMonthly = groupStalls.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
                SectionWholeYearTotal = groupStalls.Where(s => s.Status == StallStatus.Active).Sum(s => MonthlyOf(s) * 12),
                SectionFishFeeTotal = 0
            });
        }

        // Handle truly section-less stalls (TCC, NCC, BBQ, ICE, SLH — no Section AND no custom section).
        var stallsWithoutSection = stalls.Where(s => !s.Section.HasValue && string.IsNullOrWhiteSpace(s.CustomSectionName)).ToList();
        if (stallsWithoutSection.Any())
        {
            sectionsWithSection.Add(new StallHoldersSectionDto
            {
                SectionName = "All Stalls",
                StallCount = stallsWithoutSection.Count,
                Rows = stallsWithoutSection.Select((s, idx) =>
                {
                    var contract = s.Contracts.FirstOrDefault(c => c.IsActive);
                    var durationYears = contract?.DurationYears ?? 0;
                    return new StallHolderRowDto
                    {
                        RowNumber = idx + 1,
                        ActualOccupant = contract?.ActualOccupant ?? "",
                        NameOnContract = contract?.NameOnContract ?? "",
                        StallNo = s.StallNo,
                        EffectivityDate = contract?.EffectivityDate ?? default,
                        DurationYears = durationYears,
                        AreaSqm = s.AreaSqm,
                        MonthlyRentalRate = MonthlyOf(s),
                        ActualMonthlyRental = MonthlyOf(s),
                        WholeYearRental = MonthlyOf(s) * 12,
                        FishFeeTotal = null,
                        IsClosed = s.Status == StallStatus.Closed,
                        AreaLocation = s.AreaLocation?.ToString(),
                        Arrangement = contract?.Arrangement ?? OccupancyArrangement.SignedContract
                    };
                }).ToList(),
                SectionMonthlyTotal = stallsWithoutSection.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
                SectionActualMonthly = stallsWithoutSection.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
                SectionWholeYearTotal = stallsWithoutSection.Where(s => s.Status == StallStatus.Active).Sum(s => MonthlyOf(s) * 12),
                SectionFishFeeTotal = 0
            });
        }

        return new StallHoldersListDto
        {
            TotalStalls = stalls.Count,
            VegetableCount = stalls.Count(s => s.Section == MarketSection.VegetableArea),
            FishCount = stalls.Count(s => s.Section == MarketSection.FishSection),
            MeatCount = stalls.Count(s => s.Section == MarketSection.MeatSection),
            Sections = sectionsWithSection,
            GrandTotalActiveStalls = stalls.Count(s => s.Status == StallStatus.Active),
            GrandTotalMonthlyRate = stalls.Where(s => s.Status == StallStatus.Active).Sum(MonthlyOf),
            GrandTotalWholeYearRental = stalls.Where(s => s.Status == StallStatus.Active).Sum(s => MonthlyOf(s) * 12)
        };
    }

    private static DateOnly GetEffectiveCollectionEnd(DateOnly monthStart, DateOnly monthEnd, DateOnly collectionDate)
    {
        if (collectionDate < monthStart)
            return monthStart.AddDays(-1);

        if (collectionDate > monthEnd)
            return monthEnd;

        return collectionDate;
    }

    private static int CountCollectableDays(DateOnly? contractStart, DateOnly monthStart, DateOnly effectiveEnd)
    {
        if (effectiveEnd < monthStart)
            return 0;

        var start = contractStart.HasValue && contractStart.Value > monthStart
            ? contractStart.Value
            : monthStart;

        if (start > effectiveEnd)
            return 0;

        return effectiveEnd.DayNumber - start.DayNumber + 1;
    }

    private static string GetSectionName(MarketSection? section) => section switch
    {
        MarketSection.VegetableArea => "Vegetable Area",
        MarketSection.FishSection => "Fish Area",
        MarketSection.MeatSection => "Meat Area",
        _ => "Unassigned Section"
    };

    public async Task<CursorPagedResult<StallDto>> GetStallsByFacilityPaginatedAsync(FacilityCode facilityCode, MarketSection? section, DateTime? cursor, int pageSize, CancellationToken ct)
    {
        var query = context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts)
            .Where(s => s.Facility!.Code == facilityCode);

        if (section.HasValue)
            query = query.Where(s => s.Section == section.Value);

        if (cursor.HasValue)
            query = query.Where(s => s.CreatedAt < cursor.Value);

        query = query.OrderByDescending(s => s.CreatedAt);

        // Materialise first so FirstOrDefault runs client-side — avoids EF correlated
        // subquery column-type mismatch (integer vs string) on PostgreSQL.
        var pagedResult = await query
            .ToCursorPagedResultAsync(pageSize, s => s.CreatedAt, ct);

        return new CursorPagedResult<StallDto>
        {
            Items = pagedResult.Items.Select(s =>
            {
                var activeContract = s.Contracts.FirstOrDefault(c => c.IsActive);
                return new StallDto(
                    s.Id,
                    s.StallNo,
                    s.Status,
                    activeContract?.ActualOccupant,
                    activeContract?.NameOnContract,
                    s.AreaSqm,
                    activeContract?.EffectivityDate.ToDateTime(TimeOnly.MinValue),
                    s.MonthlyRate,
                    s.DailyRate,
                    activeContract?.ORNumber,
                    s.Section,
                    s.AreaLocation,
                    s.AreaNote,
                    s.Remarks,
                    activeContract?.DurationYears ?? 0,
                    s.CustomSectionName,
                    s.Fees.HasFlag(ApplicableFees.Electricity),
                    s.Fees.HasFlag(ApplicableFees.Water)
                );
            }).ToList(),
            NextCursor = pagedResult.NextCursor,
            HasMore = pagedResult.HasMore
        };
    }
    public async Task<IReadOnlyList<StallDto>> GetStallsByFacilityAsync(FacilityCode facilityCode, MarketSection? section, CancellationToken ct)
    {
        var query = context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts)
            .Where(s => s.Facility!.Code == facilityCode);

        if (section.HasValue)
            query = query.Where(s => s.Section == section.Value);

        var stalls = await query.ToListAsync(ct);

        return stalls.Select(s =>
        {
            var activeContract = s.Contracts.FirstOrDefault(c => c.IsActive);

            return new StallDto(
                s.Id,
                s.StallNo,
                s.Status,
                activeContract?.ActualOccupant,
                activeContract?.NameOnContract,
                s.AreaSqm,
                activeContract?.EffectivityDate.ToDateTime(TimeOnly.MinValue),
                s.MonthlyRate,
                s.DailyRate,
                activeContract?.ORNumber,
                s.Section,
                s.AreaLocation,
                s.AreaNote,
                s.Remarks,
                activeContract?.DurationYears ?? 0,
                s.CustomSectionName,
                s.Fees.HasFlag(ApplicableFees.Electricity),
                s.Fees.HasFlag(ApplicableFees.Water)
            );
        }).ToList();
    }

    public async Task<Dictionary<MarketSection, StallSummaryDto>> GetSectionSummariesAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct)
    {
        var stalls = await context.Stalls
            .AsNoTracking()
            .Include(s => s.PaymentRecords.Where(p => p.BillingYear == year && p.BillingMonth == month))
            .Where(s => s.Facility!.Code == facilityCode && s.Section.HasValue)
            .ToListAsync(ct);

        return stalls
            .GroupBy(s => s.Section!.Value)
            .ToDictionary(
                g => g.Key,
                g => new StallSummaryDto(
                    g.Count(),
                    g.Count(s => s.PaymentRecords.Any(p => 
                        p.BillingYear == year && 
                        p.BillingMonth == month && 
                        p.Status == PaymentStatus.Unpaid))
                )
            );
    }

    public async Task<Stall?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return await context.Stalls
            .Include(s => s.Facility)
            .Include(s => s.Contracts)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<FacilityCode?> GetFacilityCodeByStallIdAsync(Guid stallId, CancellationToken ct)
    {
        return await context.Stalls
            .Where(s => s.Id == stallId)
            .Select(s => (FacilityCode?)s.Facility!.Code)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Inactive accounts register. CLOSED = Status==Closed (frozen by an admin). EXPIRED = active stall
    /// whose contract term has lapsed (ExpiryDate &lt; today). Lifetime collected counts ALL money ever
    /// received (closure/expiry never erases history). Uncollected = arrears that accrued from contract
    /// effectivity up to the end point (close date for closed, contract expiry for expired), with
    /// excused months / absent days owing nothing — the same billing rules the reports use, contract-
    /// gated (the stall WAS operating then) and bounded to the end point so nothing is back/over-billed.
    /// </summary>
    public async Task<IReadOnlyList<ClosedStallAccountDto>> GetClosedStallAccountsAsync(CancellationToken ct)
        => await GetClosedStallAccountsCoreAsync(null, null, ct);

    /// <summary>
    /// The same register, bounded to a period: every figure states what each ended occupancy owed and paid FOR
    /// <paramref name="from"/>–<paramref name="to"/>, and an occupancy that did not exist in that period is not
    /// listed at all. This is what a year or a month view of the follow-up history must state beside a period
    /// heading; the lifetime reading above answers "what is owed in total" and belongs to the cumulative view.
    /// </summary>
    public async Task<IReadOnlyList<ClosedStallAccountDto>> GetClosedStallAccountsForPeriodAsync(
        DateOnly from, DateOnly to, CancellationToken ct)
        => await GetClosedStallAccountsCoreAsync(from, to, ct);

    private async Task<IReadOnlyList<ClosedStallAccountDto>> GetClosedStallAccountsCoreAsync(
        DateOnly? windowStart, DateOnly? windowEnd, CancellationToken ct)
    {
        var today = PhilippineTime.Today;

        // Resolve the municipality's NPM rates as of today (falls back to the ordinance constants, so
        // Cantilan's lifetime/uncollected figures are unchanged).
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var npmDailyRate = rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, today);
        var npmFishRate = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, today);

        // Candidates: every stall that has EVER been let. The register is a record of ended OCCUPANCIES, not of
        // currently-vacant stalls: a stall re-let to a new lessee must still show the previous lessee's closed or
        // expired account, with that lessee's own money — otherwise re-letting a stall erases its history from the
        // office's records. Expiry (= effectivity + duration years) is domain-computed and cannot be translated
        // into SQL, so the stalls are loaded and their occupancies derived in memory. ALL contracts are included,
        // terminated ones too, because a terminated occupancy is exactly what this register is for.
        var everLet = await context.Stalls
            .AsNoTracking()
            .Include(s => s.Facility)
            .Include(s => s.Contracts)
            .Where(s => s.Contracts.Any())
            .ToListAsync(ct);

        // One entry per ended occupancy: terminated, superseded by a new lessee, lapsed, or frozen by closure.
        // The occupancy in force is not an inactive account.
        var occupanciesByStall = everLet.ToDictionary(s => s.Id, s => s.Occupancies(today));

        var accounts = everLet
            .SelectMany(s => occupanciesByStall[s.Id].Select(o => (Stall: s, Occupancy: o)))
            .Where(x => !x.Occupancy.IsCurrent || x.Stall.Status == StallStatus.Closed)
            // A period-scoped read lists only the occupancies that existed in that period: a 2023 view showing an
            // account that began in 2026 states a debt nobody could have owed then.
            .Where(x => windowStart is not { } ws || windowEnd is not { } we
                || (x.Occupancy.Start <= we && ws <= x.Occupancy.End))
            .ToList();

        if (accounts.Count == 0)
            return new List<ClosedStallAccountDto>();

        var stallIds = accounts.Select(x => x.Stall.Id).Distinct().ToList();

        // Batch-load the financial inputs once (no N+1).
        var payments = await context.PaymentRecords.AsNoTracking()
            .Where(p => stallIds.Contains(p.StallId)).ToListAsync(ct);
        var paidDailies = await context.DailyCollections.AsNoTracking()
            .Where(d => stallIds.Contains(d.StallId) && d.IsPaid)
            .Select(d => new { d.StallId, d.CollectionDate, d.DailyFee, d.FishKilos }).ToListAsync(ct);
        var absentDailies = await context.DailyCollections.AsNoTracking()
            .Where(d => stallIds.Contains(d.StallId) && d.IsAbsent)
            .Select(d => new { d.StallId, d.CollectionDate }).ToListAsync(ct);
        var exceptions = await context.StallMonthlyExceptions.AsNoTracking()
            .Where(e => stallIds.Contains(e.StallId))
            .Select(e => new { e.StallId, e.BillingYear, e.BillingMonth }).ToListAsync(ct);
        // Days the market itself was shut. Nothing is owed for them, so charging them here would state a debt the
        // office cannot collect — and the Record-payment dialog, which has always excluded them, would then offer a
        // smaller total than this register. One closure list serves every stall: a closure is facility-wide.
        var closureDates = (await context.NpmMarketClosures.AsNoTracking()
            .Select(c => c.ClosureDate).ToListAsync(ct)).ToHashSet();

        var paidByStall = paidDailies.GroupBy(d => d.StallId).ToDictionary(g => g.Key, g => g.ToList());
        var absentByStall = absentDailies.GroupBy(d => d.StallId).ToDictionary(g => g.Key, g => g.Select(x => x.CollectionDate).ToHashSet());
        var paymentsByStall = payments.GroupBy(p => p.StallId).ToDictionary(g => g.Key, g => g.ToList());
        var excusedByStall = exceptions.GroupBy(e => e.StallId).ToDictionary(g => g.Key, g => g.Select(x => (x.BillingYear, x.BillingMonth)).ToHashSet());

        var result = new List<ClosedStallAccountDto>(accounts.Count);
        foreach (var (stall, occupancy) in accounts)
        {
            var contract = occupancy.Contract;
            var isNpm = stall.Facility?.Code == FacilityCode.NPM;
            var isClosed = stall.Status == StallStatus.Closed;

            var contractExpiry = contract.ExpiryDate;
            // End point of THIS occupancy: the day the lessee actually stopped holding the stall — terminated,
            // superseded by the next lessee, frozen by closure, or the term's end. Bounding every figure below to
            // [occupancy start, end] is what keeps one lessee's money out of another's account on a re-let stall.
            var startDate = occupancy.Start;
            var endDate = occupancy.End;
            // Charges stop at the term's end even if the lessee stayed on afterwards.
            var billableEnd = occupancy.BillableEnd;

            // A period-scoped read narrows all three to the requested window, so every figure on the row is what
            // this occupancy owed and paid FOR that period — never its lifetime total under a period heading.
            if (windowStart is { } wStart && windowEnd is { } wEnd)
            {
                if (startDate < wStart) startDate = wStart;
                if (endDate > wEnd) endDate = wEnd;
                if (billableEnd > wEnd) billableEnd = wEnd;
            }

            var windows = occupanciesByStall[stall.Id];

            // A month's charge is one indivisible obligation, so exactly one occupancy answers for it (the lessee
            // who began latest within it). Without this a mid-month handover billed that month in full to BOTH
            // lessees and credited its payment to both — the register's totals then overstated by a month's rent
            // per handover.
            bool AnswersFor(int billingYear, int billingMonth)
            {
                if (billingMonth is < 1 or > 12) return false;

                var monthStart = new DateOnly(billingYear, billingMonth, 1);
                var monthEnd = new DateOnly(billingYear, billingMonth, DateTime.DaysInMonth(billingYear, billingMonth));

                // Inside the read's own window (a period-scoped read states only that period's months) …
                if (windowStart is { } ws && windowEnd is { } we && (monthEnd < ws || we < monthStart))
                    return false;

                // … and answered for by THIS occupancy, judged on the true occupancy windows rather than the
                // clamped ones, so narrowing the view never moves a month to a different lessee.
                return StallOccupancy.AnsweringForMonth(windows, billingYear, billingMonth)?.Contract.Id == contract.Id;
            }

            var stallPaid = (paidByStall.GetValueOrDefault(stall.Id) ?? new())
                // Daily collections carry the business date they were collected FOR, so they attribute exactly.
                .Where(d => d.CollectionDate >= startDate && d.CollectionDate <= endDate)
                .ToList();
            var stallAbsent = absentByStall.GetValueOrDefault(stall.Id) ?? new();
            var stallPayments = (paymentsByStall.GetValueOrDefault(stall.Id) ?? new())
                // Attributed by the BILLING period, never by the day the money arrived: an arrear settled months
                // after a handover still belongs to the lessee who incurred it.
                .Where(p => AnswersFor(p.BillingYear, p.BillingMonth))
                .ToList();
            var stallExcused = excusedByStall.GetValueOrDefault(stall.Id) ?? new();

            // Lifetime collected = every peso actually received (status-independent). A period-scoped read states
            // what was received FOR that period.
            var lifetimeCollected = isNpm
                ? stallPaid.Sum(d => d.DailyFee + (d.FishKilos.HasValue ? d.FishKilos.Value * npmFishRate : 0m))
                : stallPayments.Sum(p => p.AmountPaid);

            // The rent this occupancy was let at. The stall's own MonthlyRate is the CURRENT figure and is rewritten
            // when the space is re-let or its rate revised, so reading it here would restate a departed lessee's
            // arrears at a rate they never agreed to. Legacy terms that carry no rate fall back to the stall's.
            var occupancyMonthlyRate = contract.MonthlyRentalRate > 0m ? contract.MonthlyRentalRate : stall.MonthlyRate;

            decimal uncollected = 0m;
            if (isNpm)
            {
                // Per calendar month: what that month owed — its collectable days at the month's rate, never more
                // than the month's base rent (₱30 × 30) — less what was actually collected for it. A 31-day month
                // therefore raises an arrear of ₱900, not ₱930: the extra day may be collected, and is revenue, but
                // it is not a debt. Absent days and facility closures owe nothing, so they never count.
                var cursor = new DateOnly(startDate.Year, startDate.Month, 1);
                var lastMonth = new DateOnly(billableEnd.Year, billableEnd.Month, 1);
                while (cursor <= lastMonth)
                {
                    var mStart = cursor > startDate ? cursor : startDate;
                    var mEnd = new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
                    if (mEnd > billableEnd) mEnd = billableEnd;

                    var billableDays = 0;
                    for (var d = mStart; d <= mEnd; d = d.AddDays(1))
                    {
                        if (stallAbsent.Contains(d) || closureDates.Contains(d)) continue;
                        billableDays++;
                    }

                    // The rate in force at the month's end — the base rent it is measured against is that month's.
                    var monthFee = stall.ResolveDailyFee(rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, mEnd));
                    var owed = DomainRules.DailyBilledMonthCharge(monthFee, billableDays);
                    var collected = stallPaid
                        .Where(p => p.CollectionDate >= mStart && p.CollectionDate <= mEnd)
                        .Sum(p => p.DailyFee);

                    uncollected += Math.Max(0m, owed - collected);
                    cursor = cursor.AddMonths(1);
                }
            }
            else
            {
                // Per calendar month this occupancy answered for: a non-Unpaid record's balance, else the full
                // monthly rent. Excused months owe nothing.
                var cursor = new DateOnly(startDate.Year, startDate.Month, 1);
                var endMonth = new DateOnly(billableEnd.Year, billableEnd.Month, 1);
                while (cursor <= endMonth)
                {
                    if (!stallExcused.Contains((cursor.Year, cursor.Month)) && AnswersFor(cursor.Year, cursor.Month))
                    {
                        var rec = stallPayments.FirstOrDefault(p => p.BillingYear == cursor.Year && p.BillingMonth == cursor.Month);
                        uncollected += rec is not null && rec.Status != PaymentStatus.Unpaid
                            ? rec.BalanceDue
                            : occupancyMonthlyRate;
                    }
                    cursor = cursor.AddMonths(1);
                }
            }

            // A closed stall's occupancies are all closed accounts; on an open stall an ended occupancy either
            // lapsed (its term ran out) or was handed over to the next lessee — both read as "expired" on the
            // register, which is what the office calls an account that is no longer current.
            var state = isClosed ? InactiveAccountState.Closed : InactiveAccountState.Expired;

            result.Add(new ClosedStallAccountDto(
                stall.Id,
                state,
                stall.Facility!.Code,
                stall.Facility!.Name,
                stall.StallNo,
                contract.ActualOccupant,
                contract.NameOnContract,
                contract.EffectivityDate,
                contract.DurationYears,
                // A daily-collected stall has no monthly contract rate: state the monthly equivalent of the
                // rate it was actually billed at (the same ResolveDailyFee rule the ledger and the
                // stallholder roster use), not the hand-entered figure stored on the stall — that only
                // matches the ordinance for a ₱30 municipality. A monthly facility states the rent THIS
                // occupancy was let at, which is also the figure the collection dialog offers.
                isNpm ? stall.ResolveDailyFee(npmDailyRate) * DomainRules.DailyBilledMonthDays : occupancyMonthlyRate,
                isClosed ? stall.ClosedAt : null,
                contractExpiry,
                lifetimeCollected,
                uncollected,
                stall.UpdatedBy,
                // The tenant's own section label (canonical sections) or the stall's custom section name, so
                // the register can be filtered and printed section by section like the roster.
                stall.Section is { } closedSec
                    ? (stall.Facility!.SectionLabel(closedSec) ?? GetSectionName(closedSec))
                    : (stall.CustomSectionName ?? string.Empty),
                // The day this lessee actually stopped holding the stall. Differs from the term's expiry when the
                // occupancy ended early — handed to the next lessee, or frozen by closure — and it is the date the
                // register must show, otherwise a handover looks like a contract still running. Stated as the fact
                // it is, even on a period-scoped read whose FIGURES stop at the end of the period.
                occupancy.End,
                // Somebody else holds this stall now, so this row is history only: the register must not offer to
                // renew or reopen it, which would act on the sitting lessee's occupancy.
                stall.Occupancies(today).Any(o => o.IsCurrent),
                // The term this row is the record of, so an action on THIS lessee cannot pick up the sitting one's.
                contract.Id));
        }

        return result
            .OrderByDescending(r => r.ClosedOn ?? r.ExpiryDate)
            .ThenBy(r => r.FacilityName)
            .ToList();
    }

    /// <summary>
    /// A billing month's placement inside an occupancy is decided by <see cref="Stall.OccupancyAnsweringForMonth"/>
    /// (one occupancy answers for a month), so this register does not test overlap for money any more.
    /// </summary>
    public async Task<Stall?> GetByIdWithContractsAsync(Guid id, CancellationToken ct)
    {        return await context.Stalls
            .Include(s => s.Contracts)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<Stall>> GetStallsWithContractsByFacilityAsync(FacilityCode facilityCode, MarketSection? section, string? customSectionName, CancellationToken ct)
    {
        // Tracked (no AsNoTracking) so import renewals — terminating old contracts, reopening a closed
        // stall, adding a new contract — are persisted on SaveChanges.
        var query = context.Stalls
            .Include(s => s.Contracts)
            .Where(s => s.Facility!.Code == facilityCode);

        if (facilityCode == FacilityCode.NPM)
        {
            if (section.HasValue)
                query = query.Where(s => s.Section == section);
            else
            {
                // A specific NPM custom section (its stalls are numbered independently). A null custom name
                // here means "all null-section NPM stalls" (kept for safety), but callers pass the name.
                var name = (customSectionName ?? string.Empty).Trim();
                query = query.Where(s => s.Section == null && s.CustomSectionName == name);
            }
        }
        else
        {
            query = query.Where(s => s.Section == null);
        }

        return await query.ToListAsync(ct);
    }

    public async Task AddAsync(Stall stall, CancellationToken ct)
    {
        await context.Stalls.AddAsync(stall, ct);
    }

    public async Task AddContractAsync(Contract contract, CancellationToken ct)
    {
        await context.Contracts.AddAsync(contract, ct);
    }

    public async Task UpdateAsync(Stall stall, CancellationToken ct)
    {
        context.Stalls.Update(stall);
        await Task.CompletedTask;
    }

    public async Task<bool> IsStallNoUniqueAsync(FacilityCode facilityCode, MarketSection? section, string? customSectionName, string stallNo, CancellationToken ct)
    {
        return !await MatchingStalls(facilityCode, section, customSectionName, stallNo).AnyAsync(ct);
    }

    /// <summary>
    /// The stall that already carries this number in the same place (facility, and for NPM the same canonical or
    /// custom section), with its contracts, so a caller can tell whether it is currently occupied. Returns null
    /// when the number is free. Used to let a new occupant take over a stall that has been vacated instead of
    /// forcing a second, fictitious stall number for the same physical space.
    /// </summary>
    public async Task<Stall?> FindStallByNumberAsync(FacilityCode facilityCode, MarketSection? section, string? customSectionName, string stallNo, CancellationToken ct)
    {
        return await MatchingStalls(facilityCode, section, customSectionName, stallNo)
            .Include(s => s.Facility)
            .Include(s => s.Contracts)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Stalls occupying one number in one place. Mirrors the database's unique index exactly: per facility, and
    /// for NPM per canonical section or per custom section name — so the same number may legitimately exist in a
    /// different section.
    /// </summary>
    private IQueryable<Stall> MatchingStalls(FacilityCode facilityCode, MarketSection? section, string? customSectionName, string stallNo)
    {
        var query = context.Stalls.Where(s =>
            s.Facility!.Code == facilityCode &&
            s.StallNo == stallNo);

        if (facilityCode == FacilityCode.NPM)
        {
            if (section.HasValue)
                query = query.Where(s => s.Section == section);
            else
            {
                // Custom NPM section — unique per (facility, custom section, stall no), so the same number
                // may exist in a different custom section (matches the DB expression index).
                var name = (customSectionName ?? string.Empty).Trim();
                query = query.Where(s => s.Section == null && s.CustomSectionName == name);
            }
        }
        else
        {
            query = query.Where(s => s.Section == null);
        }

        return query;
    }
}
