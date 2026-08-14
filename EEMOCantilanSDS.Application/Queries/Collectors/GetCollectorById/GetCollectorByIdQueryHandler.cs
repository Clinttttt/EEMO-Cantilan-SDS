using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetCollectorById;

public class GetCollectorByIdQueryHandler(ICollectorReportingQueries collectorRepo, IClock clock) 
    : IRequestHandler<GetCollectorByIdQuery, Result<CollectorActivityDto>>
{
    public async Task<Result<CollectorActivityDto>> Handle(
        GetCollectorByIdQuery request, 
        CancellationToken cancellationToken)
    {
        var currentMonth = clock.PhilippineNow.Month;
        var currentYear = clock.PhilippineNow.Year;

        var dto = await collectorRepo.GetCollectorActivityAsync(request.CollectorId, currentYear, currentMonth, cancellationToken);

        if (dto is null)
            return Result<CollectorActivityDto>.NotFound();

        return Result<CollectorActivityDto>.Success(dto);
    }
}