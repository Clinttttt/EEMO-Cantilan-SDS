using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetSectionSummaries;

public class GetSectionSummariesQueryHandler(IStallRepository stallRepository) : IRequestHandler<GetSectionSummariesQuery, Result<Dictionary<MarketSection, StallSummaryDto>>>
{
    public async Task<Result<Dictionary<MarketSection, StallSummaryDto>>> Handle(GetSectionSummariesQuery request, CancellationToken ct)
    {
        var summaries = await stallRepository.GetSectionSummariesAsync(request.FacilityCode, request.Year, request.Month, ct);
        return Result<Dictionary<MarketSection, StallSummaryDto>>.Success(summaries);
    }
}
