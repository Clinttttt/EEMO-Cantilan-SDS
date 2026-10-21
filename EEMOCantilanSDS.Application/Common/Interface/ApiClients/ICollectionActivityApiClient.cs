using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>The unified office Collection Activity (<c>GET api/collections/activity</c>).</summary>
public interface ICollectionActivityApiClient
{
    Task<Result<CollectionReconciliationDto>> GetReconciliationAsync(DateOnly from, DateOnly to,
        CollectionReconciliationBasis basis, DateTimeOffset? asOf = null, int limit = 500);
    Task<Result<CollectionActivityFeedDto>> GetAsync(DateOnly from, DateOnly to, FacilityCode? facility = null,
        Guid? collectorId = null, string? authority = null, int limit = 500, string? reference = null);
    Task<Result<CollectionReviewDto>> GetReviewAsync(Guid collectionId);
    Task<Result<HeadCorrectionResultDto>> ReverseAsync(HeadReverseCollectionRequest request);
}
