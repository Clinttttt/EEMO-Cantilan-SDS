using EEMOCantilanSDS.Application.Dtos.Slaughterhouse;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>
/// One day's slaughterhouse collection for a collector: the day's per-head transactions plus
/// running totals. Slaughter is per-transaction (no payor roster), so this is day-scoped rather
/// than a stall list. Rates are surfaced so the mobile sheet can preview the fee without hardcoding.
/// </summary>
public sealed record MobileSlaughterCollectionDto(
    DateOnly Date,
    int TransactionCount,
    int TotalHeads,
    decimal TotalCollected,
    decimal HogRatePerHead,
    decimal LargeAnimalRatePerHead,
    IReadOnlyList<SlaughterTransactionDto> Transactions);
