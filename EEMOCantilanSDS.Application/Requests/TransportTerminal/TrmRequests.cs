namespace EEMOCantilanSDS.Application.Requests.TransportTerminal;

public record RecordTripRequest(
    string DriverName,
    string PlateNumber,
    string Route,
    string ORNumber,
    string? Remarks);

public record SaveTripOrNumberRequest(string ORNumber);
