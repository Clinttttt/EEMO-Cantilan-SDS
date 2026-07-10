namespace EEMOCantilanSDS.Application.Dtos.SystemHealth;

/// <summary>
/// Per-tenant storage footprint for the CALLER'S municipality only. Every figure is scoped to the
/// authenticated caller's MunicipalityId (the DbContext tenant filter + an explicit MunicipalityId
/// predicate) — it never reflects another municipality or the whole shared database. This is the
/// per-LGU counterpart to the platform-operator-only whole-database <c>DatabaseHealthDto</c>.
/// </summary>
public record TenantUsageDto(
    long EstimatedSizeBytes,
    long TotalRecords,
    IReadOnlyList<TenantUsageItem> Breakdown,
    DateTime CollectedAt);

/// <summary>One category's record count and estimated byte footprint within the caller's tenant.</summary>
public record TenantUsageItem(string Category, long Records, long EstimatedSizeBytes);
