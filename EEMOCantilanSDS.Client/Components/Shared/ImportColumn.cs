namespace EEMOCantilanSDS.Client.Components.Shared;

/// <summary>One column of a bulk-import register: its key, header, kind of cell (text, number, date, month or select), width and placeholder.</summary>
public record ImportColumn(string Key, string Label, string Type = "text", string[]? Options = null,
    int MinWidth = 150, string Placeholder = "", string Default = "")
{
    public string[] Options { get; init; } = Options ?? Array.Empty<string>();
}

/// <summary>A cell the office edited: which row, which column, and the new text.</summary>
public sealed record ImportCellChange<TRow>(TRow Row, ImportColumn Column, string? Value);

/// <summary>A row an import could not save: its number on the sheet, who it was, and why.</summary>
public sealed record ImportResultError(int RowNumber, string Label, string Message);
