using System.Globalization;
using System.Text.RegularExpressions;

namespace EEMOCantilanSDS.Domain.Common;

/// <summary>
/// The structure of a printed accountable-form serial such as <c>2315601 A</c>: a literal prefix, one run of digits, and a
/// literal suffix. The printed value is the authoritative identity and is always kept verbatim; the prefix and suffix are
/// literal text that is repeated unchanged across a range. The suffix carries NO meaning here (IA-059): it is never read as a
/// series, variant, copy type or year, never stripped and never generated.
/// </summary>
public sealed record AccountableSerial(string Raw, string Prefix, long Number, int Digits, string Suffix)
{
    public const int MaxDigits = 18;

    private static readonly Regex Shape = new(@"^(?<prefix>\D*)(?<digits>\d+)(?<suffix>\D*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The key used to detect the same physical serial written two harmless ways ("2315601 A" and "2315601a"): upper-cased,
    /// with all whitespace removed. It is a lookup key only; the printed value stays authoritative.
    /// </summary>
    public static string Normalize(string? printed) =>
        string.Concat((printed ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    /// <summary>Parses one printed serial. False when it cannot be read as prefix + one digit run + suffix.</summary>
    public static bool TryParse(string? printed, out AccountableSerial serial)
    {
        serial = default!;
        var raw = (printed ?? string.Empty).Trim();
        if (raw.Length is 0 or > 100) return false;
        var match = Shape.Match(raw);
        if (!match.Success) return false;
        var digits = match.Groups["digits"].Value;
        if (digits.Length > MaxDigits || !long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number)) return false;
        serial = new AccountableSerial(raw, match.Groups["prefix"].Value, number, digits.Length, match.Groups["suffix"].Value);
        return true;
    }

    /// <summary>The printed form of another number in the same sequence: same literal prefix, zero-padding and suffix.</summary>
    public string Format(long number) =>
        Prefix + number.ToString($"D{Digits}", CultureInfo.InvariantCulture) + Suffix;

    /// <summary>
    /// Reads a printed range. It is enumerable only when both ends share the same literal prefix and suffix (ignoring case and
    /// whitespace) and the last serial is exactly what counting up from the first would print. Anything else is refused rather
    /// than guessed, so the office registers it as separate ranges.
    /// </summary>
    public static bool TryParseRange(string? first, string? last, out AccountableSerial start, out long lastNumber, out string error)
    {
        lastNumber = 0;
        start = default!;
        if (!TryParse(first, out start) || !TryParse(last, out var end))
        {
            error = "Enter each serial exactly as printed, with one run of digits (for example 2315601 A).";
            return false;
        }
        if (Normalize(start.Prefix) != Normalize(end.Prefix) || Normalize(start.Suffix) != Normalize(end.Suffix))
        {
            error = "The first and last serial must share the same printed prefix and suffix. Register different series as separate ranges.";
            return false;
        }
        if (end.Number < start.Number)
        {
            error = "The last serial must not come before the first.";
            return false;
        }
        if (Normalize(start.Format(end.Number)) != Normalize(end.Raw))
        {
            error = "The last serial does not follow the first one's numbering. Register it as a separate range.";
            return false;
        }
        lastNumber = end.Number;
        error = string.Empty;
        return true;
    }
}
