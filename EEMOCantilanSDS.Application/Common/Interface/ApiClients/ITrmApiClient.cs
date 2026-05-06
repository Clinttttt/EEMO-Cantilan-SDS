using EEMOCantilanSDS.Application.Command.TransportTerminal.AddTransporter;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Requests.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ITrmApiClient
{
    Task<Result<TrmOverviewDto>> GetOverviewAsync();
    Task<Result<IReadOnlyList<TrmTransporterListDto>>> GetTransportersAsync();
    Task<Result<TrmTransporterProfileDto>> GetTransporterProfileAsync(Guid transporterId);
    Task<Result<TrmTransporterDto>> AddTransporterAsync(AddTransporterCommand command);
    Task<Result<IReadOnlyList<TrmTripDto>>> GetTodayTripsAsync();
    Task<Result<TrmTripDto>> RecordTripAsync(Guid transporterId, RecordTripRequest request);
    Task<Result<bool>> SaveOrNumberAsync(Guid tripId, SaveTripOrNumberRequest request);
}
