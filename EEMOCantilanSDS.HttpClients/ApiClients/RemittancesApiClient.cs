using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class RemittancesApiClient(HttpClient http) : HandleResponse(http), IRemittancesApiClient
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public Task<Result<RemittanceScopeDto>> GetScopeAsync(Guid collectorId, DateOnly from, DateOnly to, RevenueInstrumentType? instrument) =>
        GetAsync<RemittanceScopeDto>($"api/remittances/scope?collectorId={collectorId}&from={D(from)}&to={D(to)}"
            + (instrument is { } i ? $"&instrument={(int)i}" : string.Empty));

    public Task<Result<RemittanceDetailDto>> RecordAsync(RecordRemittanceRequest request) =>
        PostAsync<RecordRemittanceRequest, RemittanceDetailDto>("api/remittances", request);

    public Task<Result<RemittanceDetailDto>> VoidAsync(Guid id, VoidRemittanceRequest request) =>
        PostAsync<VoidRemittanceRequest, RemittanceDetailDto>($"api/remittances/{id}/void", request);

    public Task<Result<IReadOnlyList<RemittanceRowDto>>> GetRegisterAsync(
        DateOnly from, DateOnly to, Guid? collectorId, RevenueInstrumentType? instrument, RemittanceStatus? status) =>
        GetAsync<IReadOnlyList<RemittanceRowDto>>($"api/remittances?from={D(from)}&to={D(to)}"
            + (collectorId is { } c ? $"&collectorId={c}" : string.Empty)
            + (instrument is { } i ? $"&instrument={(int)i}" : string.Empty)
            + (status is { } s ? $"&status={(int)s}" : string.Empty));

    public Task<Result<RemittanceDetailDto>> GetDetailAsync(Guid id) =>
        GetAsync<RemittanceDetailDto>($"api/remittances/{id}");

    public Task<Result<AccountabilityPositionDto>> GetPositionAsync(DateOnly from, DateOnly to) =>
        GetAsync<AccountabilityPositionDto>($"api/remittances/position?from={D(from)}&to={D(to)}");

    public Task<Result<int>> ReturnUnusedAsync(ReturnUnusedFormsRequest request) =>
        PostAsync<ReturnUnusedFormsRequest, int>("api/accountable-forms/return", request);

    public Task<Result<SpoiledFormDto>> SpoilAsync(SpoilFormRequest request) =>
        PostAsync<SpoilFormRequest, SpoiledFormDto>("api/accountable-forms/spoilage", request);

    public Task<Result<IReadOnlyList<SpoiledFormDto>>> GetSpoiledAsync() =>
        GetAsync<IReadOnlyList<SpoiledFormDto>>("api/accountable-forms/spoilage");
}
