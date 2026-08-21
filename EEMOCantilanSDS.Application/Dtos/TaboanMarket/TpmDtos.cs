using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Application.Dtos.TaboanMarket;

public record TpmOverviewDto
{
    public decimal CollectedThisMonth { get; init; }
    public int FridaysThisMonth { get; init; }
    public int VendorEntriesThisMonth { get; init; }
    public int CollectionRate { get; init; }

    // The LGU's configured weekly market weekday, as it stands at the END of the month being viewed. Lets the UI
    // label the arrangement the office is operating under. Do NOT expand this into a month's dates: in a month the
    // office moved its market day, the earlier weeks fall on the OLD day, and multiplying one weekday out would
    // re-label weeks that have already been collected. Use MarketDates for that.
    public DayOfWeek MarketDay { get; init; } = DayOfWeek.Friday;

    // The month's actual market dates, taken from the office's own schedule, in date order. A month the day was
    // moved in carries BOTH weekdays: the old day up to the move, the new day from it. This is the only correct
    // source for a calendar or a per-date trend.
    public IReadOnlyList<DateOnly> MarketDates { get; init; } = [];

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
