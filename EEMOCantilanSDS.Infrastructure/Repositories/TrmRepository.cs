using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Infrastructure.Repositories;

public class TrmRepository(AppDbContext context) : ITrmRepository
{
    public async Task<TrmTransporter?> GetTransporterByIdAsync(Guid id, CancellationToken ct = default)
        => await context.TrmTransporters.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<TrmTransporterListDto>> GetTransportersWithTodayTripsAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;

        var transporters = await context.TrmTransporters
            .Where(t => t.IsActive)
            .OrderBy(t => t.Organization)
            .ThenBy(t => t.Name)
            .ToListAsync(ct);

        var todayTripCounts = await context.TrmTrips
            .Where(t => t.RecordedAt.Date == today)
            .GroupBy(t => t.TransporterId)
            .Select(g => new { TransporterId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return transporters.Select(t => new TrmTransporterListDto
        {
            Id = t.Id,
            Name = t.Name,
            Organization = t.Organization,
            DefaultRoute = t.DefaultRoute,
            PlateNumber = t.PlateNumber,
            TripsToday = todayTripCounts.FirstOrDefault(x => x.TransporterId == t.Id)?.Count ?? 0
        }).ToList();
    }

    public async Task AddTransporterAsync(TrmTransporter transporter, CancellationToken ct = default)
        => await context.TrmTransporters.AddAsync(transporter, ct);

    public async Task<TrmTrip?> GetTripByIdAsync(Guid id, CancellationToken ct = default)
        => await context.TrmTrips.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<int> GetTodayTripCountForTransporterAsync(Guid transporterId, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        return await context.TrmTrips
            .CountAsync(t => t.TransporterId == transporterId && t.RecordedAt.Date == today, ct);
    }

    public async Task<int> GetNextTripNumberForTodayAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        var maxToday = await context.TrmTrips
            .Where(t => t.RecordedAt.Date == today)
            .MaxAsync(t => (int?)t.TripNumber, ct);
        return (maxToday ?? 0) + 1;
    }

    public async Task AddTripAsync(TrmTrip trip, CancellationToken ct = default)
        => await context.TrmTrips.AddAsync(trip, ct);

    public async Task<TrmOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;

        var todayTrips = await context.TrmTrips
            .Where(t => t.RecordedAt.Date == today)
            .ToListAsync(ct);

        var totalTransporters = await context.TrmTransporters.CountAsync(t => t.IsActive, ct);

        return new TrmOverviewDto
        {
            CollectedToday = todayTrips.Count * FeeRates.TrmTripFee,
            TripsToday = todayTrips.Count,
            TotalTransporters = totalTransporters,
            PendingORCount = todayTrips.Count(t => string.IsNullOrEmpty(t.ORNumber))
        };
    }

    public async Task<IReadOnlyList<TrmTripDto>> GetTodayTripsAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;

        return await context.TrmTrips
            .Include(t => t.Transporter)
            .Where(t => t.RecordedAt.Date == today)
            .OrderByDescending(t => t.RecordedAt)
            .Select(t => new TrmTripDto
            {
                Id = t.Id,
                TransporterId = t.TransporterId,
                TripNumber = t.TripNumber,
                DriverName = t.DriverName,
                Organization = t.Transporter!.Organization,
                PlateNumber = t.PlateNumber,
                Route = t.Route,
                Fee = t.Fee,
                ORNumber = t.ORNumber,
                RecordedAt = t.RecordedAt
            })
            .ToListAsync(ct);
    }

    public async Task<TrmTransporterProfileDto> GetTransporterProfileAsync(Guid transporterId, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;

        var transporter = await context.TrmTransporters
            .FirstOrDefaultAsync(t => t.Id == transporterId, ct);

        if (transporter == null)
            return new TrmTransporterProfileDto();

        var allTrips = await context.TrmTrips
            .Where(t => t.TransporterId == transporterId)
            .OrderByDescending(t => t.RecordedAt)
            .Select(t => new TrmTripDto
            {
                Id = t.Id,
                TransporterId = t.TransporterId,
                TripNumber = t.TripNumber,
                DriverName = t.DriverName,
                Organization = transporter.Organization,
                PlateNumber = t.PlateNumber,
                Route = t.Route,
                Fee = t.Fee,
                ORNumber = t.ORNumber,
                RecordedAt = t.RecordedAt
            })
            .ToListAsync(ct);

        var tripsToday = allTrips.Count(t => t.RecordedAt.Date == today);

        return new TrmTransporterProfileDto
        {
            Id = transporter.Id,
            Name = transporter.Name,
            Organization = transporter.Organization,
            DefaultRoute = transporter.DefaultRoute,
            PlateNumber = transporter.PlateNumber,
            TripsToday = tripsToday,
            TotalTrips = allTrips.Count,
            TotalFees = allTrips.Count * FeeRates.TrmTripFee,
            TripHistory = allTrips
        };
    }

    public async Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct = default)
    {
        if (await context.TrmTrips.AnyAsync(t => t.ORNumber == orNumber, ct)) return false;
        if (await context.TpmAttendances.AnyAsync(a => a.ORNumber == orNumber, ct)) return false;
        if (await context.PaymentRecords.AnyAsync(p => p.ORNumber == orNumber, ct)) return false;
        if (await context.DailyCollections.AnyAsync(d => d.ORNumber == orNumber, ct)) return false;
        return true;
    }
}
