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

        // Group stalls by section (NPM has sections, others don't)
        var sectionsWithSection = stalls
            .Where(s => s.Section.HasValue)
            .GroupBy(s => s.Section!.Value)
            .Select(g => new StallHoldersSectionDto
            {
                SectionName = g.Key.ToString(),
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
                        MonthlyRentalRate = s.MonthlyRate,
                        ActualMonthlyRental = s.MonthlyRate,
                        WholeYearRental = s.MonthlyRate * 12,
                        FishFeeTotal = null,   // List of Stallholders is base rental only — no fish/elec/water
                        IsClosed = s.Status == StallStatus.Closed
                    };
                }).ToList(),
                SectionMonthlyTotal = g.Where(s => s.Status == StallStatus.Active).Sum(s => s.MonthlyRate),
                SectionActualMonthly = g.Where(s => s.Status == StallStatus.Active).Sum(s => s.MonthlyRate),
                SectionWholeYearTotal = g.Where(s => s.Status == StallStatus.Active).Sum(s => s.MonthlyRate * 12),
                SectionFishFeeTotal = 0   // base rental only — additional fees (fish/elec/water) are not part of this list
            }).ToList();

        // Handle stalls without sections (TCC, NCC, BBQ, ICE, SLH)
        var stallsWithoutSection = stalls.Where(s => !s.Section.HasValue).ToList();
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
                        MonthlyRentalRate = s.MonthlyRate,
                        ActualMonthlyRental = s.MonthlyRate,
                        WholeYearRental = s.MonthlyRate * 12,
                        FishFeeTotal = null,
                        IsClosed = s.Status == StallStatus.Closed,
                        AreaLocation = s.AreaLocation?.ToString()
                    };
                }).ToList(),
                SectionMonthlyTotal = stallsWithoutSection.Where(s => s.Status == StallStatus.Active).Sum(s => s.MonthlyRate),
                SectionActualMonthly = stallsWithoutSection.Where(s => s.Status == StallStatus.Active).Sum(s => s.MonthlyRate),
                SectionWholeYearTotal = stallsWithoutSection.Where(s => s.Status == StallStatus.Active).Sum(s => s.MonthlyRate * 12),
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
            GrandTotalMonthlyRate = stalls.Where(s => s.Status == StallStatus.Active).Sum(s => s.MonthlyRate),
            GrandTotalWholeYearRental = stalls.Where(s => s.Status == StallStatus.Active).Sum(s => s.MonthlyRate * 12)
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
                    s.CustomSectionName
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
                s.CustomSectionName
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
    {
        var today = PhilippineTime.Today;

        // Resolve the municipality's NPM rates as of today (falls back to the ordinance constants, so
        // Cantilan's lifetime/uncollected figures are unchanged).
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var npmDailyRate = rateSnapshot.Resolve(FeeRateKey.NpmDailyStall, today);
        var npmFishRate = rateSnapshot.Resolve(FeeRateKey.NpmFishPerKilo, today);

        // Candidates: closed stalls, or active stalls whose active contract has lapsed. A stall with no
        // active contract is "vacant" (not an inactive ACCOUNT), so the active-contract filter excludes it.
        // Expiry (= effectivity + duration years) is a domain-computed value that Npgsql cannot translate
        // in a predicate, so occupied stalls are loaded then the expired ones are filtered in memory.
        var occupied = await context.Stalls
            .AsNoTracking()
            .Include(s => s.Facility)
            .Include(s => s.Contracts.Where(c => c.IsActive))
            .Where(s => s.Contracts.Any(c => c.IsActive))
            .ToListAsync(ct);

        var candidates = occupied
            .Where(s => s.Status == StallStatus.Closed || s.IsContractExpired())
            .ToList();

        if (candidates.Count == 0)
            return new List<ClosedStallAccountDto>();

        var stallIds = candidates.Select(s => s.Id).ToList();

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

        var paidByStall = paidDailies.GroupBy(d => d.StallId).ToDictionary(g => g.Key, g => g.ToList());
        var absentByStall = absentDailies.GroupBy(d => d.StallId).ToDictionary(g => g.Key, g => g.Select(x => x.CollectionDate).ToHashSet());
        var paymentsByStall = payments.GroupBy(p => p.StallId).ToDictionary(g => g.Key, g => g.ToList());
        var excusedByStall = exceptions.GroupBy(e => e.StallId).ToDictionary(g => g.Key, g => g.Select(x => (x.BillingYear, x.BillingMonth)).ToHashSet());

        var result = new List<ClosedStallAccountDto>(candidates.Count);
        foreach (var stall in candidates)
        {
            var contract = stall.Contracts.Where(c => c.IsActive).OrderBy(c => c.EffectivityDate).First();
            var isNpm = stall.Facility?.Code == FacilityCode.NPM;
            var isClosed = stall.Status == StallStatus.Closed;

            var contractExpiry = contract.EffectivityDate.AddYears(contract.DurationYears);
            // End point: close date (closed) or contract expiry (expired). Never past the contract term.
            var endDate = isClosed && stall.ClosedAt is { } ca && ca < contractExpiry ? ca : contractExpiry;

            var stallPaid = paidByStall.GetValueOrDefault(stall.Id) ?? new();
            var stallAbsent = absentByStall.GetValueOrDefault(stall.Id) ?? new();
            var stallPayments = paymentsByStall.GetValueOrDefault(stall.Id) ?? new();
            var stallExcused = excusedByStall.GetValueOrDefault(stall.Id) ?? new();

            // Lifetime collected = every peso actually received (status-independent).
            var lifetimeCollected = isNpm
                ? stallPaid.Sum(d => d.DailyFee + (d.FishKilos.HasValue ? d.FishKilos.Value * npmFishRate : 0m))
                : stallPayments.Sum(p => p.AmountPaid);

            decimal uncollected = 0m;
            if (isNpm)
            {
                // ₱30 for each contract-effective, non-absent day in [effectivity, endDate] with no paid collection.
                var paidDates = stallPaid.Select(d => d.CollectionDate).ToHashSet();
                for (var d = contract.EffectivityDate; d <= endDate && d <= contractExpiry; d = d.AddDays(1))
                {
                    if (stallAbsent.Contains(d) || paidDates.Contains(d)) continue;
                    uncollected += npmDailyRate;
                }
            }
            else
            {
                // Per calendar month overlapping [effectivity, endDate]: a non-Unpaid record's balance,
                // else the full monthly rent. Excused months owe nothing.
                var cursor = new DateOnly(contract.EffectivityDate.Year, contract.EffectivityDate.Month, 1);
                var endMonth = new DateOnly(endDate.Year, endDate.Month, 1);
                while (cursor <= endMonth)
                {
                    var mStart = cursor;
                    var mEnd = new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
                    var inContract = contract.EffectivityDate <= mEnd && contractExpiry >= mStart;
                    if (inContract && !stallExcused.Contains((cursor.Year, cursor.Month)))
                    {
                        var rec = stallPayments.FirstOrDefault(p => p.BillingYear == cursor.Year && p.BillingMonth == cursor.Month);
                        uncollected += rec is not null && rec.Status != PaymentStatus.Unpaid
                            ? rec.BalanceDue
                            : stall.MonthlyRate;
                    }
                    cursor = cursor.AddMonths(1);
                }
            }

            result.Add(new ClosedStallAccountDto(
                stall.Id,
                isClosed ? InactiveAccountState.Closed : InactiveAccountState.Expired,
                stall.Facility!.Code,
                stall.Facility!.Name,
                stall.StallNo,
                contract.ActualOccupant,
                contract.NameOnContract,
                contract.EffectivityDate,
                contract.DurationYears,
                stall.MonthlyRate,
                isClosed ? stall.ClosedAt : null,
                contractExpiry,
                lifetimeCollected,
                uncollected,
                stall.UpdatedBy));
        }

        return result
            .OrderByDescending(r => r.ClosedOn ?? r.ExpiryDate)
            .ThenBy(r => r.FacilityName)
            .ToList();
    }

    public async Task<Stall?> GetByIdWithContractsAsync(Guid id, CancellationToken ct)
    {
        return await context.Stalls
            .Include(s => s.Contracts)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<Stall>> GetStallsWithContractsByFacilityAsync(FacilityCode facilityCode, MarketSection? section, CancellationToken ct)
    {
        // Tracked (no AsNoTracking) so import renewals — terminating old contracts, reopening a closed
        // stall, adding a new contract — are persisted on SaveChanges.
        var query = context.Stalls
            .Include(s => s.Contracts)
            .Where(s => s.Facility!.Code == facilityCode);

        query = facilityCode == FacilityCode.NPM
            ? query.Where(s => s.Section == section)
            : query.Where(s => s.Section == null);

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

    public async Task<bool> IsStallNoUniqueAsync(FacilityCode facilityCode, MarketSection? section, string stallNo, CancellationToken ct)
    {
        var query = context.Stalls.Where(s =>
            s.Facility!.Code == facilityCode &&
            s.StallNo == stallNo);

        query = facilityCode == FacilityCode.NPM
            ? query.Where(s => s.Section == section)
            : query.Where(s => s.Section == null);

        return !await query.AnyAsync(ct);
    }
}
