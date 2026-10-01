using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// The signed-in collector's posted canonical Collections for a business-date range, net of corrections. It is the same
/// derivation the collector's Position reads, so Position and the Mobile report cannot state two different canonical totals.
/// The collector and tenant come from the authenticated identity, never from a request value.
/// </summary>
public interface ICollectorCollectionFacts
{
    Task<Result<IReadOnlyList<CollectorCollectionFactDto>>> GetMyCollectionsAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);
}
