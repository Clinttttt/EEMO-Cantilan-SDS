using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

public interface ITrmRepository
{
    // Transporters
    Task<TrmTransporter?> GetTransporterByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<TrmTransporterListDto>> GetTransportersWithTodayTripsAsync(CancellationToken ct = default);
    Task AddTransporterAsync(TrmTransporter transporter, CancellationToken ct = default);

    // Trips
    Task<TrmTrip?> GetTripByIdAsync(Guid id, CancellationToken ct = default);
    Task<int> GetTodayTripCountForTransporterAsync(Guid transporterId, CancellationToken ct = default);
    Task<int> GetNextTripNumberForTodayAsync(CancellationToken ct = default);
    Task AddTripAsync(TrmTrip trip, CancellationToken ct = default);

    // Dashboard queries — repo returns DTOs directly
    Task<TrmOverviewDto> GetOverviewAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TrmTripDto>> GetTodayTripsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TrmTripDto>> GetTripsByMonthAsync(int year, int month, CancellationToken ct = default);
    Task<TrmHistoryDto> GetHistoryAsync(int year, CancellationToken ct = default);
    Task<TrmTransporterProfileDto> GetTransporterProfileAsync(Guid transporterId, CancellationToken ct = default);

    // Validation
    Task<bool> IsORNumberUniqueAsync(string orNumber, CancellationToken ct = default);
}
