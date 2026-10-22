using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>The Head's direct-collection review: a server-paged read of canonical Collections plus an administrative review that never moves money.</summary>
public interface IDirectCollectionReviewApiClient
{
    Task<Result<DirectCollectionReviewPageDto>> GetAsync(DirectCollectionReviewRequest request);
    Task<Result<DirectCollectionVerificationPositionDto>> PositionAsync(DateOnly from, DateOnly to, bool scoped = false);
    Task<Result<DirectCollectionDetailDto>> DetailAsync(Guid collectionId);
    Task<Result<DirectCollectionReviewRowDto>> ReviewAsync(DirectCollectionReviewActionRequest request);
    Task<Result<DirectCollectionBulkConfirmResult>> ConfirmManyAsync(DirectCollectionBulkConfirmRequest request);
}
