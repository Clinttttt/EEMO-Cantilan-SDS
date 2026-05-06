namespace EEMOCantilanSDS.Application.Requests.TaboanMarket;

public record MarkVendorPaidRequest(bool IsPaid, Guid CollectorId);
public record SaveOrNumberRequest(string ORNumber);
