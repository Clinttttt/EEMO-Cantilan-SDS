using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>The unified office Collection Activity (<c>GET api/collections/activity</c>).</summary>
public interface ICollectionActivityApiClient
{
    Task<Result<CollectionActivityFeedDto>> GetAsync(DateOnly from, DateOnly to, FacilityCode? facility = null,
        Guid? collectorId = null, string? authority = null, int limit = 500);
}
