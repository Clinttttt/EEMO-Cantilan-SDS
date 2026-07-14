using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpHistoryYears;

public class GetFollowUpHistoryYearsQueryHandler(IFacilityReportsRepository reportsRepository)
    : IRequestHandler<GetFollowUpHistoryYearsQuery, Result<IReadOnlyList<int>>>
{
    public async Task<Result<IReadOnlyList<int>>> Handle(GetFollowUpHistoryYearsQuery request, CancellationToken ct)
    {
        var current = PhilippineTime.Today.Year;
        var earliest = await reportsRepository.GetEarliestActivityYearAsync(ct);
        if (earliest > current) earliest = current;

        var years = new List<int>();
        for (var y = current; y >= earliest; y--)
            years.Add(y);

        return Result<IReadOnlyList<int>>.Success(years);
    }
}
