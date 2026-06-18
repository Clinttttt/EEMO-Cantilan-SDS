using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorRecords;

/// <summary>
/// The authenticated collector's own collection records for a PH date range, optionally narrowed to a
/// single facility. The collector is resolved from the token server-side; results are inherently scoped
/// to their assignments because only their own (CollectorId) collections are returned.
/// </summary>
public record GetCollectorRecordsQuery(FacilityCode? Facility, DateOnly FromDate, DateOnly ToDate)
    : IRequest<Result<IReadOnlyList<MobileCollectorRecordDto>>>;
