namespace EEMOCantilanSDS.Domain.Enums;

/// <summary>
/// What the Head decided about captured money the system would not apply by itself. This is a record of the outcome and moves no money:
/// Applied cites the receipt the office issued through its normal payment path, Refunded cites the gateway's refund.
/// </summary>
public enum OnlinePaymentDisposition
{
    Applied = 1,
    Refunded = 2
}
