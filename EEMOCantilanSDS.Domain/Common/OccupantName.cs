namespace EEMOCantilanSDS.Domain.Common;

/// <summary>
/// Resolves the name to show for a stall's occupant.
/// <para>
/// The occupant is held as free text, and the office's source lists use that same column for a status when a space
/// is not occupied. Three New Public Market contracts were imported with the literal word "Closed" as the occupant
/// while the real lessee's name sat in the contract-name column, so the Register of Inactive Stall Accounts printed
/// "Closed" where a person belongs. A status word is not a name: where one is found, the name on the signed contract
/// is used instead, and only if that is missing does the caller's own fallback apply.
/// </para>
/// <para>
/// This is a read-side correction. It does not alter what is stored, so the original import remains auditable.
/// </para>
/// </summary>
public static class OccupantName
{
    /// <summary>Words the office's own forms use as a status in the occupant column. Compared case-insensitively.</summary>
    private static readonly HashSet<string> StatusWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "closed", "close", "expired", "lapsed", "vacant", "unoccupied", "vacated",
        "none", "n/a", "na", "no occupant", "not occupied", "inactive",
    };

    /// <summary>True when a stored occupant value is a status rather than a person.</summary>
    public static bool IsStatusWord(string? value)
        => !string.IsNullOrWhiteSpace(value) && StatusWords.Contains(value.Trim());

    /// <summary>
    /// The occupant to display: the stored occupant, unless it is blank or a status word, in which case the name on
    /// the signed contract. Returns an empty string when neither states a person, leaving the wording to the caller.
    /// </summary>
    public static string Resolve(string? actualOccupant, string? nameOnContract)
    {
        if (!string.IsNullOrWhiteSpace(actualOccupant) && !IsStatusWord(actualOccupant))
            return actualOccupant.Trim();

        if (!string.IsNullOrWhiteSpace(nameOnContract) && !IsStatusWord(nameOnContract))
            return nameOnContract.Trim();

        return string.Empty;
    }
}
