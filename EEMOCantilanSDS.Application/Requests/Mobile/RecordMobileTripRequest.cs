namespace EEMOCantilanSDS.Application.Requests.Mobile;

public sealed record RecordMobileTripRequest(
    Guid? TransporterId,
    string DriverName,
    string PlateNumber,
    string Route,
    string ORNumber,
    string? Organization = null,
    string? Remarks = null);
