namespace EEMOCantilanSDS.Application.Dtos.Settings;

/// <summary>
/// A municipality's operation directory configuration, as the Head Portal reads it.
/// </summary>
/// <param name="UsesConfiguration">
/// False for a municipality that has not adopted tenant configuration (every one activated before it existed): then EVERY
/// operation is listed under its established name, exactly as before. It is not "nothing is enabled".
/// </param>
public sealed record TenantOperationSettingsDto(bool UsesConfiguration, IReadOnlyList<TenantOperationSettingDto> Items);

/// <param name="OperationCode">Stable identity. Never derived from a label.</param>
/// <param name="DefaultLabel">The neutral standard name.</param>
/// <param name="LocalDisplayName">The municipality's own name, or null.</param>
/// <param name="DisplayName">What the directory prints: local name, else (adopted) the standard name, else (legacy) the established name.</param>
/// <param name="IsListed">Presentation only - listing is not financial authorization.</param>
/// <param name="Version">Concurrency token for edits; 0 when the municipality has no stored row yet.</param>
/// <param name="PolicyNotice">Set for a municipality OTHER than the default (Cantilan) where the platform applies a rule accepted only for Cantilan's office; the Head must confirm it. Informational: it does not change what is enforced.</param>
/// <param name="AssignedCollectors">How many Collectors the Head has assigned to the operation. A FACT, not a readiness verdict: an operation collected at the office needs none, and an assigned one may still lack its source, rate or documents.</param>
/// <param name="HasRecordedActivity">Collections already recorded under the operation's classification. Hiding it never removes them.</param>
public sealed record TenantOperationSettingDto(
    string OperationCode,
    string DefaultLabel,
    string GroupKey,
    string? LocalDisplayName,
    string DisplayName,
    bool IsListed,
    long Version,
    string Origin,
    bool HasRecordedActivity,
    string? PolicyNotice = null,
    int AssignedCollectors = 0);
