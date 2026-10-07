using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace EEMOCantilanSDS.Client.Services;

/// <summary>
/// Reads a spreadsheet export into rows of cells, with no external dependency: CSV text, or the first worksheet of an .xlsx.
/// Shared by the bulk imports so every list is read the same way. It only reads: what the cells mean is each import's own business.
/// </summary>
public static class SpreadsheetReader
{
    public static List<string[]> ReadCsv(string text)
    {
        var rows = new List<string[]>();
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            rows.Add(SplitCsvLine(line));
        }
        return rows;
    }

    // Splits a CSV line honoring double-quoted fields (which may contain commas and "" escapes).
    private static string[] SplitCsvLine(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes) { cells.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(ch);
        }
        cells.Add(sb.ToString().Trim());
        return cells.ToArray();
    }

    // Minimal .xlsx reader (no external dependency): reads the first worksheet's rows and resolves
    // shared strings. Cells are placed at their real column index from the A1-style cell reference.
    public static List<string[]> ReadXlsx(Stream stream)
    {
        var rows = new List<string[]>();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var shared = new List<string>();
        var sst = zip.GetEntry("xl/sharedStrings.xml");
        if (sst is not null)
        {
            using var s = sst.Open();
            var doc = XDocument.Load(s);
            if (doc.Root is not null)
                foreach (var si in doc.Root.Elements(ns + "si"))
                    shared.Add(SiText(si, ns));
        }

        var sheetEntry = zip.Entries
            .Where(en => en.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase)
                      && en.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(en => en.FullName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (sheetEntry is null) return rows;

        using var ss = sheetEntry.Open();
        var sheet = XDocument.Load(ss);
        var sheetData = sheet.Root?.Element(ns + "sheetData");
        if (sheetData is null) return rows;

        foreach (var row in sheetData.Elements(ns + "row"))
        {
            var map = new Dictionary<int, string>();
            var maxIdx = -1;
            foreach (var c in row.Elements(ns + "c"))
            {
                var colIdx = ColIndex((string?)c.Attribute("r") ?? string.Empty);
                var type = (string?)c.Attribute("t");
                string val;
                if (type == "s")
                {
                    val = int.TryParse(c.Element(ns + "v")?.Value, out var idx) && idx >= 0 && idx < shared.Count
                        ? shared[idx] : string.Empty;
                }
                else if (type == "inlineStr")
                {
                    val = c.Element(ns + "is") is XElement isEl ? SiText(isEl, ns) : string.Empty;
                }
                else
                {
                    val = c.Element(ns + "v")?.Value ?? string.Empty;
                }
                map[colIdx] = val;
                if (colIdx > maxIdx) maxIdx = colIdx;
            }

            if (maxIdx < 0) { rows.Add(Array.Empty<string>()); continue; }
            var arr = new string[maxIdx + 1];
            for (var i = 0; i <= maxIdx; i++) arr[i] = map.TryGetValue(i, out var v) ? v : string.Empty;
            rows.Add(arr);
        }
        return rows;
    }

    private static string SiText(XElement si, XNamespace ns)
    {
        var t = si.Element(ns + "t");
        if (t is not null) return t.Value;
        // Rich text: concatenate the <r><t> runs.
        return string.Concat(si.Elements(ns + "r").Select(r => r.Element(ns + "t")?.Value ?? string.Empty));
    }

    private static int ColIndex(string cellRef)
    {
        var idx = 0;
        foreach (var ch in cellRef)
        {
            if (char.IsLetter(ch)) idx = idx * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            else break;
        }
        return Math.Max(0, idx - 1);
    }
}
