using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Requests.Collectors;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.HttpClients.Helper;
using System.Net.Http;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

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

    public async Task<Result<int>> SendNotificationAsync(Guid collectorId, string title, string body) =>
        await PostAsync<EEMOCantilanSDS.Application.Requests.Notifications.SendCollectorNotificationRequest, int>(
            $"api/Notifications/collectors/{collectorId}/send",
            new EEMOCantilanSDS.Application.Requests.Notifications.SendCollectorNotificationRequest(title, body));

    public async Task<Result<EEMOCantilanSDS.Application.Dtos.Settings.MobileBindLinkDto>> GetCollectorAppLinkAsync(bool rotate = false) =>
        await PostAsync<object, EEMOCantilanSDS.Application.Dtos.Settings.MobileBindLinkDto>(
            $"api/municipality-profile/bind-link?rotate={(rotate ? "true" : "false")}", new { });
}
