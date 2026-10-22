using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Domain.Entities.Revenue;

/// <summary>
/// Municipality-owned section configuration. Stable section identity is NEVER derived from a local name.
/// The enabled flag gates NEW Terminal collection activity but does not alter historical records.
/// </summary>
public sealed class TerminalSectionSetting : AuditableEntity, IMunicipalityOwned
{
    public Guid MunicipalityId { get; private set; }
    public TerminalSection Section { get; private set; }
    public bool IsEnabled { get; private set; }
    public string? LocalDisplayName { get; private set; }
    public long Version { get; private set; } = 1;

    private TerminalSectionSetting() { }

    public static TerminalSectionSetting Create(TerminalSection section, bool isEnabled, string? localDisplayName,
        Guid municipalityId, string actor)
    {
        if (!Enum.IsDefined(section) || municipalityId == Guid.Empty) throw new ArgumentException("Invalid tenant section identity.");
        var label = NormalizeName(localDisplayName);
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("An actor is required.", nameof(actor));
        return new TerminalSectionSetting {
            Id = Guid.NewGuid(), MunicipalityId = municipalityId, Section = section,
            IsEnabled = isEnabled, LocalDisplayName = label, Version = 1, CreatedAt = DateTime.UtcNow, CreatedBy = actor
        };
    }

    public void Update(bool isEnabled, string? localDisplayName, string actor)
    {
        var label = NormalizeName(localDisplayName);
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("An actor is required.", nameof(actor));
        if (IsEnabled == isEnabled && LocalDisplayName == label) return;
        IsEnabled = isEnabled;
        LocalDisplayName = label;
        Version++;
        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = actor;
    }

    public static string? NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > 80 || trimmed.Any(char.IsControl))
            throw new ArgumentException("Section name must be at most 80 characters without control characters.");
        return trimmed;
    }
}
