using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.HttpClients.ApiClients;

public sealed class OfficialReportsApiClient(HttpClient http) : HandleResponse(http), IOfficialReportsApiClient
{
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public Task<Result<OfficialMonthlyIncomeDto>> GetMonthlyIncomeAsync(int year, int? month) =>
        GetAsync<OfficialMonthlyIncomeDto>($"api/official-reports/monthly-income?year={year}" + (month is { } m ? $"&month={m}" : string.Empty));

    public Task<Result<CollectionsRegisterDto>> GetCollectionsAsync(
        DateOnly from, DateOnly to, Guid? collectorId, RevenueInstrumentType? instrument, Guid? classificationId) =>
        GetAsync<CollectionsRegisterDto>($"api/official-reports/collections?from={D(from)}&to={D(to)}"
            + (collectorId is { } c ? $"&collectorId={c}" : string.Empty)
            + (instrument is { } i ? $"&instrument={(int)i}" : string.Empty)
            + (classificationId is { } k ? $"&classificationId={k}" : string.Empty));

    public Task<Result<CollectionDocumentDto>> GetCollectionAsync(Guid collectionId) =>
        GetAsync<CollectionDocumentDto>($"api/official-reports/collections/{collectionId}");

    public Task<Result<DocumentTraceDto>> TraceDocumentAsync(string documentNumber) =>
        GetAsync<DocumentTraceDto>($"api/official-reports/documents/{Uri.EscapeDataString(documentNumber)}");
}
