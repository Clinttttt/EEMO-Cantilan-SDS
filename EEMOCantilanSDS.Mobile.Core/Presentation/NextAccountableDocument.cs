using EEMOCantilanSDS.Application.Dtos.Revenue;

namespace EEMOCantilanSDS.Mobile.Presentation;

/// <summary>
/// Which assigned accountable form (OR or CT) the collector issues next. Forms are used in serial order, so the next form is
/// the lowest-serial one assigned to the collector that this device has not already issued (an issue waiting in the offline
/// queue keeps its form, so it is never offered again, including after a restart). Choosing another assigned form is a
/// deliberate secondary action; a number can never be typed.
/// </summary>
public static class NextAccountableDocument
{
    /// <summary>The forms still usable on this device, in the order they should be issued.</summary>
    public static IReadOnlyList<CashTicketDocumentDto> Usable(
        IEnumerable<CashTicketDocumentDto> assigned, IReadOnlySet<Guid> issuedOnThisDevice) =>
        assigned.Where(d => !issuedOnThisDevice.Contains(d.DocumentId))
            .OrderBy(d => d.SerialNumber)
            .ThenBy(d => d.DocumentNumber, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The form to issue: the collector's explicit choice while it is still usable, otherwise the next in serial order.
    /// Null when no assigned form remains.
    /// </summary>
    public static CashTicketDocumentDto? Resolve(IReadOnlyList<CashTicketDocumentDto> usable, Guid? chosen) =>
        chosen is { } id && usable.FirstOrDefault(d => d.DocumentId == id) is { } kept ? kept : usable.FirstOrDefault();

    /// <summary>"CT000101 – CT000150" for the usable range, or the single number.</summary>
    public static string RangeLabel(IReadOnlyList<CashTicketDocumentDto> usable) => usable.Count switch
    {
        0 => string.Empty,
        1 => usable[0].DocumentNumber,
        _ => $"{usable[0].DocumentNumber} – {usable[^1].DocumentNumber}",
    };
}
