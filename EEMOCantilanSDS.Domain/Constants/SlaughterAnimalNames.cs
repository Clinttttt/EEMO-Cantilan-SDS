using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Domain.Constants;

/// <summary>Stable built-in slaughterhouse identities and their canonical display fallbacks.</summary>
public static class SlaughterAnimalNames
{
    public static string Normalize(string? name) =>
        (name ?? string.Empty).Trim().ToUpperInvariant();

    public static bool Same(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    public static bool HasDuplicates(IEnumerable<string?> names) =>
        names.Select(Normalize).GroupBy(name => name, StringComparer.Ordinal).Any(group => group.Count() > 1);

    public static bool CollidesWithAny(string? candidate, IEnumerable<string?> names) =>
        names.Any(name => Same(candidate, name));

    /// <summary>
    /// A repeated transaction may use an established custom name with the same spelling. A spelling that differs only
    /// by case would create two visible identities for one animal and is therefore rejected.
    /// </summary>
    public static bool UsesEstablishedSpelling(string? candidate, IEnumerable<string?> existingNames)
    {
        var trimmed = (candidate ?? string.Empty).Trim();
        var collisions = existingNames
            .Where(name => Same(name, trimmed))
            .Select(name => (name ?? string.Empty).Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return collisions.Count == 0 || (collisions.Count == 1 && collisions[0] == trimmed);
    }

    public static readonly IReadOnlyList<AnimalType> BuiltIns =
        [AnimalType.Hog, AnimalType.Carabao, AnimalType.Cow];

    public static string Canonical(AnimalType type) => type switch
    {
        AnimalType.Hog => "Hog",
        AnimalType.Carabao => "Carabao",
        AnimalType.Cow => "Cow",
        _ => "Other",
    };

    public static bool IsBuiltIn(AnimalType type) => BuiltIns.Contains(type);

    public static bool IsCompatibleBuiltInLabel(AnimalType identity, string? label)
    {
        var normalized = Normalize(label);
        var aliasIdentity = normalized switch
        {
            "HOG" or "PIG" or "SWINE" => AnimalType.Hog,
            "CARABAO" or "BUFFALO" => AnimalType.Carabao,
            "COW" or "CATTLE" or "BULL" => AnimalType.Cow,
            _ => (AnimalType?)null,
        };

        // "Large animal" names the shared rate category, not either canonical animal identity.
        if (normalized is "LARGE ANIMAL" or "LARGE-ANIMAL") return false;
        return aliasIdentity is null || aliasIdentity == identity;
    }

    public static bool BuiltInLabelsAreUnambiguous(string? hog, string? carabao, string? cow) =>
        !HasDuplicates([hog, carabao, cow])
        && IsCompatibleBuiltInLabel(AnimalType.Hog, hog)
        && IsCompatibleBuiltInLabel(AnimalType.Carabao, carabao)
        && IsCompatibleBuiltInLabel(AnimalType.Cow, cow);

    /// <summary>Names reserved for canonical identities; custom animals may not take them.</summary>
    public static bool IsCanonicalNameOrAlias(string? name)
    {
        var normalized = Normalize(name);
        return normalized is "HOG" or "PIG" or "SWINE"
            or "CARABAO" or "BUFFALO"
            or "COW" or "CATTLE" or "BULL" or "LARGE ANIMAL" or "LARGE-ANIMAL";
    }
}
