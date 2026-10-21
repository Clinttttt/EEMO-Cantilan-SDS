using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>The shared account lifecycle: the server's view of one account, a preview of an action, and its confirmed execution.</summary>
public interface ILifecycleApiClient
{
    Task<Result<AccountLifecycleDto>> GetAsync(LifecycleSource source, Guid accountId);
    Task<Result<IReadOnlyList<AccountLifecycleDto>>> GetManyAsync(LifecycleSource source, IReadOnlyList<Guid> accountIds);
    Task<Result<LifecyclePreviewDto>> PreviewAsync(LifecycleSource source, Guid accountId, LifecycleCommand command);
    Task<Result<LifecycleOutcome>> ExecuteAsync(LifecycleSource source, Guid accountId, LifecycleExecution execution);
}
