using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class TpmRepository(AppDbContext context, ITpmMarketDayProvider marketDayProvider) : ITpmRepository
{
    public async Task<TpmVendor?> GetVendorByIdAsync(Guid id, CancellationToken ct = default)
        => await context.TpmVendors.FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<IReadOnlyList<TpmVendor>> GetAllVendorsAsync(CancellationToken ct = default)
        => await context.TpmVendors.AsNoTracking().Where(v => v.IsActive).OrderBy(v => v.VendorName).ToListAsync(ct);

    public async Task AddVendorAsync(TpmVendor vendor, CancellationToken ct = default)
        => await context.TpmVendors.AddAsync(vendor, ct);

    public async Task<TpmAttendance?> GetAttendanceByIdAsync(Guid id, CancellationToken ct = default)
        => await context.TpmAttendances.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<TpmAttendance?> GetAttendanceAsync(Guid vendorId, DateOnly marketDate, CancellationToken ct = default)
        => await context.TpmAttendances
            .FirstOrDefaultAsync(a => a.VendorId == vendorId && a.MarketDate == marketDate, ct);

    public async Task<IReadOnlyList<TpmAttendance>> GetAttendancesByDateAsync(DateOnly marketDate, CancellationToken ct = default)
        => await context.TpmAttendances
            .AsNoTracking()
            .Include(a => a.Vendor)
            .Where(a => a.MarketDate == marketDate)
            .OrderBy(a => a.Vendor!.VendorName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TpmAttendance>> GetAttendancesByMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);
        
        return await context.TpmAttendances
            .AsNoTracking()
            .Where(a => a.MarketDate >= startDate && a.MarketDate <= endDate)
            .ToListAsync(ct);
    }

    public async Task AddAttendanceAsync(TpmAttendance attendance, CancellationToken ct = default)
        => await context.TpmAttendances.AddAsync(attendance, ct);

    public async Task<TpmOverviewDto> GetOverviewAsync(int year, int month, CancellationToken ct = default)
    {
        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var attendances = await context.TpmAttendances
            .AsNoTracking()
            .Where(a => a.MarketDate >= startDate && a.MarketDate <= endDate)
            .ToListAsync(ct);

        var paidCount = attendances.Count(a => a.IsPaid);
        var totalAttendances = attendances.Count;

        var marketDay = await marketDayProvider.GetMarketDayAsync(ct);

        return new TpmOverviewDto
        {
            CollectedThisMonth = attendances.Where(a => a.IsPaid).Sum(a => a.Fee),
            FridaysThisMonth = GetMarketDaysInMonth(year, month, marketDay).Count,
            VendorEntriesThisMonth = totalAttendances,
            CollectionRate = totalAttendances > 0 ? (int)((double)paidCount / totalAttendances * 100) : 0
        };
    }

    public async Task<IReadOnlyList<TpmMarketDayDto>> GetMarketDaysAsync(int year, int month, CancellationToken ct = default)
    {
        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        var attendances = await context.TpmAttendances
            .Where(a => a.MarketDate >= startDate && a.MarketDate <= endDate)
            .GroupBy(a => a.MarketDate)
            .Select(g => new TpmMarketDayDto
            {
                MarketDate = g.Key,
                VendorsPaid = g.Count(a => a.IsPaid),
                TotalCollected = g.Where(a => a.IsPaid).Sum(a => a.Fee)
            })
            .ToListAsync(ct);

        return attendances;
    }

    public async Task<IReadOnlyList<TpmVendorAttendanceDto>> GetVendorAttendanceAsync(DateOnly marketDate, CancellationToken ct = default)
    {
        return await context.TpmAttendances
            .Where(a => a.MarketDate == marketDate)
            .Select(a => new TpmVendorAttendanceDto
            {
                Id = a.Id,
                VendorId = a.VendorId,
                VendorName = a.Vendor!.VendorName,
                Goods = a.Vendor.Goods,
                IsPaid = a.IsPaid,
                ORNumber = a.ORNumber,
                Fee = a.Fee,
                MarketDate = a.MarketDate
            })
            .OrderBy(a => a.VendorName)
            .ToListAsync(ct);
    }

    public async Task<bool> IsVendorNameUniqueAsync(string vendorName, CancellationToken ct = default)
        => !await context.TpmVendors.AnyAsync(v => v.VendorName.ToLower() == vendorName.ToLower(), ct);

    public async Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct = default)
    {
        return await OrNumberRegistry.IsAvailableAsync(context, orNumber, ct);
    }

    /// <summary>
    /// Every vendor attendance for the month, projected with vendor name/goods in a single query
    /// (used by the report's Monthly phase and Status Report — replaces the per-Friday N+1 fetch).
    /// </summary>
    public async Task<IReadOnlyList<TpmVendorAttendanceDto>> GetMonthAttendanceAsync(int year, int month, CancellationToken ct = default)
    {
        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        return await context.TpmAttendances
            .AsNoTracking()
            .Where(a => a.MarketDate >= startDate && a.MarketDate <= endDate)
            .OrderBy(a => a.MarketDate)
            .ThenBy(a => a.Vendor!.VendorName)
            .Select(a => new TpmVendorAttendanceDto
            {
                Id = a.Id,
                VendorId = a.VendorId,
                VendorName = a.Vendor!.VendorName,
                Goods = a.Vendor.Goods,
                IsPaid = a.IsPaid,
                ORNumber = a.ORNumber,
                Fee = a.Fee,
                MarketDate = a.MarketDate
            })
            .ToListAsync(ct);
    }

    /// <summary>
    /// Collection history for Tabo-an Public Market: every month of <paramref name="year"/> (up to the
    /// current month for the current year, all 12 for past years) plus a rolling 5-year summary.
    /// All attendances across the window are loaded once, then aggregated in memory.
    /// MarketDate is a calendar <see cref="DateOnly"/>, so no timezone conversion is required.
    /// </summary>
    public async Task<TpmHistoryDto> GetHistoryAsync(int year, CancellationToken ct = default)
    {
        var today = PhilippineTime.Today;
        var firstYear = year - 4;
        var startDate = new DateOnly(firstYear, 1, 1);
        var endDate = new DateOnly(year, 12, 31);

        var rows = await context.TpmAttendances
            .AsNoTracking()
            .Where(a => a.MarketDate >= startDate && a.MarketDate <= endDate)
            .Select(a => new HistoryRow(a.Vendor!.Goods, a.Fee, a.IsPaid, a.MarketDate))
            .ToListAsync(ct);

        var maxMonth = year < today.Year ? 12 : year == today.Year ? today.Month : 0;

        var monthly = new List<TpmPeriodSummaryDto>();
        for (var m = 1; m <= maxMonth; m++)
        {
            var set = rows.Where(r => r.Date.Year == year && r.Date.Month == m).ToList();
            monthly.Add(SummarizeHistory(new DateOnly(year, m, 1).ToString("MMMM"), year, m, set));
        }

        var yearly = new List<TpmPeriodSummaryDto>();
        for (var y = firstYear; y <= year; y++)
        {
            var set = rows.Where(r => r.Date.Year == y).ToList();
            yearly.Add(SummarizeHistory(y.ToString(), y, null, set));
        }

        return new TpmHistoryDto(year, monthly, yearly);
    }

    private sealed record HistoryRow(string Goods, decimal Fee, bool IsPaid, DateOnly Date);

    private static TpmPeriodSummaryDto SummarizeHistory(string label, int year, int? month, List<HistoryRow> set)
    {
        var total = set.Count;
        var paid = set.Count(r => r.IsPaid);
        var collected = set.Where(r => r.IsPaid).Sum(r => r.Fee);
        var marketDays = set.Select(r => r.Date).Distinct().Count();

        var goods = set
            .GroupBy(r => string.IsNullOrWhiteSpace(r.Goods) ? "—" : r.Goods)
            .Select(g => new TpmGoodsTallyDto(g.Key, g.Count(), g.Where(r => r.IsPaid).Sum(r => r.Fee)))
            .OrderByDescending(x => x.Entries)
            .ToList();

        return new TpmPeriodSummaryDto(
            label, year, month,
            marketDays,
            total,
            paid,
            total - paid,
            collected,
            total > 0 ? (int)Math.Round((double)paid / total * 100) : 0,
            goods);
    }

    private static List<DateOnly> GetMarketDaysInMonth(int year, int month, DayOfWeek marketDay)
    {
        var days = new List<DateOnly>();
        var date = new DateOnly(year, month, 1);

        while (date.DayOfWeek != marketDay)
            date = date.AddDays(1);

        while (date.Month == month)
        {
            days.Add(date);
            date = date.AddDays(7);
        }

        return days;
    }
}
