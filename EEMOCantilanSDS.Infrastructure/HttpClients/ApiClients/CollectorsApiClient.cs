using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Requests.Collectors;
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

    public async Task<Result<bool>> UpdateCollectorAsync(UpdateCollectorCommand command) =>
        await PutAsync<UpdateCollectorCommand, bool>($"api/Collectors/{command.CollectorId}", command);

    public async Task<Result<bool>> ToggleCollectorStatusAsync(Guid id, bool isActive) =>
        await UpdateAsync<ToggleCollectorStatusRequest, bool>($"api/Collectors/{id}/status", new ToggleCollectorStatusRequest(isActive));

    public async Task<Result<bool>> ResetCollectorPasswordAsync(Guid id, string newPassword, string confirmPassword) =>
        await UpdateAsync<ResetCollectorPasswordRequest, bool>($"api/Collectors/{id}/reset-password", new ResetCollectorPasswordRequest(newPassword, confirmPassword));

    public async Task<Result<string>> GetNextEmployeeIdAsync() =>
        await _http.GetPlainStringAsync("api/Collectors/next-employee-id");
}
