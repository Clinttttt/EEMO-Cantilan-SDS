using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.HttpClients.Helper;
using System.Net.Http;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;

public class CollectorsApiClient(HttpClient http) : HandleResponse(http), ICollectorsApiClient
{
    private readonly HttpClient _http = http;

    public async Task<Result<IReadOnlyList<CollectorListDto>>> GetAllCollectorsAsync() =>
        await GetAsync<IReadOnlyList<CollectorListDto>>("api/Collectors");

    public async Task<Result<CollectorActivityDto>> GetCollectorByIdAsync(Guid id) =>
        await GetAsync<CollectorActivityDto>($"api/Collectors/{id}");

    public async Task<Result<CollectorDto>> CreateCollectorAsync(CreateCollectorCommand command) =>
        await PostAsync<CreateCollectorCommand, CollectorDto>("api/Collectors", command);

    public async Task<Result<string>> GetNextEmployeeIdAsync() =>
        await _http.GetPlainStringAsync("api/Collectors/next-employee-id");
}
