namespace EEMOCantilanSDS.Application.Dtos.Payments;

/// <summary>
/// One row in a stall's transparency collection-history log (newest first). For NPM each row is a
/// recorded daily collection (paid or absent); for monthly facilities each row is a payment record.
/// </summary>
public sealed record StallCollectionHistoryRowDto(
    DateTime Date,
    string PayorName,
    string Status,
    decimal Amount,
    string? ORNumber,
    string? CollectorName
);
