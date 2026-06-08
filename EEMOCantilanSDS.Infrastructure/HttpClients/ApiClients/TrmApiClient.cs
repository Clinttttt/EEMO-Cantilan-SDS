using EEMOCantilanSDS.Application.Command.TransportTerminal.AddTransporter;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Application.Requests.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;

public class TrmApiClient(HttpClient http) : HandleResponse(http), ITrmApiClient
{
    public async Task<Result<TrmOverviewDto>> GetOverviewAsync() =>
        await GetAsync<TrmOverviewDto>("api/trm/overview");

    public async Task<Result<IReadOnlyList<TrmTransporterListDto>>> GetTransportersAsync() =>
        await GetAsync<IReadOnlyList<TrmTransporterListDto>>("api/trm/transporters");

    public async Task<Result<TrmTransporterProfileDto>> GetTransporterProfileAsync(Guid transporterId) =>
        await GetAsync<TrmTransporterProfileDto>($"api/trm/transporters/{transporterId}");

    public async Task<Result<TrmTransporterDto>> AddTransporterAsync(AddTransporterCommand command) =>
        await PostAsync<AddTransporterCommand, TrmTransporterDto>("api/trm/transporters", command);

    public async Task<Result<IReadOnlyList<TrmTripDto>>> GetTodayTripsAsync() =>
        await GetAsync<IReadOnlyList<TrmTripDto>>("api/trm/trips/today");

    public async Task<Result<IReadOnlyList<TrmTripDto>>> GetTripsAsync(int year, int month) =>
        await GetAsync<IReadOnlyList<TrmTripDto>>($"api/trm/trips?year={year}&month={month}");

    public async Task<Result<TrmHistoryDto>> GetHistoryAsync(int year) =>
        await GetAsync<TrmHistoryDto>($"api/trm/history?year={year}");

    public async Task<Result<TrmTripDto>> RecordTripAsync(Guid transporterId, RecordTripRequest request) =>
        await PostAsync<RecordTripRequest, TrmTripDto>($"api/trm/trips/{transporterId}", request);

    public async Task<Result<bool>> SaveOrNumberAsync(Guid tripId, SaveTripOrNumberRequest request) =>
        await UpdateAsync<SaveTripOrNumberRequest, bool>($"api/trm/trips/{tripId}/or-number", request);
}
