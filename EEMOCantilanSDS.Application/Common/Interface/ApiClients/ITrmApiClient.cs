using EEMOCantilanSDS.Application.Command.TransportTerminal.AddTransporter;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Requests.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ITrmApiClient
{
    Task<Result<TransportationCurrentActivityDto>> GetCurrentAsync(DateOnly from, DateOnly to);
    // Remaining methods retain the legacy trip/registry contracts, not current canonical transportation.
    Task<Result<TrmOverviewDto>> GetOverviewAsync();
    Task<Result<IReadOnlyList<TrmTransporterListDto>>> GetTransportersAsync();
    Task<Result<TrmTransporterProfileDto>> GetTransporterProfileAsync(Guid transporterId);
    Task<Result<TrmTransporterDto>> AddTransporterAsync(AddTransporterCommand command);
    Task<Result<IReadOnlyList<TrmTripDto>>> GetTodayTripsAsync();
    Task<Result<IReadOnlyList<TrmTripDto>>> GetTripsAsync(int year, int month);
    Task<Result<TrmHistoryDto>> GetHistoryAsync(int year);
    Task<Result<TrmTripDto>> RecordTripAsync(Guid transporterId, RecordTripRequest request);
    Task<Result<bool>> SaveOrNumberAsync(Guid tripId, SaveTripOrNumberRequest request);
}
