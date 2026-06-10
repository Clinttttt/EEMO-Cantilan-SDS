namespace EEMOCantilanSDS.Application.Requests.TaboanMarket;

public record MarkVendorPaidRequest(bool IsPaid);
public record SaveOrNumberRequest(string ORNumber);
public record UpdateTpmVendorRequest(string VendorName, string Goods, bool IsPaid, string? ORNumber = null);
