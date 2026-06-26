using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetCollectionReport;

/// <summary>
/// Per-facility collection report for the Export Data page, for one billing month. Composes existing
/// canonical sources (no new aggregation): rental compliance + service-facility month records.
/// </summary>
public record GetCollectionReportQuery(int Year, int Month) : IRequest<Result<CollectionReportDto>>;
