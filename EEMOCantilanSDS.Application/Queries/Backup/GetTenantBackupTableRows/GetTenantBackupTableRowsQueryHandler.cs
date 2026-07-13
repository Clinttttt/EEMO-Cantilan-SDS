using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetTenantBackupTableRows;

public class GetTenantBackupTableRowsQueryHandler(ITenantBackupRepository repository)
    : IRequestHandler<GetTenantBackupTableRowsQuery, Result<TenantBackupTableRowsDto>>
{
    // Safety cap on how many records the viewer returns for a single table.
    private const int MaxRows = 500;

    public async Task<Result<TenantBackupTableRowsDto>> Handle(GetTenantBackupTableRowsQuery request, CancellationToken ct)
    {
        var rows = await repository.GetTableRowsAsync(request.Id, request.Table, MaxRows, ct);
        return rows is null
            ? Result<TenantBackupTableRowsDto>.NotFound()
            : Result<TenantBackupTableRowsDto>.Success(rows);
    }
}
