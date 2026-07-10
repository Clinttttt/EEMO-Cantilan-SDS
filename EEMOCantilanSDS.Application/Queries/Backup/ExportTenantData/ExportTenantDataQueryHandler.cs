using System.Text.Json;
using System.Text.Json.Serialization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Backup;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Backup.ExportTenantData;

public class ExportTenantDataQueryHandler(ITenantExportRepository repository)
    : IRequestHandler<ExportTenantDataQuery, Result<BackupArtifact>>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,          // EF navigation loops never break the export
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<Result<BackupArtifact>> Handle(ExportTenantDataQuery request, CancellationToken cancellationToken)
    {
        var payload = await repository.ExportAsync(cancellationToken);
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, Options);

        var stamp = payload.GeneratedAtUtc.ToString("yyyyMMdd-HHmmss");
        var code = string.IsNullOrWhiteSpace(payload.TenantCode) ? "tenant" : payload.TenantCode;
        var fileName = $"stalltrack-{code}-{stamp}.json";

        return Result<BackupArtifact>.Success(new BackupArtifact(fileName, json, "application/json"));
    }
}
