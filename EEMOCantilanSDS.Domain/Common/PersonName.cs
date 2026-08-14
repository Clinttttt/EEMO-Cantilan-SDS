using System.Text.RegularExpressions;

namespace EEMOCantilanSDS.Domain.Common;

/// <summary>
/// Decides when two typed names are the same person.
/// <para>
/// The slaughterhouse module has no client entity: an owner IS the name a clerk typed, and that name is what ties a
/// transaction to a receipt and to a client's history. Exact string comparison therefore made ordinary typing differences
/// into different people. Two consequences, both real:
/// </para>
/// <list type="bullet">
///   <item>
///     The office's rule is that two animals butchered on one receipt share its OR number. The OR check asked whether the
///     number already belonged to a "different owner" using exact equality, so entering the second animal as
///     "Juan dela Cruz" when the first was "Juan Dela Cruz" made the office's own receipt look like another person's and
///     the OR was refused. The clerk could not record an animal on the receipt they had already issued.
///   </item>
///   <item>
///     A client's history and monthly totals were split across spellings, understating what that client had actually paid.
///   </item>
/// </list>
/// <para>
/// Matching is on a canonical form: outer whitespace trimmed, internal runs of whitespace collapsed to one space, and case
/// ignored. "JUAN DELA CRUZ", "Juan Dela Cruz" and "Juan  dela cruz " are one person.
/// </para>
/// <para>
/// What this deliberately does NOT do is decide that two different people who share a name are one person. Nothing derivable
/// from a name can tell them apart; that needs a client record the office can distinguish, which is a decision for the
/// office and not something to infer here. This narrows the fault to genuine namesakes instead of leaving it open to every
/// stray capital letter.
/// </para>
/// <para>
/// Canonical form preserves the capitalisation that was typed, because that is what prints on the office's documents. Only
/// comparison ignores case.
/// </para>
/// </summary>
public static class PersonName
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// The form to STORE and display: trimmed, with internal whitespace runs collapsed to a single space. Capitalisation is
    /// left exactly as typed. Null or blank becomes an empty string.
    /// </summary>
    public static string Canonical(string? raw)
        => string.IsNullOrWhiteSpace(raw) ? string.Empty : WhitespaceRun.Replace(raw.Trim(), " ");

    /// <summary>
    /// The form to COMPARE by: the canonical form, lowercased with invariant casing so the result does not depend on the
    /// server's locale. Use this as a dictionary or grouping key; never show it to a user.
    /// </summary>
    public static string MatchKey(string? raw) => Canonical(raw).ToLowerInvariant();

    /// <summary>True when both names denote the same person by the rule above. Two blanks are not a match.</summary>
    public static bool Matches(string? left, string? right)
    {
        var key = MatchKey(left);
        return key.Length > 0 && key == MatchKey(right);
    }

    /// <summary>
    /// Groups names by person while keeping a typed name to display. Ordinal-ignore-case over canonical forms, so callers
    /// that group in memory agree with the database comparisons.
    /// </summary>
    public static IEqualityComparer<string> Comparer { get; } = new CanonicalComparer();

    private sealed class CanonicalComparer : IEqualityComparer<string>
    {
        public bool Equals(string? x, string? y) => MatchKey(x) == MatchKey(y);

        public int GetHashCode(string obj) => MatchKey(obj).GetHashCode(StringComparison.Ordinal);
    }
}
