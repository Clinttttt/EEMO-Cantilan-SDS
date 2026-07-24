using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Application.Dtos.TaboanMarket;

public record TpmOverviewDto
{
    public decimal CollectedThisMonth { get; init; }
    public int FridaysThisMonth { get; init; }
    public int VendorEntriesThisMonth { get; init; }
    public int CollectionRate { get; init; }

    // The LGU's configured weekly market weekday (Cantilan default = Friday). Lets the UI render the
    // correct calendar/labels per tenant (e.g. Madrid = Saturday) instead of hardcoding Friday.
    public DayOfWeek MarketDay { get; init; } = DayOfWeek.Friday;

    // The tenant's resolved per-vendor market-day fee (₱100 ordinance fallback keeps Cantilan identical),
    // so the UI shows this LGU's own fee instead of a hardcoded ₱100.
    public decimal VendorFee { get; init; } = FeeRates.TpmVendorFee;
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

// ── Collection history (server-aggregated, mirrors FacilityHistory / TrmHistory) ──
public record TpmHistoryDto(
    int Year,
    IReadOnlyList<TpmPeriodSummaryDto> Monthly,   // each month of Year (up to current month for the current year)
    IReadOnlyList<TpmPeriodSummaryDto> Yearly     // rolling last 5 years
);

public record TpmPeriodSummaryDto(
    string Label,            // "January" for monthly rows, "2024" for yearly rows
    int Year,
    int? Month,              // null for yearly rows
    int MarketDays,          // distinct Fridays with at least one attendance
    int VendorEntries,       // total attendance entries
    int PaidEntries,
    int UnpaidEntries,
    decimal Collected,       // fees collected (paid entries only)
    int CollectionRate,      // paid / total, as a percentage
    IReadOnlyList<TpmGoodsTallyDto> Goods   // entry/fee tally per goods category
);

public record TpmGoodsTallyDto(
    string Goods,
    int Entries,
    decimal Collected
);
