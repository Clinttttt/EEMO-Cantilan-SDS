using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetAllCollectors;

public class GetAllCollectorsQueryHandler(ICollectorRepository collectorRepo) 
    : IRequestHandler<GetAllCollectorsQuery, Result<IReadOnlyList<CollectorListDto>>>
{
    public async Task<Result<IReadOnlyList<CollectorListDto>>> Handle(
        GetAllCollectorsQuery request, 
        CancellationToken cancellationToken)
    {
        var currentMonth = DateTime.UtcNow.Month;
        var currentYear = DateTime.UtcNow.Year;

        var collectors = await collectorRepo.GetAllCollectorsWithStatsAsync(currentYear, currentMonth, cancellationToken);

        return Result<IReadOnlyList<CollectorListDto>>.Success(collectors);
    }
}
