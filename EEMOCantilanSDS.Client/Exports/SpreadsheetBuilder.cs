using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace EEMOCantilanSDS.Client.Exports;

/// <summary>What a column holds, which decides how Excel stores, formats and sorts it.</summary>
public enum SheetValueKind
{
    /// <summary>Names, numbers of spaces, anything the office reads rather than computes with.</summary>
    Text,

    /// <summary>A plain count, right-aligned and sortable as a number.</summary>
    Number,

    /// <summary>Pesos, shown with thousands separators and two decimals.</summary>
    Money,

    /// <summary>A real date, so the office can sort and filter by it rather than by the text of it.</summary>
    Date,
}

/// <summary>One column of the sheet.</summary>
public sealed record SheetColumn(string Header, SheetValueKind Kind);

/// <summary>
/// Writes a worksheet the office can read without touching it.
/// </summary>
/// <remarks>
/// <para>
/// Added 2026-09-06 because the CSV export showed <c>########</c> in the Effectivity column. That is not our data being wrong:
/// Excel prints a DATE or a NUMBER as hashes when the column is narrower than the value, and its default column fits about
/// eight characters, so <c>8/9/2026</c> appeared and <c>8/11/2026</c> did not. A CSV file cannot carry a column width, so no
/// amount of reformatting the text could fix it - the office had to widen every column by hand each time.
/// </para>
/// <para>
/// The two usual CSV tricks for forcing text - a leading <c>="…"</c> or a leading tab - are refused on purpose by
/// <see cref="EEMOCantilanSDS.Domain.Common.CsvCell"/>, which neutralises them so that an occupant name recorded as
/// <c>=HYPERLINK(…)</c> cannot run when the office double-clicks an export. Widening a column is not worth giving that up, so
/// the answer is a file format that states its own widths.
/// </para>
/// <para>
/// THE CSV IS KEPT. This sits beside it rather than replacing it: anything that already consumes the CSV keeps working, and a
/// spreadsheet is the wrong thing to hand to a program that wants data.
/// </para>
/// <para>
/// Deliberately small. It writes one sheet of headed columns and knows four kinds of value, because that is what the office's
/// registers are. It is not a reporting engine.
/// </para>
/// </remarks>
public static class SpreadsheetBuilder
{
    // Style indices into the stylesheet built below. Named rather than written as bare numbers at the call sites, because a
    // wrong index here produces a file that opens with the right figures wearing the wrong format.
    private const uint StyleDefault = 0;
    private const uint StyleHeader = 1;
    private const uint StyleDate = 2;
    private const uint StyleMoney = 3;

    /// <summary>Excel's column width is measured in characters, so a width is the longest cell plus room to breathe.</summary>
    private const double WidthPadding = 2.6;

    /// <summary>Wide enough for a long occupant name, short enough that the sheet still fits a page.</summary>
    private const double MaxWidth = 42d;

    private const double MinWidth = 6d;

    /// <summary>
    /// The workbook as bytes, ready to hand to the browser.
    /// </summary>
    /// <param name="sheetName">The tab's name. Excel refuses some characters and any name past 31 characters, so it is trimmed.</param>
    /// <param name="columns">The headed columns, in the order the printed form states them.</param>
    /// <param name="rows">One array per row, matching <paramref name="columns"/> by position. A null cell is left empty.</param>
    public static byte[] Build(
        string sheetName,
        IReadOnlyList<SheetColumn> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);

        using var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = BuildStylesheet();
            stylesPart.Stylesheet.Save();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();

            // The header, and then the body.
            sheetData.Append(HeaderRow(columns));
            foreach (var row in rows)
                sheetData.Append(BodyRow(columns, row));

            // THE WHOLE POINT OF THIS FILE: the widths travel with it, so nothing is shown as hashes.
            var widths = new DocumentFormat.OpenXml.Spreadsheet.Columns();
            for (var i = 0; i < columns.Count; i++)
            {
                widths.Append(new Column
                {
                    Min = (uint)(i + 1),
                    Max = (uint)(i + 1),
                    Width = WidthOf(columns[i], rows, i),
                    CustomWidth = true,
                });
            }

            // The header stays in view while the office scrolls a long register — the same reason the printed sheet repeats it
            // on every page.
            var freezeHeader = new SheetViews(
                new SheetView
                {
                    WorkbookViewId = 0,
                    Pane = new Pane
                    {
                        VerticalSplit = 1d,
                        TopLeftCell = "A2",
                        ActivePane = PaneValues.BottomLeft,
                        State = PaneStateValues.Frozen,
                    },
                });

            // Order matters to the schema: views, then column widths, then the cells.
            worksheetPart.Worksheet = new Worksheet(freezeHeader, widths, sheetData);
            worksheetPart.Worksheet.Save();

            workbookPart.Workbook.AppendChild(new Sheets()).Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = SafeSheetName(sheetName),
            });

            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static Row HeaderRow(IReadOnlyList<SheetColumn> columns)
    {
        var row = new Row();
        foreach (var column in columns)
            row.Append(TextCell(column.Header, StyleHeader));
        return row;
    }

    private static Row BodyRow(IReadOnlyList<SheetColumn> columns, IReadOnlyList<object?> values)
    {
        var row = new Row();

        for (var i = 0; i < columns.Count; i++)
        {
            var value = i < values.Count ? values[i] : null;

            if (value is null)
            {
                row.Append(new Cell { StyleIndex = StyleDefault });
                continue;
            }

            row.Append(columns[i].Kind switch
            {
                SheetValueKind.Date => DateCell(value),
                SheetValueKind.Money => NumberCell(value, StyleMoney),
                SheetValueKind.Number => NumberCell(value, StyleDefault),
                _ => TextCell(value.ToString() ?? string.Empty, StyleDefault),
            });
        }

        return row;
    }

    /// <summary>
    /// A text cell written INLINE rather than through the shared-strings table.
    /// </summary>
    /// <remarks>
    /// Shared strings save space where values repeat; these registers are mostly distinct names, and an inline string cannot
    /// fall out of step with an index. For a roster of a few hundred rows the saving is not worth the extra part.
    /// </remarks>
    private static Cell TextCell(string text, uint style) => new()
    {
        DataType = CellValues.InlineString,
        StyleIndex = style,
        InlineString = new InlineString(new Text(text)),
    };

    private static Cell NumberCell(object value, uint style) => new()
    {
        DataType = CellValues.Number,
        StyleIndex = style,
        CellValue = new CellValue(Convert.ToDecimal(value)),
    };

    /// <summary>
    /// A real date: stored as Excel's own day number and shown by the format, never as text.
    /// </summary>
    /// <remarks>
    /// Text that looks like a date sorts alphabetically, which puts 1 September before 2 August. The office sorts these
    /// registers, so the type has to be right and not merely look right.
    /// </remarks>
    private static Cell DateCell(object value)
    {
        var date = value switch
        {
            DateOnly d => d.ToDateTime(TimeOnly.MinValue),
            DateTime dt => dt,
            _ => DateTime.Parse(value.ToString()!),
        };

        return new Cell
        {
            DataType = CellValues.Number,
            StyleIndex = StyleDate,
            CellValue = new CellValue(date.ToOADate()),
        };
    }

    /// <summary>The width the longest value in this column needs, header included.</summary>
    private static double WidthOf(SheetColumn column, IReadOnlyList<IReadOnlyList<object?>> rows, int index)
    {
        var longest = column.Header.Length;

        foreach (var row in rows)
        {
            if (index >= row.Count || row[index] is null) continue;

            var shown = column.Kind switch
            {
                // Measured as the office will SEE it, not as it is stored. A date held as 45,000-odd shows as ten characters,
                // and money as "190,800.00" is wider than the number it came from - which is exactly how the hashes appeared.
                SheetValueKind.Date => 10,
                SheetValueKind.Money => Convert.ToDecimal(row[index]).ToString("#,##0.00").Length,
                _ => (row[index]!.ToString() ?? string.Empty).Length,
            };

            if (shown > longest) longest = shown;
        }

        return Math.Clamp(longest + WidthPadding, MinWidth, MaxWidth);
    }

    /// <summary>
    /// A tab name Excel will accept.
    /// </summary>
    /// <remarks>
    /// Excel refuses <c>: \ / ? * [ ]</c> and anything past 31 characters, and it refuses the whole FILE rather than the name -
    /// so a facility named with a slash would produce a download that will not open.
    /// </remarks>
    private static string SafeSheetName(string name)
    {
        var cleaned = new string((name ?? string.Empty)
            .Where(c => c is not (':' or '\\' or '/' or '?' or '*' or '[' or ']'))
            .ToArray())
            .Trim();

        if (cleaned.Length == 0) cleaned = "Sheet1";

        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    /// <summary>
    /// The smallest stylesheet that carries a bold header, a date format and a money format.
    /// </summary>
    /// <remarks>
    /// Excel requires the first font, fill, border and cell format to exist even unused, and requires the second fill to be
    /// gray125. Omitting either is the classic cause of "we found a problem with some content".
    /// </remarks>
    private static Stylesheet BuildStylesheet() => new(
        new Fonts(
            new Font(new FontSize { Val = 11d }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11d }, new FontName { Val = "Calibri" })),
        new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 })),
        new Borders(new Border()),
        new CellStyleFormats(new CellFormat()),
        new CellFormats(
            // 0 — default
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0 },
            // 1 — header
            new CellFormat { FontId = 1, FillId = 0, BorderId = 0, ApplyFont = true },
            // 2 — date, built-in format 14 (m/d/yyyy)
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 14, ApplyNumberFormat = true },
            // 3 — money, built-in format 4 (#,##0.00)
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 4, ApplyNumberFormat = true }));
}
