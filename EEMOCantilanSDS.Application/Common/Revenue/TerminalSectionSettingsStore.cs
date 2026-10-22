using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Tenant-scoped terminal-section resolution. Existing municipalities with NO rows keep all three legacy choices;
/// an explicitly configured municipality stores all three rows, including disabled ones.
/// </summary>
public static class TerminalSectionSettingsStore
{
    public sealed record SectionView(TerminalSection Section, string DisplayName, string? LocalDisplayName,
        bool IsEnabled, long Version);

    public static IReadOnlyList<SectionView> Project(IReadOnlyCollection<TerminalSectionSetting> rows)
    {
        var bySection = rows.ToDictionary(x => x.Section);
        return Enum.GetValues<TerminalSection>().Select(section =>
        {
            bySection.TryGetValue(section, out var row);
            return new SectionView(section, row?.LocalDisplayName ?? OfficeCollectionWorkflow.SectionName(section),
                row?.LocalDisplayName, rows.Count == 0 || row?.IsEnabled == true, row?.Version ?? 0);
        }).ToArray();
    }

    public static async Task<IReadOnlyList<SectionView>> ReadAsync(IAppDbContext db, Guid tenantId, CancellationToken ct)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("A municipality is required.", nameof(tenantId));
        var rows = await db.TerminalSectionSettings.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId).ToListAsync(ct);
        return Project(rows);
    }

    public static async Task<bool> AllowsAsync(IAppDbContext db, Guid tenantId, TerminalSection section, CancellationToken ct)
    {
        if (!Enum.IsDefined(section) || tenantId == Guid.Empty) return false;
        var rows = await db.TerminalSectionSettings.AsNoTracking().Where(x => x.MunicipalityId == tenantId)
            .Select(x => new { x.Section, x.IsEnabled }).ToListAsync(ct);
        return rows.Count == 0 || rows.Any(x => x.Section == section && x.IsEnabled);
    }
}
