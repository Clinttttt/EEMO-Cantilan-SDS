using EEMOCantilanSDS.Infrastructure.Time;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Payments;
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

// Entry partial of StallRepository: the stall AGGREGATE and the ordinary stall reads (IStallRepository). The capabilities that
// answer a different question live in sibling files - .Attention.cs (contracts needing attention), .Mobile.cs (the collector
// app's rounds), .Register.cs (the List of Stallholders), .ClosedAccounts.cs (the inactive-accounts register) - and the
// arithmetic they share in .Collectable.cs.
//
// One class in six files, not six classes: the mobile screens reuse the same private obligation and eligibility arithmetic the
// register uses, and duplicating that is how two screens start disagreeing about the same peso. What IS separate is the
// contracts, so a handler serving the app cannot reach a stall aggregate.
public partial class StallRepository(
    AppDbContext context,
    IFeeRateResolver feeRateResolver,
    IClock clock,
    INpmMonthSettlementService? npmMonthSettlement = null)
    : IStallRepository, IStallMobileQueries, IClosedStallAccountQueries, IContractAttentionQueries, IStallRegisterQueries
{
    // Test/non-DI convenience: resolves fees from the context (empty rate table => ordinance constants).
    public StallRepository(AppDbContext context) : this(context, new FeeRateResolver(context), new SystemClock()) { }

    /// <summary>
    /// Captured into fields because this class is PARTIAL: a primary-constructor parameter is only in scope in the file that
    /// declares it, and the reads that need the context, the rates and "today" are spread across the sibling files. The same
    /// convention <see cref="Reports.FacilityReportsRepository"/> and <see cref="CollectorRepository"/> already use.
    /// </summary>
    private readonly AppDbContext _context = context;

    private readonly IFeeRateResolver _feeRateResolver = feeRateResolver;
    private readonly IClock _clock = clock;

    private INpmMonthSettlementService? _npmMonthSettlement = npmMonthSettlement;

    /// <summary>
    /// How the office settles one market month. Asked rather than reimplemented: what a month owes depends on whether the
    /// office lets it for a rent or bills its days, and a second copy of that arithmetic is how the collector's screen and the
    /// payor's own screen start disagreeing about the same peso.
    /// </summary>
    /// <remarks>
    /// OPTIONAL in the constructor, and built from the context when absent, so that the sixty-odd tests and the one convenience
    /// constructor that already say <c>new StallRepository(context)</c> keep working untouched. DI supplies the real one.
    /// </remarks>
    private INpmMonthSettlementService NpmMonthSettlement =>
        _npmMonthSettlement ??= new NpmMonthSettlementService(
            new DailyCollectionRepository(_context),
            new NpmMarketClosureRepository(_context),
            _feeRateResolver,
            _clock);





    public async Task<CursorPagedResult<StallDto>> GetStallsByFacilityPaginatedAsync(FacilityCode facilityCode, MarketSection? section, DateTime? cursor, int pageSize, CancellationToken ct)
    {
        var query = _context.Stalls
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

        // The fee each stall IS billed, settled by the one rule, so a screen stating a stall's rate cannot answer by a
        // different rule than the collector charging for it. Only for the market: nothing else is billed by the day.
        var rateSnapshot = facilityCode == FacilityCode.NPM ? await _feeRateResolver.GetSnapshotAsync(ct) : null;
        var rateAsOf = _clock.PhilippineToday;

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
                    s.Fees.HasFlag(ApplicableFees.Water),
                    rateSnapshot is null ? null : NpmDailyFee.ForStallOrNull(s, rateSnapshot, rateAsOf)
                );
            }).ToList(),
            NextCursor = pagedResult.NextCursor,
            HasMore = pagedResult.HasMore
        };
    }
    public async Task<IReadOnlyList<StallDto>> GetStallsByFacilityAsync(FacilityCode facilityCode, MarketSection? section, CancellationToken ct)
    {
        var query = _context.Stalls
            .AsNoTracking()
            .Include(s => s.Contracts)
            .Where(s => s.Facility!.Code == facilityCode);

        if (section.HasValue)
            query = query.Where(s => s.Section == section.Value);

        var stalls = await query.ToListAsync(ct);

        // The same resolved fee as the paged read above: one rule, so two lists of the same stalls cannot disagree.
        var rateSnapshot = facilityCode == FacilityCode.NPM ? await _feeRateResolver.GetSnapshotAsync(ct) : null;
        var rateAsOf = _clock.PhilippineToday;

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
                s.Fees.HasFlag(ApplicableFees.Water),
                rateSnapshot is null ? null : NpmDailyFee.ForStallOrNull(s, rateSnapshot, rateAsOf)
            );
        }).ToList();
    }

    public async Task<Dictionary<MarketSection, StallSummaryDto>> GetSectionSummariesAsync(FacilityCode facilityCode, int year, int month, CancellationToken ct)
    {
        var stalls = await _context.Stalls
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
        return await _context.Stalls
            .Include(s => s.Facility)
            .Include(s => s.Contracts)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<FacilityCode?> GetFacilityCodeByStallIdAsync(Guid stallId, CancellationToken ct)
    {
        return await _context.Stalls
            .Where(s => s.Id == stallId)
            .Select(s => (FacilityCode?)s.Facility!.Code)
            .FirstOrDefaultAsync(ct);
    }


    /// <summary>
    /// A billing month's placement inside an occupancy is decided by <see cref="Stall.OccupancyAnsweringForMonth"/>
    /// (one occupancy answers for a month), so this register does not test overlap for money any more.
    /// </summary>
    public async Task<Stall?> GetByIdWithContractsAsync(Guid id, CancellationToken ct)
    {        return await _context.Stalls
            .Include(s => s.Contracts)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<Stall>> GetStallsWithContractsByFacilityAsync(FacilityCode facilityCode, MarketSection? section, string? customSectionName, CancellationToken ct)
    {
        // Tracked (no AsNoTracking) so import renewals — terminating old contracts, reopening a closed
        // stall, adding a new contract — are persisted on SaveChanges.
        var query = _context.Stalls
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
        await _context.Stalls.AddAsync(stall, ct);
    }

    public async Task AddContractAsync(Contract contract, CancellationToken ct)
    {
        await _context.Contracts.AddAsync(contract, ct);
    }

    public async Task UpdateAsync(Stall stall, CancellationToken ct)
    {
        _context.Stalls.Update(stall);
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
        var query = _context.Stalls.Where(s =>
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
