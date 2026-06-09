namespace EEMOCantilanSDS.Application.Requests.Mobile;

public sealed record AddMobileTpmVendorRequest(
    string VendorName,
    string Goods);

public sealed record MarkMobileTpmVendorPaidRequest(
    Guid AttendanceId,
    bool IsPaid,
    string? ORNumber = null);
