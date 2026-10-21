using EEMOCantilanSDS.Application.Command.Admins.CreateAdmin;
using EEMOCantilanSDS.Application.Command.Admins.InviteAdmin;
using EEMOCantilanSDS.Application.Command.Admins.UpdateAdmin;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Application.Requests.Admins;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class AdminsApiClient(HttpClient http) : HandleResponse(http), IAdminsApiClient
{
    public async Task<Result<IReadOnlyList<AdminListDto>>> GetAllAdminsAsync() =>
        await GetAsync<IReadOnlyList<AdminListDto>>("api/Admins");

    public async Task<Result<AdminInvitationDto>> InviteAdminAsync(InviteAdminCommand command) =>
        await PostAsync<InviteAdminCommand, AdminInvitationDto>("api/Admins/invitations", command);

    public async Task<Result<AdminInvitationDto>> ResendAdminInvitationAsync(Guid id) =>
        await PostAsync<AdminInvitationDto>($"api/Admins/{id}/invitation/resend");

    public async Task<Result<bool>> RevokeAdminInvitationAsync(Guid id) =>
        await PostAsync<bool>($"api/Admins/{id}/invitation/revoke");

    public async Task<Result<AdminDto>> CreateAdminAsync(CreateAdminCommand command) =>
        await PostAsync<CreateAdminCommand, AdminDto>("api/Admins", command);

    public async Task<Result<bool>> UpdateAdminAsync(UpdateAdminCommand command) =>
        await PutAsync<UpdateAdminCommand, bool>($"api/Admins/{command.AdminId}", command);

    public async Task<Result<bool>> ToggleAdminStatusAsync(Guid id, bool isActive) =>
        await UpdateAsync<ToggleAdminStatusRequest, bool>($"api/Admins/{id}/status", new ToggleAdminStatusRequest(isActive));

    public async Task<Result<bool>> ResetAdminPasswordAsync(Guid id, string newPassword, string confirmPassword) =>
        await UpdateAsync<ResetPasswordRequest, bool>($"api/Admins/{id}/reset-password", new ResetPasswordRequest(newPassword, confirmPassword));

    public async Task<Result<bool>> SendAdminEmailVerificationAsync(Guid id) =>
        await PostAsync<object, bool>($"api/Admins/{id}/send-email-verification", new { });
}
