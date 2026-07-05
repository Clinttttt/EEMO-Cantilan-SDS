using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.SystemHealth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.SystemHealth.GetDatabaseHealth;

public class GetDatabaseHealthQueryHandler(IDatabaseHealthRepository repository)
    : IRequestHandler<GetDatabaseHealthQuery, Result<DatabaseHealthDto>>
{
    public async Task<Result<DatabaseHealthDto>> Handle(GetDatabaseHealthQuery request, CancellationToken cancellationToken)
    {
        var health = await repository.GetHealthAsync(cancellationToken);
        return Result<DatabaseHealthDto>.Success(health);
    }
}
