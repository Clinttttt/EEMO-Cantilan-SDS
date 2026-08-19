namespace EEMOCantilanSDS.Application.Requests.Mobile;

/// <summary>
/// A market collection as the collector's app reports it.
/// </summary>
/// <param name="CollectionDate">
/// The market day being settled. Optional: omitted means TODAY, which is what every build of the app sent before this
/// existed, so an older app keeps working unchanged.
///
/// <para>
/// It exists because a day that went uncollected stays owed. A payor who did not pay yesterday is still unpaid for
/// yesterday, and the collector who catches them today has to be able to say which day the money answers for - otherwise
/// the office is left with a payment recorded against the wrong date and yesterday still open.
/// </para>
/// </summary>
public sealed record RecordMobileNpmCollectionRequest(
    Guid StallId,
    bool IsPaid,
    string? ORNumber = null,
    decimal? FishKilos = null,
    // Excused/absent day: the payor was not operating. ₱0 owed, mutually exclusive with IsPaid.
    bool IsAbsent = false,
    DateOnly? CollectionDate = null);
