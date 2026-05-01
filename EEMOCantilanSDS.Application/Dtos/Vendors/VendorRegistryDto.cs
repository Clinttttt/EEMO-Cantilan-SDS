namespace EEMOCantilanSDS.Application.Dtos.Vendors;

public sealed record VendorRegistryDto(
    int TotalVendors,
    int ActiveVendors,
    int ClosedVendors,
    int PaidThisMonth,
    int UnpaidCount,
    decimal TotalOutstanding,
    decimal MonthlyTarget,
    IReadOnlyList<VendorListItemDto> Vendors
);
