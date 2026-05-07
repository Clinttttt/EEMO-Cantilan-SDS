namespace EEMOCantilanSDS.Application.Dtos.Slaughterhouse;

public record ClientProfileDto(
    string OwnerName,
    int TotalTransactions,
    int TotalHeads,
    decimal TotalCollected,
    IReadOnlyList<ClientTransactionDto> Transactions,
    ClientCollectionSummaryDto CollectionSummary
);

public record ClientTransactionDto(
    DateOnly TransactionDate,
    string AnimalTypeDisplay,
    int NumberOfHeads,
    decimal RatePerHead,
    decimal TotalAmount,
    string? ORNumber,
    string? CollectorName
);

public record ClientCollectionSummaryDto(
    int TotalTransactions,
    int TotalHeadsProcessed,
    decimal AveragePerTransaction,
    decimal TotalRevenue
);
