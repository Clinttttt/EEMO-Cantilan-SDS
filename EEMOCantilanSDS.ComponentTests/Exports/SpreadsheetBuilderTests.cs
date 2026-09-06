using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EEMOCantilanSDS.Client.Exports;

namespace EEMOCantilanSDS.ComponentTests.Exports;

/// <summary>
/// The spreadsheet export, read back out of the bytes it produces.
/// </summary>
/// <remarks>
/// Written because the fault being fixed is invisible in the data: the CSV's figures were always right and Excel still showed
/// <c>########</c>, because a CSV cannot say how wide a column is. So the assertions here are mostly about the things that are
/// not values - the widths, the types and the formats - since those are what the office actually complained about.
///
/// <para>Read back through the same library that wrote them. That is deliberate: it proves the package is well-formed rather
/// than merely that the code ran, and a file Excel refuses to open is a worse outcome than the hashes.</para>
/// </remarks>
public class SpreadsheetBuilderTests
{
    private static readonly SheetColumn[] Columns =
    [
        new("Name of Lessee", SheetValueKind.Text),
        new("Effectivity", SheetValueKind.Date),
        new("No. of Years", SheetValueKind.Number),
        new("Whole Year Rental", SheetValueKind.Money),
    ];

    private static readonly IReadOnlyList<object?>[] Rows =
    [
        new object?[] { "Kim Chui", new DateOnly(2026, 8, 11), 3, 10_800m },
        new object?[] { "Maria L. Sabandal", new DateOnly(2026, 9, 1), 3, 10_800m },
        new object?[] { "A Lessee With A Notably Longer Name", null, 3, 190_800m },
    ];

    /// <summary>Every column states its own width — the fault this file exists to fix.</summary>
    [Fact]
    public void EveryColumnCarriesAWidthWideEnoughForWhatItShows()
    {
        var bytes = SpreadsheetBuilder.Build("Stallholders", Columns, Rows);
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheet = document.WorkbookPart!.Workbook.Descendants<Sheet>().First();
        var worksheet = ((WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!)).Worksheet;

        var widths = worksheet.Descendants<Column>().ToList();
        Assert.Equal(Columns.Length, widths.Count);
        Assert.All(widths, c =>
        {
            Assert.True(c.CustomWidth?.Value == true, "a width Excel is free to ignore is no width at all");
            Assert.True(c.Width?.Value > 0);
        });

        // The date column is the one that showed hashes: it must hold ten characters and the header, whichever is longer.
        var dateColumn = widths[1];
        Assert.True(dateColumn.Width!.Value >= 11d, $"date column was {dateColumn.Width.Value}, too narrow for 8/11/2026");

        // Money is measured as the office SEES it — "190,800.00" is wider than the bare number.
        var moneyColumn = widths[3];
        Assert.True(moneyColumn.Width!.Value >= 12d, $"money column was {moneyColumn.Width.Value}");
    }

    /// <summary>
    /// A date is stored as a date, not as text that looks like one.
    /// </summary>
    /// <remarks>
    /// Text sorts alphabetically, which files 1 September before 2 August. The office sorts these registers.
    /// </remarks>
    [Fact]
    public void ADateIsARealDateAndNotText()
    {
        var bytes = SpreadsheetBuilder.Build("Stallholders", Columns, Rows);
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheet = document.WorkbookPart!.Workbook.Descendants<Sheet>().First();
        var worksheet = ((WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!)).Worksheet;

        var body = worksheet.Descendants<Row>().Skip(1).First();
        var dateCell = body.Elements<Cell>().ElementAt(1);

        Assert.NotEqual(CellValues.InlineString, dateCell.DataType?.Value);
        Assert.Equal(new DateTime(2026, 8, 11).ToOADate(), double.Parse(dateCell.CellValue!.Text));

        // Built-in number format 14 is Excel's own short date, so it follows the reader's locale rather than ours.
        var style = document.WorkbookPart.WorkbookStylesPart!.Stylesheet.CellFormats!
            .Elements<CellFormat>()
            .ElementAt((int)dateCell.StyleIndex!.Value);
        Assert.Equal(14u, style.NumberFormatId?.Value);
    }

    /// <summary>The header is on the first row, bold, and stays in view while a long register scrolls.</summary>
    [Fact]
    public void TheHeaderIsBoldAndFrozen()
    {
        var bytes = SpreadsheetBuilder.Build("Stallholders", Columns, Rows);
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheet = document.WorkbookPart!.Workbook.Descendants<Sheet>().First();
        var worksheet = ((WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!)).Worksheet;

        var header = worksheet.Descendants<Row>().First();
        Assert.Equal(
            Columns.Select(c => c.Header),
            header.Elements<Cell>().Select(c => c.InlineString!.Text!.Text));

        var headerStyle = document.WorkbookPart.WorkbookStylesPart!.Stylesheet.CellFormats!
            .Elements<CellFormat>()
            .ElementAt((int)header.Elements<Cell>().First().StyleIndex!.Value);
        var font = document.WorkbookPart.WorkbookStylesPart.Stylesheet.Fonts!
            .Elements<Font>()
            .ElementAt((int)headerStyle.FontId!.Value);
        Assert.NotNull(font.Bold);

        var pane = worksheet.Descendants<Pane>().Single();
        Assert.Equal("A2", pane.TopLeftCell!.Value);
        Assert.Equal(PaneStateValues.Frozen, pane.State!.Value);
    }

    /// <summary>An empty cell stays empty rather than printing a nought the office would read as a figure.</summary>
    [Fact]
    public void AMissingValueIsLeftBlank()
    {
        var bytes = SpreadsheetBuilder.Build("Stallholders", Columns, Rows);
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheet = document.WorkbookPart!.Workbook.Descendants<Sheet>().First();
        var worksheet = ((WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!)).Worksheet;

        // Third body row has no effectivity — a space-only arrangement, which the printed form leaves blank too.
        var row = worksheet.Descendants<Row>().Skip(3).First();
        var blank = row.Elements<Cell>().ElementAt(1);

        Assert.Null(blank.CellValue);
        Assert.Null(blank.InlineString);
    }

    /// <summary>
    /// A tab name Excel will not accept is corrected rather than allowed to spoil the whole file.
    /// </summary>
    /// <remarks>
    /// Excel rejects the FILE, not just the name, so a facility named with a slash would otherwise produce a download that
    /// simply will not open — and the office would have no way to tell why.
    /// </remarks>
    [Theory]
    [InlineData("New Public Market / NPM", "New Public Market  NPM")]
    [InlineData("", "Sheet1")]
    public void ASheetNameExcelWouldRefuseIsCorrected(string given, string expected)
    {
        var bytes = SpreadsheetBuilder.Build(given, Columns, Rows);
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);

        Assert.Equal(expected, document.WorkbookPart!.Workbook.Descendants<Sheet>().First().Name!.Value);
    }

    /// <summary>A name past Excel's 31-character limit is shortened, again because it costs the whole file.</summary>
    [Fact]
    public void ASheetNameIsKeptWithinExcelsLimit()
    {
        var bytes = SpreadsheetBuilder.Build(new string('x', 60), Columns, Rows);
        using var document = SpreadsheetDocument.Open(new MemoryStream(bytes), false);

        Assert.Equal(31, document.WorkbookPart!.Workbook.Descendants<Sheet>().First().Name!.Value!.Length);
    }
}
