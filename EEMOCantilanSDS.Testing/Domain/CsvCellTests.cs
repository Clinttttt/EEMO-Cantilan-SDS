using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Writing a value into a CSV cell the office will open in Excel.
///
/// <para>
/// Four exporters each escaped quotes correctly and none of them considered that Excel treats a cell beginning
/// with <c>=</c>, <c>+</c>, <c>-</c> or <c>@</c> as a FORMULA. The import accepts such a name today - it checks
/// length and rejects a short list of placeholder words, nothing more - so an occupant recorded as
/// <c>=HYPERLINK(...)</c> became a live link in the exported register.
/// </para>
/// </summary>
public class CsvCellTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\",\"Click\")")]
    [InlineData("=1+1")]
    [InlineData("+1234567890")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1:A9)")]
    public void ACellExcelWouldRunAsAFormulaIsNeutralised(string dangerous)
    {
        var escaped = CsvCell.Escape(dangerous);

        // The apostrophe makes Excel read the rest as literal text. Visible in the file, which is the honest
        // trade: a government export should show an odd-looking name rather than run something on a double-click.
        Assert.StartsWith("'", escaped.TrimStart('"'));
        Assert.Contains(dangerous.Split('(')[0], escaped);
    }

    [Fact]
    public void LeadingWhitespaceDoesNotSmuggleAFormulaThrough()
    {
        // Excel strips leading whitespace before deciding, so " =CMD()" is as dangerous as "=CMD()".
        Assert.Contains("'", CsvCell.Escape(" =cmd()"));
        Assert.Contains("'", CsvCell.Escape("\t=cmd()"));
    }

    [Theory]
    [InlineData("Juan Dela Cruz")]
    [InlineData("Maria Clara Santos")]
    [InlineData("900.00")]
    [InlineData("8/1/2026")]
    [InlineData("SP-1")]
    [InlineData("No contract (space only)")]
    public void AnOrdinaryValueIsWrittenExactlyAsItIs(string ordinary)
    {
        // The remedy must not disfigure the register. Names, money, dates and the office's own wording go through
        // untouched, or the office would stop trusting the export.
        Assert.Equal(ordinary, CsvCell.Escape(ordinary));
    }

    [Fact]
    public void QuotingStillFollowsRfc4180()
    {
        Assert.Equal("\"Cruz, Juan\"", CsvCell.Escape("Cruz, Juan"));
        Assert.Equal("\"He said \"\"hello\"\"\"", CsvCell.Escape("He said \"hello\""));
        Assert.Equal("\"line one\nline two\"", CsvCell.Escape("line one\nline two"));
    }

    [Fact]
    public void ADangerousValueThatAlsoNeedsQuotingGetsBoth()
    {
        var escaped = CsvCell.Escape("=SUM(1,2)");

        // A comma inside means it must be quoted as well as neutralised - and the apostrophe belongs INSIDE the
        // quotes, or Excel sees the formula again.
        Assert.StartsWith("\"'", escaped);
        Assert.EndsWith("\"", escaped);
    }

    [Fact]
    public void AnAbsentValueIsAnEmptyField()
    {
        Assert.Equal(string.Empty, CsvCell.Escape(null));
        Assert.Equal(string.Empty, CsvCell.Escape(""));
    }

    [Fact]
    public void ARowJoinsItsEscapedFields()
    {
        Assert.Equal("Juan Dela Cruz,1,900.00", CsvCell.Row("Juan Dela Cruz", "1", "900.00"));
        Assert.Equal("\"Cruz, Juan\",'=1+1", CsvCell.Row("Cruz, Juan", "=1+1"));
    }
}
