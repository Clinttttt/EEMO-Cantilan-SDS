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
            .Include(s => s.Contracts)
            .Where(s => s.Facility!.Code == facilityCode);

        if (section.HasValue)
            query = query.Where(s => s.Section == section.Value);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.ToLower();
            query = query.Where(s =>
                s.StallNo.ToLower().Contains(term) ||
                s.Contracts.Any(c =>
                    c.ActualOccupant.ToLower().Contains(term) ||
                    c.NameOnContract.ToLower().Contains(term)));
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
                    var contract = s.Contracts.FirstOrDefault(c => c.IsActive);
                    var durationYears = contract != null ? DateTime.Now.Year - contract.EffectivityDate.Year : 0;
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
                    var contract = s.Contracts.FirstOrDefault(c => c.IsActive);
                    var durationYears = contract != null ? DateTime.Now.Year - contract.EffectivityDate.Year : 0;
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
            .Include(s => s.Contracts)
            .Where(s => s.Facility!.Code == facilityCode);

        if (section.HasValue)
            query = query.Where(s => s.Section == section.Value);

        if (cursor.HasValue)
            query = query.Where(s => s.CreatedAt < cursor.Value);

        query = query.OrderByDescending(s => s.CreatedAt);

        var pagedResult = await query
            .Select(s => new
            {
                Stall = s,
                ActiveContract = s.Contracts.FirstOrDefault(c => c.IsActive)
            })
            .ToCursorPagedResultAsync(pageSize, x => x.Stall.CreatedAt, ct);

        return new CursorPagedResult<StallDto>
        {
            Items = pagedResult.Items.Select(x => new StallDto(
                x.Stall.Id,
                x.Stall.StallNo,
                x.Stall.Status,
                x.ActiveContract?.ActualOccupant,
                x.ActiveContract?.NameOnContract,
                x.Stall.AreaSqm,
                x.ActiveContract?.EffectivityDate.ToDateTime(TimeOnly.MinValue),
                x.Stall.MonthlyRate,
                x.ActiveContract?.ORNumber,
                x.Stall.Section,
                x.Stall.AreaLocation
            )).ToList(),
            NextCursor = pagedResult.NextCursor,
            HasMore = pagedResult.HasMore
        };
    }

    public async Task<IReadOnlyList<StallDto>> GetStallsByFacilityAsync(FacilityCode facilityCode, MarketSection? section, CancellationToken ct)
    {
        var query = context.Stalls
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
                activeContract?.ORNumber,
                s.Section,
                s.AreaLocation
            );
        }).ToList();
    }

    public async Task<Dictionary<MarketSection, StallSummaryDto>> GetSectionSummariesAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct)
    {
        var stalls = await context.Stalls
            .Include(s => s.PaymentRecords)
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
        return await context.Stalls.FirstOrDefaultAsync(s => s.Id == id, ct);
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

    public async Task<bool> IsStallNoUniqueAsync(FacilityCode facilityCode, string stallNo, CancellationToken ct)
    {
        return !await context.Stalls.AnyAsync(s => 
            s.Facility!.Code == facilityCode && 
            s.StallNo == stallNo, ct);
    }
}
