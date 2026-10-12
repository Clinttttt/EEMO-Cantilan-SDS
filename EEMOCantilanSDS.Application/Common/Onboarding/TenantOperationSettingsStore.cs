using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Onboarding;

/// <summary>
/// Reads and (once, explicitly) adopts a municipality's operation configuration. The one place that knows what "no rows"
/// means, so the legacy fallback cannot drift between the directory, the settings page and the tests.
/// </summary>
public static class TenantOperationSettingsStore
{
    /// <summary>
    /// The municipality's configuration. No stored rows means not adopted: every operation is listed under its established name.
    /// </summary>
    public static async Task<TenantOperationSettingsDto> ReadAsync(IAppDbContext context, Guid municipalityId, CancellationToken ct)
    {
        var rows = await context.TenantOperationSettings.AsNoTracking()
            .Where(s => s.MunicipalityId == municipalityId)
            .ToListAsync(ct);

        var withActivity = await OperationsWithRecordedActivityAsync(context, municipalityId, ct);
        var byCode = rows.ToDictionary(r => r.OperationCode, StringComparer.Ordinal);
        var adopted = rows.Count > 0;

        var items = TenantOperationCatalog.All.Select(entry =>
        {
            byCode.TryGetValue(entry.Code, out var row);
            var listed = adopted ? row?.IsListed ?? false : true;
            var label = row?.LocalDisplayName;
            var display = adopted ? label ?? entry.DefaultLabel : entry.LegacyDirectoryLabel;
            return new TenantOperationSettingDto(
                entry.Code, entry.DefaultLabel, entry.GroupKey, label, display, listed, row?.Version ?? 0,
                row?.Origin ?? (adopted ? "Head" : "Legacy"), withActivity.Contains(entry.ClassificationCode));
        }).ToList();

        return new TenantOperationSettingsDto(adopted, items);
    }

    /// <summary>
    /// Records the legacy state as explicit rows - every operation listed, under the wording it already has - so a municipality
    /// can then edit one of them without the others disappearing. Called only from an authorised edit, never on read.
    /// </summary>
    public static IReadOnlyList<TenantOperationSetting> AdoptLegacy(IAppDbContext context, Guid municipalityId, string by)
    {
        var rows = TenantOperationCatalog.All.Select(entry => TenantOperationSetting.Create(
            entry.Code, isListed: true,
            // Keep the established name as the recorded label only where it differs from the standard one, so adopting
            // renames nothing on the municipality's screens.
            localDisplayName: string.Equals(entry.LegacyDirectoryLabel, entry.DefaultLabel, StringComparison.Ordinal) ? null : entry.LegacyDirectoryLabel,
            origin: "Legacy", municipalityId, by)).ToList();
        context.TenantOperationSettings.AddRange(rows);
        return rows;
    }

    private static async Task<HashSet<string>> OperationsWithRecordedActivityAsync(IAppDbContext context, Guid municipalityId, CancellationToken ct)
    {
        var codes = await (
            from line in context.CollectionLines.AsNoTracking()
            join classification in context.RevenueClassifications.AsNoTracking() on line.RevenueClassificationId equals classification.Id
            where line.MunicipalityId == municipalityId
            select classification.SemanticCode).Distinct().ToListAsync(ct);
        return codes.ToHashSet(StringComparer.Ordinal);
    }
}
