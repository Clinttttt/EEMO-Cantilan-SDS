namespace EEMOCantilanSDS.Domain.Enums
{
    /// <summary>
    /// Lifecycle of an online (PayMongo/GCash) payment. <see cref="Paid"/> means the money was
    /// received and delinquency has cleared, but the Official Receipt is still pending staff encoding;
    /// <see cref="Completed"/> means staff have encoded the OR.
    /// </summary>
    public enum OnlinePaymentStatus
    {
        Initiated = 1,
        Pending = 2,
        Paid = 3,       // money received — awaiting staff OR encoding
        Completed = 4,  // OR encoded by staff
        Failed = 5,
        Cancelled = 6,
        Expired = 7
    }
}
