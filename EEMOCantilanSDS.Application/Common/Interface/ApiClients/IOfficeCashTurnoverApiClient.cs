using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Office Cash Turnover (<c>api/collections/office-turnovers</c>): Head and Admin read, the Head records and voids.</summary>
public interface IOfficeCashTurnoverApiClient
{
    Task<Result<bool>> IsAvailableAsync();
    Task<Result<OfficeCashWorkspaceDto>> GetWorkspaceAsync(OfficeCashFilterRequest request);
    Task<Result<OfficeCashPositionDto>> GetPositionAsync(DateOnly from, DateOnly to);
    Task<Result<OfficeCashTurnoverReviewDto>> GetReviewAsync(DateOnly from, DateOnly to);
    Task<Result<IReadOnlyList<OfficeCashTurnoverRowDto>>> GetRegisterAsync(DateOnly from, DateOnly to);
    Task<Result<OfficeCashTurnoverDetailDto>> GetDetailAsync(Guid id);
    Task<Result<OfficeCashTurnoverDetailDto>> RecordAsync(RecordOfficeCashTurnoverRequest request);
    Task<Result<OfficeCashTurnoverDetailDto>> VoidAsync(Guid id, VoidOfficeCashTurnoverRequest request);
}
