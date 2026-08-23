using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Slaughterhouse;

/// <summary>A per-LGU custom slaughterhouse animal type and its default per-head rate.</summary>
public record SlaughterAnimalRateDto(
    Guid Id,
    string AnimalName,
    decimal RatePerHead,
    bool IsActive
);

public record SlaughterTransactionDto(
    Guid Id,
    string OwnerName,
    AnimalType AnimalType,
    string? CustomAnimalType,
    int NumberOfHeads,
    decimal RatePerHead,
    decimal TotalAmount,
    string? ORNumber,
    DateOnly TransactionDate
);

public record SlaughterOverviewDto(
    int TotalTransactions,
    int TotalHeads,
    decimal TotalCollected,
    int HogCount,
    int CarabaoCount,
    int CowCount,
    int OthersCount,
    // The office's OWN per-head rates, or null where its ordinance states none. Null is the whole point: these used to
    // default to Cantilan's 250 and 365, and the overview resolved them with Resolve(), which reads an unstated rate as
    // zero - so an office that does not slaughter carabao was offered a carabao at ₱0 per head, and one that had not
    // been configured at all was quoted Cantilan's ordinance. An animal an office does not price is not offered.
    decimal? HogRatePerHead = null,
    decimal? LargeRatePerHead = null
);

public record OwnerTransactionGroupDto(
    string OwnerName,
    DateOnly LatestTransactionDate,
    string? ORNumber,
    int TotalTransactionDates,  // Now represents distinct OR number count
    IReadOnlyList<SlaughterTransactionDto> LatestTransactions
);

public record OwnerTransactionHistoryDto(
    string OwnerName,
    IReadOnlyList<TransactionDateGroupDto> TransactionGroups
);

// ── Slaughterhouse collection history (server-aggregated, mirrors FacilityHistory) ──
public record SlaughterHistoryDto(
    int Year,
    IReadOnlyList<SlaughterPeriodSummaryDto> Monthly,   // each month of Year (up to current month for the current year)
    IReadOnlyList<SlaughterPeriodSummaryDto> Yearly     // rolling last 5 years
);

public record SlaughterPeriodSummaryDto(
    string Label,            // "January" for monthly rows, "2024" for yearly rows
    int Year,
    int? Month,              // null for yearly rows
    int Transactions,
    int Receipts,            // distinct OR receipts (one receipt may cover several animal line-items)
    int OwnersServed,        // distinct owners in the period
    int TotalHeads,
    decimal TotalCollected,
    int HogHeads,
    int CarabaoHeads,
    int CowHeads,
    int OtherHeads,
    decimal HogRevenue,
    decimal CarabaoRevenue,
    decimal CowRevenue,
    decimal OtherRevenue,
    IReadOnlyList<CustomAnimalTallyDto> OtherAnimals   // specific custom animal types within "Other"
);

public record CustomAnimalTallyDto(
    string Name,
    int Heads,
    decimal Revenue
);

public record TransactionDateGroupDto(
    DateOnly TransactionDate,
    string? ORNumber,
    IReadOnlyList<SlaughterTransactionDto> Transactions
);
