using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public class LifecycleApiClient(HttpClient http) : HandleResponse(http), ILifecycleApiClient
{
    // The source goes in the route by name, never by number.
    private static string Route(LifecycleSource source, Guid accountId) => $"api/AccountLifecycle/{source}/{accountId}";

    public async Task<Result<AccountLifecycleDto>> GetAsync(LifecycleSource source, Guid accountId) =>
        await GetAsync<AccountLifecycleDto>(Route(source, accountId));

    public async Task<Result<IReadOnlyList<AccountLifecycleDto>>> GetManyAsync(LifecycleSource source, IReadOnlyList<Guid> accountIds) =>
        await PostAsync<IReadOnlyList<Guid>, IReadOnlyList<AccountLifecycleDto>>($"api/AccountLifecycle/{source}/batch", accountIds);

    public async Task<Result<LifecyclePreviewDto>> PreviewAsync(LifecycleSource source, Guid accountId, LifecycleCommand command) =>
        await PostAsync<LifecycleCommand, LifecyclePreviewDto>($"{Route(source, accountId)}/preview", command);

    public async Task<Result<LifecycleOutcome>> ExecuteAsync(LifecycleSource source, Guid accountId, LifecycleExecution execution) =>
        await PostAsync<LifecycleExecution, LifecycleOutcome>($"{Route(source, accountId)}/execute", execution);
}
