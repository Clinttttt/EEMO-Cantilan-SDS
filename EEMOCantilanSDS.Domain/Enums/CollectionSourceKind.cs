namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>Stable internal source identity for a future ledger line adapter.</summary>
public enum CollectionSourceKind
{
    PaymentRecord = 1,
    DailyCollection = 2,
    UtilityBill = 3,
    SlaughterTransaction = 4,
    TpmAttendance = 5,
    TrmTrip = 6,
    OnlinePaymentTransaction = 7,
    /// <summary>A governed configurable service (Market Fees, Landing/Berthing, ...). SourceId is the tenant service identity.</summary>
    GovernedService = 8,
    /// <summary>An approved penalty definition version (IA-049). SourceId is the exact version applied; it carries no receivable.</summary>
    PenaltyDefinition = 9,
    /// <summary>One assessed period of a specialized obligation account (vendor fee, Kanmanggay, event lot). SourceId is the period.</summary>
    ObligationPeriod = 10
}
