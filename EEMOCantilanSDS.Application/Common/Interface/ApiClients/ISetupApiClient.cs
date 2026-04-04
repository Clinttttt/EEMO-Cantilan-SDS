using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.CreateFirstAdmin;
using EEMOCantilanSDS.Application.Queries.Auth.GetSetupStatus;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

public interface ISetupApiClient
{
    Task<Result<SetupStatusDto>> GetSetupStatusAsync();
    Task<Result<bool>> CreateFirstAdminAsync(CreateFirstAdminCommand command);
}
