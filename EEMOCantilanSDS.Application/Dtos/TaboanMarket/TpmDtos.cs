namespace EEMOCantilanSDS.Application.Dtos.TaboanMarket;

public record TpmOverviewDto
{
    public decimal CollectedThisMonth { get; init; }
    public int FridaysThisMonth { get; init; }
    public int VendorEntriesThisMonth { get; init; }
    public int CollectionRate { get; init; }
}

public record TpmMarketDayDto
{
    public DateOnly MarketDate { get; init; }
    public int VendorsPaid { get; init; }
    public decimal TotalCollected { get; init; }
}

public record TpmVendorAttendanceDto
{
    public Guid Id { get; init; }
    public Guid VendorId { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string Goods { get; init; } = string.Empty;
    public bool IsPaid { get; init; }
    public string? ORNumber { get; init; }
    public decimal Fee { get; init; }
    public DateOnly MarketDate { get; init; }
}

public record TpmVendorDto
{
    public Guid Id { get; init; }
    public string VendorName { get; init; } = string.Empty;
    public string Goods { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string? ContactNumber { get; init; }
}
