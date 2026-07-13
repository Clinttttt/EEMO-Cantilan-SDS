namespace EEMOCantilanSDS.Application.Dtos.Backup;

/// <summary>
/// The actual records of ONE table inside a stored backup, rendered as a column/row grid of string
/// values for display. Capped for safety (<see cref="Truncated"/> flags when more rows exist than were
/// returned). Read-only inspection of the caller's own backup — no restore is performed.
/// </summary>
public sealed record TenantBackupTableRowsDto(
    string Table,
    string DisplayName,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string?>> Rows,
    int TotalRows,
    bool Truncated);
