using System.Text.RegularExpressions;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// Presentation of an audit event's action. The stored value is never changed: Created / Updated / Deleted keep their short uppercase label and any
/// other event name (for example <c>WcfMobileCollectionEnabled</c>) is read as words, so a long internal code never overflows its column.
/// </summary>
public static partial class AuditActionLabel
{
    private static readonly HashSet<string> Core = new(StringComparer.OrdinalIgnoreCase) { "Created", "Updated", "Deleted" };
    private static readonly HashSet<string> Acronyms = new(StringComparer.OrdinalIgnoreCase)
        { "Wcf", "Ecf", "Npm", "Tcc", "Ncc", "Ice", "Or", "Ct", "Src", "Lgu", "Api", "Id" };

    public static bool IsCore(string action) => Core.Contains(action);

    public static string For(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "";
        if (IsCore(action)) return action.ToUpperInvariant();
        var words = WordBoundary().Split(action.Replace('_', ' ').Trim()).Where(w => w.Length > 0).ToArray();
        var text = string.Join(' ', words.Select((w, i) => Acronyms.Contains(w) ? w.ToUpperInvariant() : i == 0 ? w : w.ToLowerInvariant()));
        return text.Length == 0 ? action : char.ToUpperInvariant(text[0]) + text[1..];
    }

    // Splits "WcfMobileCollectionEnabled" and "WCFMobileCollectionEnabled" at each word start, and leaves a trailing digit run attached.
    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|\s+")]
    private static partial Regex WordBoundary();
}
