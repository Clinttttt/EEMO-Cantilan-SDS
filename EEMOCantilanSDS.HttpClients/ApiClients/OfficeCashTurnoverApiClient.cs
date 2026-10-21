using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class OfficeCashTurnoverApiClient(HttpClient http) : HandleResponse(http), IOfficeCashTurnoverApiClient
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public Task<Result<bool>> IsAvailableAsync() => GetAsync<bool>("api/collections/office-turnovers/availability");
    public Task<Result<OfficeCashWorkspaceDto>> GetWorkspaceAsync(OfficeCashFilterRequest request) =>
        GetAsync<OfficeCashWorkspaceDto>($"api/collections/office-turnovers/workspace?from={D(request.From)}&to={D(request.To)}"
            + (request.AuthorityId is { } authority ? $"&authorityId={authority:D}" : "")
            + (request.Instrument is { } instrument ? $"&instrument={(int)instrument}" : ""));
    public Task<Result<OfficeCashPositionDto>> GetPositionAsync(DateOnly from, DateOnly to) =>
        GetAsync<OfficeCashPositionDto>($"api/collections/office-turnovers/position?from={D(from)}&to={D(to)}");
    public Task<Result<OfficeCashTurnoverReviewDto>> GetReviewAsync(DateOnly from, DateOnly to) =>
        GetAsync<OfficeCashTurnoverReviewDto>($"api/collections/office-turnovers/review?from={D(from)}&to={D(to)}");
    public Task<Result<IReadOnlyList<OfficeCashTurnoverRowDto>>> GetRegisterAsync(DateOnly from, DateOnly to) =>
        GetAsync<IReadOnlyList<OfficeCashTurnoverRowDto>>($"api/collections/office-turnovers?from={D(from)}&to={D(to)}");
    public Task<Result<OfficeCashTurnoverDetailDto>> GetDetailAsync(Guid id) =>
        GetAsync<OfficeCashTurnoverDetailDto>($"api/collections/office-turnovers/{id}");
    public Task<Result<OfficeCashTurnoverDetailDto>> RecordAsync(RecordOfficeCashTurnoverRequest request) =>
        PostAsync<RecordOfficeCashTurnoverRequest, OfficeCashTurnoverDetailDto>("api/collections/office-turnovers", request);
    public Task<Result<OfficeCashTurnoverDetailDto>> VoidAsync(Guid id, VoidOfficeCashTurnoverRequest request) =>
        PostAsync<VoidOfficeCashTurnoverRequest, OfficeCashTurnoverDetailDto>($"api/collections/office-turnovers/{id}/void", request);
}
