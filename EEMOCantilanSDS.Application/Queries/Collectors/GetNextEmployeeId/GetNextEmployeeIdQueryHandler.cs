using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetNextEmployeeId;

public class GetNextEmployeeIdQueryHandler(ICollectorRepository collectorRepo) 
    : IRequestHandler<GetNextEmployeeIdQuery, Result<string>>
{
    public async Task<Result<string>> Handle(GetNextEmployeeIdQuery request, CancellationToken cancellationToken)
    {
        var nextId = await collectorRepo.GenerateNextEmployeeIdAsync(cancellationToken);
        return Result<string>.Success(nextId);
    }
}
