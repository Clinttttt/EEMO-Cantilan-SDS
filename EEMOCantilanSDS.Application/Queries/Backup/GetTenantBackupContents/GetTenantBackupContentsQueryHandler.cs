using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetTenantBackupContents;

public class GetTenantBackupContentsQueryHandler(ITenantBackupRepository repository)
    : IRequestHandler<GetTenantBackupContentsQuery, Result<TenantBackupContentsDto>>
{
    public async Task<Result<TenantBackupContentsDto>> Handle(GetTenantBackupContentsQuery request, CancellationToken ct)
    {
        var contents = await repository.GetContentsAsync(request.Id, ct);
        return contents is null
            ? Result<TenantBackupContentsDto>.NotFound()
            : Result<TenantBackupContentsDto>.Success(contents);
    }
}
