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
    // Tenant-resolved per-head rates (250 / 365 ordinance fallback keeps Cantilan identical), so the UI
    // shows this LGU's own rates. Trailing defaults keep the existing positional repository construction valid.
    decimal HogRatePerHead = 250m,
    decimal LargeRatePerHead = 365m
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
