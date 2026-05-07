using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Slaughterhouse;

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
    int OthersCount
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

public record TransactionDateGroupDto(
    DateOnly TransactionDate,
    string? ORNumber,
    IReadOnlyList<SlaughterTransactionDto> Transactions
);
