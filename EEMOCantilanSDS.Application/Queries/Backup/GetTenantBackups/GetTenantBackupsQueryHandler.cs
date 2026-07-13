using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetTenantBackups;

public class GetTenantBackupsQueryHandler(ITenantBackupRepository repository)
    : IRequestHandler<GetTenantBackupsQuery, Result<IReadOnlyList<TenantBackupInfo>>>
{
    public async Task<Result<IReadOnlyList<TenantBackupInfo>>> Handle(GetTenantBackupsQuery request, CancellationToken ct)
    {
        var list = await repository.ListAsync(ct);
        return Result<IReadOnlyList<TenantBackupInfo>>.Success(list);
    }
}
