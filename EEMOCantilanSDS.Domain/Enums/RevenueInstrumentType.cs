namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>
/// An accountable instrument permitted by a tenant's effective-dated revenue policy.
/// This describes policy only; it is not an issued receipt or form record.
/// </summary>
public enum RevenueInstrumentType
{
    OfficialReceipt = 1,
    CashTicket = 2
}
