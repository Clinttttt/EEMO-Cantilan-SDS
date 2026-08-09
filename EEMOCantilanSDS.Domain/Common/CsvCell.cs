namespace EEMOCantilanSDS.Domain.Common;

/// <summary>
/// Writes one value into a CSV cell.
///
/// <para>
/// Quoting alone is not enough. The office opens these files in Excel, and Excel treats a cell beginning with
/// <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> as a FORMULA rather than as text - so an occupant name recorded as
/// <c>=HYPERLINK("http://…","Click")</c> becomes a live link in the exported register, and other functions can
/// reach the file system or the network. The import accepts such a name today: it validates length and rejects a
/// short list of placeholder words, nothing more. Four separate exporters each escaped quotes correctly and none
/// of them considered this.
/// </para>
///
/// <para>
/// A leading apostrophe is the standard remedy: Excel takes the rest of the cell as literal text. It is visible
/// in the file, which is the honest trade - a government export should rather show an odd-looking name than run
/// something when the office double-clicks it. Values that do not start with one of those characters are written
/// exactly as they are, so ordinary names, numbers and dates are untouched.
/// </para>
/// </summary>
public static class CsvCell
{
    // Tab and carriage return are included because Excel strips leading whitespace before deciding whether a cell
    // is a formula, so " =CMD()" is as dangerous as "=CMD()".
    private static readonly char[] FormulaStarters = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>
    /// The value as a CSV field: neutralised if Excel would read it as a formula, then quoted if it contains a
    /// comma, a quote or a newline. Null becomes an empty field.
    /// </summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var text = value;

        // Compared against the value with leading whitespace ignored, for the reason above.
        var firstMeaningful = text.TrimStart();
        if (firstMeaningful.Length > 0 && FormulaStarters.Contains(firstMeaningful[0]))
            text = "'" + text;

        return text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r')
            ? "\"" + text.Replace("\"", "\"\"") + "\""
            : text;
    }

    /// <summary>A whole row, comma-separated, each field escaped.</summary>
    public static string Row(params string?[] fields) => string.Join(',', fields.Select(Escape));
}
