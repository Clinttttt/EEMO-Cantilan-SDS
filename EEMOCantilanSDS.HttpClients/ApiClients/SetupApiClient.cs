using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.CreateFirstAdmin;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Queries.Auth.GetSetupStatus;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class SetupApiClient : HandleResponse, ISetupApiClient
{
    public SetupApiClient(HttpClient http) : base(http)
    {
    }

    public async Task<Result<SetupStatusDto>> GetSetupStatusAsync() => 
        await GetAsync<SetupStatusDto>("api/Setup/status");

    public async Task<Result<bool>> CreateFirstAdminAsync(CreateFirstAdminCommand command) => 
        await PostAsync<CreateFirstAdminCommand, bool>("api/Setup/create-first-admin", command);
}
