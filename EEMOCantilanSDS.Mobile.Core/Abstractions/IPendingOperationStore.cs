using EEMOCantilanSDS.Mobile.Models;

namespace EEMOCantilanSDS.Mobile.Abstractions;

/// <summary>
/// On-device persistence for queued offline collections. Survives app restarts so a collection
/// captured with no signal is never lost before it syncs.
/// </summary>
public interface IPendingOperationStore
{
    /// <summary>All queued rows (any local status), newest first.</summary>
    Task<IReadOnlyList<PendingOperation>> GetAllAsync();

    /// <summary>Appends a newly-captured operation.</summary>
    Task AddAsync(PendingOperation operation);

    /// <summary>Replaces the stored row that shares the same <c>ClientOperationId</c> (no-op if absent).</summary>
    Task UpdateAsync(PendingOperation operation);

    /// <summary>Removes the row with the given idempotency key (e.g. after a successful sync).</summary>
    Task RemoveAsync(Guid clientOperationId);
}
