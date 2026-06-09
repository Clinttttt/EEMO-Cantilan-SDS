namespace EEMOCantilanSDS.Application.Dtos.Payments;

/// <summary>
/// Rolling 12-month ledger totals for one stall, computed daily-aware (NPM folds daily
/// collections + contract-aware ₱30/day obligation per month; other facilities use monthly rent).
/// Powers the stall profile's payment-history summary so its figures match the reports.
/// </summary>
public sealed record StallLedgerSummaryDto(
    int MonthsPaid,
    int MonthsUnpaid,
    decimal TotalCollected,
    decimal TotalOutstanding
);
