using System.Globalization;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Reading an amount the office typed into a spreadsheet cell.
///
/// <para>
/// The import stripped every comma and then matched digits, so "1.500,00" became "1.50000" and was read as 1.50. A
/// space let at fifteen hundred pesos a month imported as costing one peso fifty — and nothing downstream could catch
/// it, because 1.50 is a perfectly valid rate. The office's lists are written by different hands and use both
/// conventions.
/// </para>
/// </summary>
public class SheetNumberTests
{
    [Theory]
    // The defect, first: comma as the decimal separator.
    [InlineData("1.500,00", 1500.00)]
    [InlineData("2.760,50", 2760.50)]
    [InlineData("10.800,00", 10800.00)]
    // Point as the decimal separator, comma grouping.
    [InlineData("1,500.00", 1500.00)]
    [InlineData("28,800.00", 28800.00)]
    [InlineData("119,520.00", 119520.00)]
    // A single separator with three trailing digits is grouping, whichever it is.
    [InlineData("1,500", 1500)]
    [InlineData("1.500", 1500)]
    [InlineData("28,800", 28800)]
    // A single separator that is plainly a decimal.
    [InlineData("1500.5", 1500.5)]
    [InlineData("1500,5", 1500.5)]
    [InlineData("0.50", 0.50)]
    [InlineData("2760.75", 2760.75)]
    // Plain, and dressed up.
    [InlineData("1500", 1500)]
    [InlineData("900", 900)]
    [InlineData("₱1,500.00", 1500.00)]
    [InlineData("  2,400  ", 2400)]
    [InlineData("PHP 3,240.00", 3240.00)]
    public void AnAmountIsReadAsTheOfficeWroteIt(string cell, double expected)
    {
        Assert.Equal((decimal)expected, SheetNumber.ToDecimal(cell));
        // And always emitted with '.' as the decimal point, so the caller can parse it invariantly.
        Assert.Equal(
            ((decimal)expected).ToString("0.####", CultureInfo.InvariantCulture),
            decimal.Parse(SheetNumber.Normalize(cell), CultureInfo.InvariantCulture).ToString("0.####", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TheThousandfoldUnderBilling_IsGoneForEveryGroupingWrittenWithAPoint()
    {
        // Stated on its own because this is the shape that lost the money: a monthly rent read as loose change.
        foreach (var (cell, expected) in new[]
                 {
                     ("1.500,00", 1500.00m), ("2.400,00", 2400.00m), ("2.760,00", 2760.00m),
                     ("3.840,00", 3840.00m), ("33.120,00", 33120.00m),
                 })
        {
            var actual = SheetNumber.ToDecimal(cell);
            Assert.Equal(expected, actual);
            Assert.True(actual > 1_000m, $"'{cell}' was read as {actual}, which is loose change rather than a rent.");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("n/a")]
    [InlineData("—")]
    [InlineData("no contract")]
    public void ACellWithNoNumberYieldsNothing_RatherThanZeroOrAnException(string? cell)
    {
        Assert.Equal(string.Empty, SheetNumber.Normalize(cell));
        Assert.Null(SheetNumber.ToDecimal(cell));
    }

    [Fact]
    public void ANegativeIsKept()
    {
        Assert.Equal(-1500m, SheetNumber.ToDecimal("-1,500.00"));
        Assert.Equal(-1500m, SheetNumber.ToDecimal("-1.500,00"));
    }

    [Fact]
    public void TheReadingDoesNotDependOnTheServersCulture()
    {
        // Blazon Server parses on the server, so the container's culture must not decide what the office's sheet says.
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "en-US", "de-DE", "fil-PH", "fr-FR" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                Assert.Equal(1500.00m, SheetNumber.ToDecimal("1,500.00"));
                Assert.Equal(1500.00m, SheetNumber.ToDecimal("1.500,00"));
                Assert.Equal(2760.50m, SheetNumber.ToDecimal("2,760.50"));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
