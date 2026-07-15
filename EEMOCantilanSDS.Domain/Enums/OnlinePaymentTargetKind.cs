namespace EEMOCantilanSDS.Domain.Enums
{
    /// <summary>
    /// What an <c>OnlinePaymentTransaction</c> pays for. Monthly-rental facilities (TCC/NCC/BBQ/ICE)
    /// pay a single monthly <c>PaymentRecord</c>; NPM is daily-billed, so it pays a whole month of
    /// daily ₱30 fees settled across that month's <c>DailyCollection</c> days (no monthly record).
    /// </summary>
    public enum OnlinePaymentTargetKind
    {
        MonthlyRecord = 1,   // links a PaymentRecordId (TCC/NCC/BBQ/ICE)
        NpmDailyMonth = 2,   // settles NPM daily fees for TargetStallId + TargetYear/TargetMonth
        NpmUtilityBill = 3,  // settles the NPM electricity + water bill for TargetStallId + TargetYear/TargetMonth
        NpmFishDay = 4       // settles ONE NPM fish-section day (TargetStallId + TargetYear/TargetMonth/TargetDay) at ₱30 base + payor-declared kilos × ₱/kg
    }
}
