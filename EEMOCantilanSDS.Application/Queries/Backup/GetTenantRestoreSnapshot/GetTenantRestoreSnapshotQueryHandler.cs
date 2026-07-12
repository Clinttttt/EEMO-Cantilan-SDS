using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.GetTenantRestoreSnapshot;

public class GetTenantRestoreSnapshotQueryHandler(ITenantRestoreRepository repository)
    : IRequestHandler<GetTenantRestoreSnapshotQuery, Result<BackupArtifact>>
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public async Task<Result<BackupArtifact>> Handle(GetTenantRestoreSnapshotQuery request, CancellationToken ct)
    {
        var snapshot = await repository.CreateSnapshotAsync(ct);
        var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);

        var stamp = snapshot.GeneratedAtUtc.ToString("yyyyMMdd-HHmmss");
        var code = string.IsNullOrWhiteSpace(snapshot.TenantCode) ? "tenant" : snapshot.TenantCode;
        var fileName = $"stalltrack-{code}-restore-{stamp}.json";

        return Result<BackupArtifact>.Success(new BackupArtifact(fileName, json, "application/json"));
    }
}
