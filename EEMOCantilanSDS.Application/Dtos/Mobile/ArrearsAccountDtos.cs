namespace EEMOCantilanSDS.Application.Dtos.Mobile;

public enum ArrearsAccountKind { StallRent = 1, SpaceRental = 2 }

/// <summary>
/// One liable account in the Collector Mobile Arrears and Delinquency workspace (never one row per month). <see cref="Identity"/> is the source the
/// canonical collection session opens: the ORIGINAL contract for stall rent, the obligation account for space rental. Amounts are the Head's own readers'
/// figures; nothing is assessed or recomputed here. <see cref="Status"/> is Active, Lapsed, Closed, Renewed or Re-let; <see cref="Reason"/> is
/// arrears, delinquent or both.
/// </summary>
public sealed record ArrearsAccountDto(
    string AccountKey, ArrearsAccountKind Kind, CollectionSourceIdentity Identity, string OperationCode, string OperationName,
    string Payer, string Space, string? Section, string Status, string Reason,
    decimal ArrearsAmount, decimal DelinquentAmount, decimal Total, int UnpaidMonths,
    string? FirstPeriod, string? LastPeriod,
    // Space rental: what an earlier term of a renewed space left owing, apart from the current term. Always zero for stall rent (a former term is its own contract row).
    decimal PriorTermAmount, decimal CurrentTermAmount,
    string? ArrearsBasis,
    // Whether a canonical collection can be opened for this account at all (monthly-rent facility or space rental); the session still states which periods are collectible now.
    bool CanCollect,
    // Original liable identity, for the activation / verified link flow.
    Guid? ContractId, Guid? ObligationAccountId);

public sealed record ArrearsWorkspaceDto(DateOnly AsOf, IReadOnlyList<ArrearsAccountDto> Accounts, decimal Total, decimal ArrearsTotal, decimal DelinquentTotal);
