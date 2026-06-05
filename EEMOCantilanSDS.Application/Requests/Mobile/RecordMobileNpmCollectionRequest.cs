namespace EEMOCantilanSDS.Application.Requests.Mobile;

public sealed record RecordMobileNpmCollectionRequest(
    Guid StallId,
    bool IsPaid,
    decimal? FishKilos = null);
