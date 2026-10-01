using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.ApiClients;

/// <summary>The official financial readers: Monthly Income, the Collections register with its RCD-style summary, and the serial trace.</summary>
public interface IOfficialReportsApiClient
{
    Task<Result<OfficialMonthlyIncomeDto>> GetMonthlyIncomeAsync(int year, int? month);

    /// <summary>Every revenue source for the period, with the official Monthly Income money and model-aware counts.</summary>
    Task<Result<RevenueSourcePerformanceDto>> GetSourcePerformanceAsync(int year, int? month);

    Task<Result<CollectionsRegisterDto>> GetCollectionsAsync(
        DateOnly from, DateOnly to, Guid? collectorId, RevenueInstrumentType? instrument, Guid? classificationId);

    Task<Result<CollectionDocumentDto>> GetCollectionAsync(Guid collectionId);

    Task<Result<DocumentTraceDto>> TraceDocumentAsync(string documentNumber);
}
