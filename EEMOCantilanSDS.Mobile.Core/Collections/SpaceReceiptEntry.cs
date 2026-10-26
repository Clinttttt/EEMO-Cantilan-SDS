using EEMOCantilanSDS.Application.Dtos.Mobile;

namespace EEMOCantilanSDS.Mobile.Collections;

/// <summary>
/// When one physical Official Receipt may be entered for a checkout: only when every item is a month of the SAME space account.
/// The server re-checks this and the receipt itself (registered, unused, in the collector's custody); this only decides whether to offer the field.
/// </summary>
public static class SpaceReceiptEntry
{
    public static bool Applies(IReadOnlyList<CollectionSessionItemIntent> items) =>
        items.Count > 0 && items.All(i => i.Kind == CollectionSessionItemKind.Obligation && i.Obligation is not null)
        && items.Select(i => i.Obligation!.AccountId).Distinct().Count() == 1;

    /// <summary>The trimmed number to send, or null when none applies or was typed (the SRC-only path).</summary>
    public static string? Number(IReadOnlyList<CollectionSessionItemIntent> items, string? typed) =>
        Applies(items) && !string.IsNullOrWhiteSpace(typed) ? typed.Trim() : null;
}
