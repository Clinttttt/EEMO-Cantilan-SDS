using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>Office view of remittance and liquidation, collector positions and form accountability (IA-052).</summary>
public interface IRemittancesApiClient
{
    Task<Result<RemittanceScopeDto>> GetScopeAsync(Guid collectorId, DateOnly from, DateOnly to, RevenueInstrumentType? instrument);
    Task<Result<RemittanceDetailDto>> RecordAsync(RecordRemittanceRequest request);

    /// <summary>One independent remittance per collector, saved together or not at all (POST api/remittances/batch).</summary>
    Task<Result<IReadOnlyList<RemittanceDetailDto>>> RecordBatchAsync(RecordRemittanceBatchRequest request);
    Task<Result<RemittanceDetailDto>> VoidAsync(Guid id, VoidRemittanceRequest request);
    Task<Result<IReadOnlyList<RemittanceRowDto>>> GetRegisterAsync(
        DateOnly from, DateOnly to, Guid? collectorId, RevenueInstrumentType? instrument, RemittanceStatus? status);
    /// <summary>The register with a multi-collector submission shown as one row (GET api/remittances/history).</summary>
    Task<Result<IReadOnlyList<RemittanceHistoryRowDto>>> GetHistoryAsync(
        DateOnly from, DateOnly to, Guid? collectorId, RevenueInstrumentType? instrument, RemittanceStatus? status);
    Task<Result<RemittanceSubmissionDto>> GetSubmissionAsync(Guid submissionId);
    Task<Result<RemittanceDetailDto>> GetDetailAsync(Guid id);
    Task<Result<AccountabilityPositionDto>> GetPositionAsync(DateOnly from, DateOnly to);
    Task<Result<int>> ReturnUnusedAsync(ReturnUnusedFormsRequest request);
    Task<Result<SpoiledFormDto>> SpoilAsync(SpoilFormRequest request);
    Task<Result<IReadOnlyList<SpoiledFormDto>>> GetSpoiledAsync();
}
