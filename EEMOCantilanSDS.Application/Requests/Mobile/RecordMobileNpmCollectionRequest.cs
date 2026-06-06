namespace EEMOCantilanSDS.Application.Requests.Mobile;

public sealed record RecordMobileNpmCollectionRequest(
    Guid StallId,
    bool IsPaid,
    string? ORNumber = null,
    decimal? FishKilos = null);
