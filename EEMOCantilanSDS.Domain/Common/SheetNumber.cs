using System.Globalization;
using System.Text;

namespace EEMOCantilanSDS.Domain.Common;

/// <summary>
/// Reads a number the office typed into a spreadsheet cell.
///
/// <para>
/// The office's own lists are written by different hands, so an amount arrives as <c>1,500.00</c> or <c>1.500,00</c>
/// or <c>₱1,500</c> or <c>1500</c>. The import used to strip every comma and then match digits, which turned
/// <c>1.500,00</c> into <c>1.50000</c> and read it as <b>1.50</b>: a space let at fifteen hundred pesos a month was
/// imported as costing one peso fifty, and it passed every validation on the way through because 1.50 is a perfectly
/// valid rate.
/// </para>
///
/// <para>
/// Which separator is the decimal point is therefore decided before anything is stripped, the way a reader decides
/// it: the LAST separator present is the decimal point, since a grouping separator can never be the final one. A
/// single separator followed by exactly three digits is grouping — <c>1,500</c> and <c>1.500</c> are both fifteen
/// hundred. The result always uses '.' as the decimal point so the caller can parse it invariantly, whatever culture
/// the server happens to be running under.
/// </para>
/// </summary>
public static class SheetNumber
{
    /// <summary>
    /// The cell's number, written with '.' as the decimal point and no grouping, or empty where the cell holds no
    /// number at all. Never throws: the column is free text and a bad cell must not stop an import.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var text = raw.Trim();
        var decimalSeparator = DecimalSeparatorOf(text);

        var sb = new StringBuilder(text.Length);
        var seenDecimal = false;
        foreach (var ch in text)
        {
            if (ch == '-' && sb.Length == 0) { sb.Append('-'); continue; }
            if (char.IsDigit(ch)) { sb.Append(ch); continue; }
            if (decimalSeparator is { } sep && ch == sep && !seenDecimal)
            {
                sb.Append('.');
                seenDecimal = true;
            }
            // Everything else — the grouping separator, a peso sign, spaces — is dropped.
        }

        var match = System.Text.RegularExpressions.Regex.Match(sb.ToString(), @"-?\d+(\.\d+)?");
        return match.Success ? match.Value : string.Empty;
    }

    /// <summary>The cell's value as a decimal, or null where the cell holds no number.</summary>
    public static decimal? ToDecimal(string? raw)
    {
        var normalized = Normalize(raw);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static char? DecimalSeparatorOf(string text)
    {
        var lastComma = text.LastIndexOf(',');
        var lastPoint = text.LastIndexOf('.');

        // Both present: the later one is the decimal point, because grouping separators come first.
        if (lastComma >= 0 && lastPoint >= 0)
            return lastComma > lastPoint ? ',' : '.';

        if (lastComma < 0 && lastPoint < 0) return null;

        // One separator. Exactly three trailing digits is grouping; anything else is a decimal fraction.
        var only = lastComma >= 0 ? ',' : '.';
        var at = lastComma >= 0 ? lastComma : lastPoint;
        var tail = text[(at + 1)..];
        var isGrouping = tail.Length == 3 && tail.All(char.IsDigit);
        return isGrouping ? null : only;
    }
}
