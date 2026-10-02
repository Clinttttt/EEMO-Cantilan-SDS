using EEMOCantilanSDS.Application.Dtos.Collections;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// Reads one tenant's authoritative collection events for a period, exactly once: legacy source rows only while their legacy
/// money is authoritative, posted canonical Collections only where their lines are the authoritative money
/// (<see cref="Domain.Constants.CollectionSourceAuthorityMap"/>). Never drafts, assessments, opening settlement, remittances,
/// shadow rows or online-provider transactions. Read-only.
/// </summary>
public interface ICollectionActivityReader
{
    Task<IReadOnlyList<CollectionActivityEventDto>> GetAsync(Guid tenantId, DateOnly from, DateOnly to, CancellationToken ct = default);
}
