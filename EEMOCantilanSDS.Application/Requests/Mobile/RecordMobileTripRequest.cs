namespace EEMOCantilanSDS.Application.Requests.Mobile;

public sealed record RecordMobileTripRequest(
    Guid TransporterId,
    string DriverName,
    string PlateNumber,
    string Route,
    string ORNumber,
    string? Remarks = null);
