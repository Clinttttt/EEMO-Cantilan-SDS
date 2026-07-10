namespace EEMOCantilanSDS.Application.Dtos.Backup;

/// <summary>
/// A logical, per-tenant data export assembled from the CALLER'S own municipality rows only. Each table
/// entry holds the caller's rows for one operational/financial entity. Credential material (user accounts,
/// activation codes) is deliberately excluded. This is the per-LGU counterpart to the platform-operator
/// whole-database backup.
/// </summary>
public record TenantExportPayload(
    string TenantCode,
    string MunicipalityName,
    Guid MunicipalityId,
    DateTime GeneratedAtUtc,
    IReadOnlyDictionary<string, object> Tables);
