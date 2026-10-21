using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Head follow-up for posted direct Fish/Meat Vendor Fee collections that name a payer but no registered vendor.</summary>
public interface IFishMeatPayorFollowUpApiClient
{
    Task<Result<FishMeatPayorFollowUpPageDto>> ListAsync(string? search, int skip, int take);
    Task<Result<FishMeatPayorFollowUpDetailDto>> DetailAsync(Guid collectionId);
    Task<Result<FishMeatPayorResolveResult>> ResolveAsync(FishMeatPayorResolveRequest request);
}
