namespace EEMOCantilanSDS.Application.Requests.Mobile;

public sealed record RecordMobileNpmCollectionRequest(
    Guid StallId,
    bool IsPaid,
    string? ORNumber = null,
    decimal? FishKilos = null,
    // Excused/absent day: the payor was not operating. ₱0 owed, mutually exclusive with IsPaid.
    bool IsAbsent = false);
