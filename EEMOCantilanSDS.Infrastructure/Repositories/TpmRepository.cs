using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class TpmRepository(AppDbContext context) : ITpmRepository
{
    public async Task<TpmVendor?> GetVendorByIdAsync(Guid id, CancellationToken ct = default)
        => await context.TpmVendors.FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<IReadOnlyList<TpmVendor>> GetAllVendorsAsync(CancellationToken ct = default)
        => await context.TpmVendors.Where(v => v.IsActive).OrderBy(v => v.VendorName).ToListAsync(ct);

    public async Task AddVendorAsync(TpmVendor vendor, CancellationToken ct = default)
        => await context.TpmVendors.AddAsync(vendor, ct);

    public async Task<TpmAttendance?> GetAttendanceByIdAsync(Guid id, CancellationToken ct = default)
        => await context.TpmAttendances.FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<TpmAttendance?> GetAttendanceAsync(Guid vendorId, DateOnly marketDate, CancellationToken ct = default)
        => await context.TpmAttendances
            .FirstOrDefaultAsync(a => a.VendorId == vendorId && a.MarketDate == marketDate, ct);

    public async Task<IReadOnlyList<TpmAttendance>> GetAttendancesByDateAsync(DateOnly marketDate, CancellationToken ct = default)
        => await context.TpmAttendances
            .Include(a => a.Vendor)
            .Where(a => a.MarketDate == marketDate)
            .OrderBy(a => a.Vendor!.VendorName)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TpmAttendance>> GetAttendancesByMonthAsync(int year, int month, CancellationToken ct = default)
    {
        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);
        
        return await context.TpmAttendances
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
            .Where(a => a.MarketDate >= startDate && a.MarketDate <= endDate)
            .ToListAsync(ct);

        var totalVendors = await context.TpmVendors.CountAsync(v => v.IsActive, ct);
        var paidCount = attendances.Count(a => a.IsPaid);
        var totalAttendances = attendances.Count;

        return new TpmOverviewDto
        {
            CollectedThisMonth = attendances.Where(a => a.IsPaid).Sum(a => a.Fee),
            FridaysThisMonth = GetFridaysInMonth(year, month).Count,
            RegisteredVendors = totalVendors,
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
            .Include(a => a.Vendor)
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
        var existsInTpm = await context.TpmAttendances.AnyAsync(a => a.ORNumber == orNumber, ct);
        if (existsInTpm) return false;

        var existsInPayments = await context.PaymentRecords.AnyAsync(p => p.ORNumber == orNumber, ct);
        if (existsInPayments) return false;

        var existsInDaily = await context.DailyCollections.AnyAsync(d => d.ORNumber == orNumber, ct);
        return !existsInDaily;
    }
    private static List<DateOnly> GetFridaysInMonth(int year, int month)
    {
  
        var fridays = new List<DateOnly>();
        var date = new DateOnly(year, month, 1);
        
        while (date.DayOfWeek != DayOfWeek.Friday)
            date = date.AddDays(1);
        
        while (date.Month == month)
        {
            fridays.Add(date);
            date = date.AddDays(7);
        }
        
        return fridays;
    }
}
