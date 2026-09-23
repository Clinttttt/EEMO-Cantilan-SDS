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
    OnlinePaymentTransaction = 7
}
