using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Extensions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class StallRepository(AppDbContext context) : IStallRepository
{
    public async Task<MobileNpmCollectionDto> GetMobileNpmCollectionAsync(int year, int month, DateOnly collectionDate, CancellationToken ct)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var effectiveEnd = GetEffectiveCollectionEnd(monthStart, monthEnd, collectionDate);

        var stalls = await context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts.Where(c => !c.IsDeleted))
            .Include(s => s.DailyCollections.Where(d =>
                d.CollectionDate >= monthStart &&
                d.CollectionDate <= monthEnd &&
                !d.IsDeleted))
            .Where(s =>
                s.Facility!.Code == FacilityCode.NPM &&
                s.Status == StallStatus.Active &&
                s.Section.HasValue &&
                !s.IsDeleted)
            .OrderBy(s => s.Section)
            .ThenBy(s => s.StallNo)
            .ToListAsync(ct);

        var rows = stalls.Select(s =>
        {
            var contract = s.Contracts
                .Where(c => c.IsActive && !c.IsDeleted)
                .OrderByDescending(c => c.EffectivityDate)
                .FirstOrDefault();

            var dailyRate = s.DailyRate ?? FeeRates.NpmDailyFee;
            var todayCollection = s.DailyCollections.FirstOrDefault(d => d.CollectionDate == collectionDate);
            var paidCollections = s.DailyCollections
                .Where(d => d.IsPaid && d.CollectionDate >= monthStart && d.CollectionDate <= effectiveEnd)
                .ToList();

            var collectableDays = CountCollectableDays(contract?.EffectivityDate, monthStart, effectiveEnd);
            var daysCollected = paidCollections.Count;
            var daysMissed = Math.Max(0, collectableDays - daysCollected);
            var monthCollectedAmount = paidCollections.Sum(d =>
                d.DailyFee + (d.FishKilos.GetValueOrDefault() * FeeRates.NpmFishFeePerKilo));

            return new MobileNpmStallCollectionDto(
                s.Id,
                s.StallNo,
                string.IsNullOrWhiteSpace(contract?.ActualOccupant) ? "No active occupant" : contract.ActualOccupant,
                contract?.NameOnContract ?? contract?.ActualOccupant ?? string.Empty,
                s.Section,
                GetSectionName(s.Section),
                s.Status,
                dailyRate,
                todayCollection is not null,
                todayCollection?.IsPaid == true,
                todayCollection?.ORNumber,
                todayCollection?.FishKilos,
                daysCollected,
                daysMissed,
                collectableDays,
                monthCollectedAmount);
        }).ToList();

        var collectedToday = rows.Where(r => r.IsCollectedToday).ToList();
        var pendingToday = rows.Where(r => r.CollectableDays > 0).ToList();

        return new MobileNpmCollectionDto(
            year,
            month,
            collectionDate,
            rows.Count,
            collectedToday.Count,
            pendingToday.Count(r => !r.IsCollectedToday),
            collectedToday.Sum(r => r.DailyRate + (r.FishKilosToday.GetValueOrDefault() * FeeRates.NpmFishFeePerKilo)),
            pendingToday.Where(r => !r.IsCollectedToday).Sum(r => r.DailyRate),
            rows.Sum(r => r.DaysCollected),
            rows.Sum(r => r.DaysMissed),
            rows);
    }

    public async Task<MobileMonthlyCollectionDto> GetMobileMonthlyCollectionAsync(
        FacilityCode facilityCode, int year, int month, DateOnly collectionDate, CancellationToken ct)
    {
        var stalls = await context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts.Where(c => !c.IsDeleted))
            .Include(s => s.PaymentRecords.Where(p =>
                p.BillingYear == year &&
                p.BillingMonth == month &&
                !p.IsDeleted))
            .Where(s =>
                s.Facility!.Code == facilityCode &&
                s.Status == StallStatus.Active &&
                !s.IsDeleted &&
                s.Contracts.Any(c => c.IsActive && !c.IsDeleted))
            .OrderBy(s => s.StallNo)
            .ToListAsync(ct);

        var rows = stalls.Select(s =>
        {
            var contract = s.Contracts
                .Where(c => c.IsActive && !c.IsDeleted)
                .OrderByDescending(c => c.EffectivityDate)
                .FirstOrDefault();

            var record = s.PaymentRecords.FirstOrDefault();
            var status = record?.Status ?? PaymentStatus.Unpaid;
            // Monthly-rental facilities carry no utilities, so the bill is the flat monthly rate.
            var amountPaid = record?.AmountPaid ?? 0m;
            var balance = record is not null ? record.BalanceDue : s.MonthlyRate;

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
                record is not null);
        }).ToList();

        return new MobileMonthlyCollectionDto(
            facilityCode,
            GetFacilityDisplayName(facilityCode),
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
        return s.Type.ToString();
    }

    private static string GetFacilityDisplayName(FacilityCode code) => code switch
    {
        FacilityCode.TCC => "Town Center Commercial",
        FacilityCode.NCC => "New Commercial Center",
        FacilityCode.BBQ => "Barbecue Stand",
        FacilityCode.ICE => "Ice Plant",
        _ => code.ToString()
    };

    public async Task<StallHoldersListDto> GetStallHoldersListAsync(FacilityCode facilityCode, MarketSection? section, string? searchTerm, CancellationToken ct)
    {
        var query = context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts.Where(c => !c.IsDeleted))
            .Where(s => s.Facility!.Code == facilityCode && !s.IsDeleted);

        if (section.HasValue)
            query = query.Where(s => s.Section == section.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            query = query.Where(s =>
                s.StallNo.ToLower().Contains(term) ||
                s.Contracts.Any(c =>
                    !c.IsDeleted && (
                        c.ActualOccupant.ToLower().Contains(term) ||
                        (c.NameOnContract ?? "").ToLower().Contains(term))));
        }

        var stalls = await query
            .OrderBy(s => s.Section)
            .ThenBy(s => s.StallNo)
            .ToListAsync(ct);

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
                    var contract = s.Contracts.FirstOrDefault(c => c.IsActive && !c.IsDeleted);
                    var durationYears = contract != null ? PhilippineTime.Today.Year - contract.EffectivityDate.Year : 0;
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
                        FishFeeTotal = g.Key == MarketSection.FishSection ? s.MonthlyRate * 12 : null,
                        IsClosed = s.Status == StallStatus.Closed
                    };
                }).ToList(),
                SectionMonthlyTotal = g.Sum(s => s.MonthlyRate),
                SectionActualMonthly = g.Sum(s => s.MonthlyRate),
                SectionWholeYearTotal = g.Sum(s => s.MonthlyRate * 12),
                SectionFishFeeTotal = g.Key == MarketSection.FishSection ? g.Sum(s => s.MonthlyRate * 12) : 0
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
                    var contract = s.Contracts.FirstOrDefault(c => c.IsActive && !c.IsDeleted);
                    var durationYears = contract != null ? PhilippineTime.Today.Year - contract.EffectivityDate.Year : 0;
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
                SectionMonthlyTotal = stallsWithoutSection.Sum(s => s.MonthlyRate),
                SectionActualMonthly = stallsWithoutSection.Sum(s => s.MonthlyRate),
                SectionWholeYearTotal = stallsWithoutSection.Sum(s => s.MonthlyRate * 12),
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
            GrandTotalMonthlyRate = stalls.Sum(s => s.MonthlyRate),
            GrandTotalWholeYearRental = stalls.Sum(s => s.MonthlyRate * 12)
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
        MarketSection.FishSection => "Fish Section",
        MarketSection.MeatSection => "Meat Section",
        _ => "Unassigned Section"
    };

    public async Task<CursorPagedResult<StallDto>> GetStallsByFacilityPaginatedAsync(FacilityCode facilityCode, MarketSection? section, DateTime? cursor, int pageSize, CancellationToken ct)
    {
        var query = context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts.Where(c => !c.IsDeleted))
            .Where(s => s.Facility!.Code == facilityCode && !s.IsDeleted);

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
                var activeContract = s.Contracts.FirstOrDefault(c => c.IsActive && !c.IsDeleted);
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
                    activeContract?.DurationYears ?? 0
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
            .Include(s => s.Contracts.Where(c => !c.IsDeleted))
            .Where(s => s.Facility!.Code == facilityCode && !s.IsDeleted);

        if (section.HasValue)
            query = query.Where(s => s.Section == section.Value);

        var stalls = await query.ToListAsync(ct);

        return stalls.Select(s =>
        {
            var activeContract = s.Contracts.FirstOrDefault(c => c.IsActive && !c.IsDeleted);

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
                activeContract?.DurationYears ?? 0
            );
        }).ToList();
    }

    public async Task<Dictionary<MarketSection, StallSummaryDto>> GetSectionSummariesAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct)
    {
        var stalls = await context.Stalls
            .AsNoTracking()
            .Include(s => s.PaymentRecords.Where(p => p.BillingYear == year && p.BillingMonth == month && !p.IsDeleted))
            .Where(s => s.Facility!.Code == facilityCode && s.Section.HasValue && !s.IsDeleted)
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

    public async Task<Stall?> GetByIdWithContractsAsync(Guid id, CancellationToken ct)
    {
        return await context.Stalls
            .Include(s => s.Contracts)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
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
            s.StallNo == stallNo &&
            !s.IsDeleted);

        query = facilityCode == FacilityCode.NPM
            ? query.Where(s => s.Section == section)
            : query.Where(s => s.Section == null);

        return !await query.AnyAsync(ct);
    }
}
