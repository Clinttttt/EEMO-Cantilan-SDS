namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>
/// A tenant's stable internal revenue identity and its policy effective as of the requested business date.
/// A null EffectivePolicy means no version is effective yet; HasPolicyVersions distinguishes that from no
/// policy history having been configured at all.
/// </summary>
public sealed record RevenueClassificationDto(
    Guid Id,
    string SemanticCode,
    bool IsActive,
    bool HasPolicyVersions,
    RevenueClassificationPolicyDto? EffectivePolicy);
