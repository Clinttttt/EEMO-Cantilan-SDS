using EEMOCantilanSDS.Application.Command.Admins.CreateAdmin;
using EEMOCantilanSDS.Application.Command.Admins.UpdateAdmin;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface IAdminsApiClient
{
    Task<Result<IReadOnlyList<AdminListDto>>> GetAllAdminsAsync();
    Task<Result<AdminDto>> CreateAdminAsync(CreateAdminCommand command);
    Task<Result<bool>> UpdateAdminAsync(UpdateAdminCommand command);
    Task<Result<bool>> ToggleAdminStatusAsync(Guid id, bool isActive);
    Task<Result<bool>> ResetAdminPasswordAsync(Guid id, string newPassword);
}
