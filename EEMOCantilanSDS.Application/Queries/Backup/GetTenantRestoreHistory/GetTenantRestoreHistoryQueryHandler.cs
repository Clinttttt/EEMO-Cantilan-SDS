using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetTenantRestoreHistory;

public class GetTenantRestoreHistoryQueryHandler(ITenantBackupRepository repository)
    : IRequestHandler<GetTenantRestoreHistoryQuery, Result<IReadOnlyList<TenantRestoreEventDto>>>
{
    public async Task<Result<IReadOnlyList<TenantRestoreEventDto>>> Handle(GetTenantRestoreHistoryQuery request, CancellationToken ct)
    {
        var list = await repository.ListRestoreEventsAsync(20, ct);
        return Result<IReadOnlyList<TenantRestoreEventDto>>.Success(list);
    }
}
