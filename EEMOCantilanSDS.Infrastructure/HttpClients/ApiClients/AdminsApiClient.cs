using EEMOCantilanSDS.Application.Command.Admins.CreateAdmin;
using EEMOCantilanSDS.Application.Command.Admins.UpdateAdmin;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Requests.Admins;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Infrastructure.HttpClients.ApiClients;

public class AdminsApiClient(HttpClient http) : HandleResponse(http), IAdminsApiClient
{
    public async Task<Result<IReadOnlyList<AdminListDto>>> GetAllAdminsAsync() =>
        await GetAsync<IReadOnlyList<AdminListDto>>("api/Admins");

    public async Task<Result<AdminDto>> CreateAdminAsync(CreateAdminCommand command) =>
        await PostAsync<CreateAdminCommand, AdminDto>("api/Admins", command);

    public async Task<Result<bool>> UpdateAdminAsync(UpdateAdminCommand command) =>
        await PutAsync<UpdateAdminCommand, bool>($"api/Admins/{command.AdminId}", command);

    public async Task<Result<bool>> ToggleAdminStatusAsync(Guid id, bool isActive) =>
        await UpdateAsync<ToggleAdminStatusRequest, bool>($"api/Admins/{id}/status", new ToggleAdminStatusRequest(isActive));

    public async Task<Result<bool>> ResetAdminPasswordAsync(Guid id, string newPassword) =>
        await UpdateAsync<ResetPasswordRequest, bool>($"api/Admins/{id}/reset-password", new ResetPasswordRequest(newPassword));
}
