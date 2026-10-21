using System.Text.Json;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Reads the facts an itemized governed-service line froze when it was posted (the approved charge name, and for Slaughterhouse the animal
/// and heads). Read-only: an unreadable or older snapshot simply has no such fact, and nothing is inferred from amounts or today's schedule.
/// </summary>
public static class FrozenCollectionFacts
{
    /// <summary>The approved charge's own name ("Table Charge"), or null for a line that froze none.</summary>
    public static string? ChargeName(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return null;
        try
        {
            using var document = JsonDocument.Parse(snapshot);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("feeOptionName", out var label) || label.ValueKind != JsonValueKind.String)
                return null;
            var name = label.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>The animal and heads a Slaughterhouse line was for ("Carabao × 2"), or null when none was frozen.</summary>
    public static string? SlaughterSubject(IEnumerable<string?> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshot)) continue;
            try
            {
                using var document = JsonDocument.Parse(snapshot);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object
                    || !source.TryGetProperty("animal", out var animal) || animal.ValueKind != JsonValueKind.String) continue;
                var name = source.TryGetProperty("customAnimalName", out var custom) && custom.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(custom.GetString()) ? custom.GetString()!.Trim() : animal.GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                return source.TryGetProperty("heads", out var heads) && heads.ValueKind == JsonValueKind.Number && heads.TryGetInt32(out var n) && n > 0
                    ? $"{name} × {n}" : name;
            }
            catch (JsonException) { /* an unreadable snapshot just has no animal line */ }
        }
        return null;
    }
}
