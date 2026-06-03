using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.StallHolders;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Extensions;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class StallRepository(AppDbContext context) : IStallRepository
{
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
                        IsClosed = s.Status == StallStatus.Closed
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
                    s.Remarks
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
                s.Remarks
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
