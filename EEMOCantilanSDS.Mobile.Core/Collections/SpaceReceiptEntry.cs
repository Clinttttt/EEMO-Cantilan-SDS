using EEMOCantilanSDS.Application.Dtos.Mobile;

namespace EEMOCantilanSDS.Mobile.Collections;

/// <summary>
/// One optional reference can accompany any month(s) of the SAME space account.
/// It is collector-entered information, not proof of accountable form issuance or custody.
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
