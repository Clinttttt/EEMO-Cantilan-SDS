namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// Idempotency lookups for offline sync. A client-generated operation id is stamped on the created
/// collection record; this checks whether an operation has already been persisted to any collection
/// table so a replayed/duplicated offline operation is never processed twice.
/// </summary>
public interface ISyncRepository
{
    Task<bool> IsOperationProcessedAsync(Guid clientOperationId, CancellationToken cancellationToken = default);
}
