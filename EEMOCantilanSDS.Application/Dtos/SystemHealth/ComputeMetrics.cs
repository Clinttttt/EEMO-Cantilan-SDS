namespace EEMOCantilanSDS.Application.Dtos.SystemHealth;

/// <summary>
/// Host compute metrics for the database server, read from Azure Monitor (not PostgreSQL, which cannot
/// report host CPU/memory). CPU/Memory are null when the metric is unavailable (no permission / not
/// configured / transient), so the UI shows "—" rather than a fabricated value.
/// </summary>
public sealed record ComputeMetrics(double? CpuPercent, double? MemoryPercent, long ProvisionedStorageBytes);
