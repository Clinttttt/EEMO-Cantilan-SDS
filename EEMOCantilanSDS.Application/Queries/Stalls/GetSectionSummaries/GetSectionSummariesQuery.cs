using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetSectionSummaries;

public record GetSectionSummariesQuery(FacilityCode FacilityCode, int Year, int Month) : IRequest<Result<Dictionary<MarketSection, StallSummaryDto>>>;
