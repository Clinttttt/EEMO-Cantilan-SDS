using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Entities.Suggestions;

/// <summary>
/// A curated blocklist entry: a pick-list value (driver/route/organization/goods) that should no
/// longer be suggested to collectors. Pick-lists are derived from records, so this is the only
/// non-destructive way to stop suggesting a value (e.g. a typo) without altering historical data.
/// </summary>
public class HiddenSuggestion : AuditableEntity, IMunicipalityOwned
{
    /// <inheritdoc />
    public Guid MunicipalityId { get; private set; }
    public SuggestionType Type { get; private set; }
    public string Value { get; private set; } = string.Empty;

    private HiddenSuggestion() { } // EF Core

    public static HiddenSuggestion Create(SuggestionType type, string value, string hiddenBy)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", nameof(value));

        return new HiddenSuggestion
        {
            Id = Guid.NewGuid(),
            Type = type,
            Value = value.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = hiddenBy
        };
    }
}
