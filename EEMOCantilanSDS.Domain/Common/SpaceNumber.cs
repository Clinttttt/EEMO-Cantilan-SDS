namespace EEMOCantilanSDS.Domain.Common;

/// <summary>
/// The identifier carried by a space the office does not number.
///
/// <para>
/// A stall let under a signed contract has a number on the office's own list. A space let WITHOUT one — a barbecue
/// stand, an ice-plant space, a commercial-centre space held on an extension — has no number there at all: the sheet
/// leaves the contract columns blank and identifies the occupancy by the lessee. The system nonetheless requires an
/// identifier for every space it records.
/// </para>
///
/// <para>
/// It used to satisfy that requirement by continuing the facility's ordinary numbering, which quietly spent numbers
/// belonging to real stalls: an un-numbered commercial-centre space was recorded as "4", and the office could then no
/// longer register the actual stall 4, because the number was reported as occupied. The sheet blanks the number for
/// such a row, so the office could not even see which of its numbers had been taken.
/// </para>
///
/// <para>
/// Un-numbered spaces therefore carry their own series — <c>SP-1</c>, <c>SP-2</c> — which cannot collide with a
/// numeric stall number and reads on screen as what it is. The series is a plain identifier string, so nothing that
/// keys, routes or links by stall number needs to change.
/// </para>
/// </summary>
public static class SpaceNumber
{
    /// <summary>Marks an identifier as belonging to a space the office does not number.</summary>
    public const string Prefix = "SP-";

    /// <summary>True where the identifier is one of ours rather than an office stall number.</summary>
    public static bool IsSpace(string? stallNo) =>
        !string.IsNullOrWhiteSpace(stallNo)
        && stallNo.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The identifier for the nth un-numbered space.</summary>
    public static string Format(int ordinal) => $"{Prefix}{ordinal}";

    /// <summary>
    /// The highest ordinal already used among the given identifiers, or zero where none are ours. Anything after the
    /// prefix that is not a whole number is ignored rather than throwing: the column is free text the office types
    /// into, and a malformed entry must not stop an import.
    /// </summary>
    public static int HighestOrdinal(IEnumerable<string?> stallNos)
    {
        var highest = 0;
        foreach (var no in stallNos)
        {
            if (!IsSpace(no)) continue;
            var tail = no!.Trim()[Prefix.Length..];
            if (int.TryParse(tail, out var ordinal) && ordinal > highest) highest = ordinal;
        }
        return highest;
    }

    /// <summary>
    /// What the office sees. A numbered stall shows its number; a space the office does not number shows NOTHING,
    /// because that is what its own list shows — the column is left empty and the occupancy is identified by the
    /// lessee. The identifier still exists and is still what the system keys, links and routes on; it is simply not a
    /// fact about the space that the office would recognise, so it is not put in front of them.
    /// </summary>
    public static string Display(string? stallNo) =>
        IsSpace(stallNo) ? string.Empty : (stallNo ?? string.Empty).Trim();

    /// <summary>
    /// How the identifier reads in a sentence — "Stall 4". Empty for a space the office does not number, for the same
    /// reason as <see cref="Display"/>: naming it would assert a numbering that does not exist. Callers composing a
    /// label from several parts must therefore drop empty ones rather than emitting a stray separator.
    /// </summary>
    public static string Describe(string? stallNo) =>
        IsSpace(stallNo) ? string.Empty : $"Stall {stallNo}";

    /// <summary>
    /// Joins the parts of a one-line label with " · ", dropping any that are absent.
    ///
    /// <para>This lives here because the absence it exists for originates here: the stall part of a label is
    /// legitimately empty for a space the office does not number, and interpolating it directly left lines reading
    /// " · Fish Area" or "Stall  · TCC" with a separator and nothing before it. Every screen that names a space needs
    /// the same rule, so it is stated once rather than copied into each page.</para>
    /// </summary>
    public static string Line(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
