using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetCollectorById;

public class GetCollectorByIdQueryHandler(ICollectorRepository collectorRepo) 
    : IRequestHandler<GetCollectorByIdQuery, Result<CollectorActivityDto>>
{
    public async Task<Result<CollectorActivityDto>> Handle(
        GetCollectorByIdQuery request, 
        CancellationToken cancellationToken)
    {
        var currentMonth = DateTime.UtcNow.Month;
        var currentYear = DateTime.UtcNow.Year;

        var dto = await collectorRepo.GetCollectorActivityAsync(request.CollectorId, currentYear, currentMonth, cancellationToken);

        if (dto is null)
            return Result<CollectorActivityDto>.NotFound();

        return Result<CollectorActivityDto>.Success(dto);
    }
}
