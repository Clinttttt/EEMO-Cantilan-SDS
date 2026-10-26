namespace EEMOCantilanSDS.Mobile.Collections;

/// <summary>
/// The optional receipt reference a collector may type once for a checkout. It is offered when the checkout holds an Official Receipt charge of any operation
/// (or months of one space account); Cash Ticket and other instruments never offer it. It is collector-entered information, not proof of an issued or registered
/// receipt, and never a prerequisite for collecting.
/// </summary>
public static class ReceiptReferenceEntry
{
    public const int MaxLength = 100;

    /// <summary>The trimmed reference to send, or null when the field is not offered, was left blank, or is too long to be sent.</summary>
    public static string? Number(bool offered, string? typed)
    {
        var text = typed?.Trim();
        return offered && !string.IsNullOrEmpty(text) && text.Length <= MaxLength ? text : null;
    }

    /// <summary>The same reference shown back on a saved checkout, whatever way it was stored.</summary>
    public static string Restore(string? reference, string? legacyOfficialReceiptNumber) => reference ?? legacyOfficialReceiptNumber ?? "";
}
