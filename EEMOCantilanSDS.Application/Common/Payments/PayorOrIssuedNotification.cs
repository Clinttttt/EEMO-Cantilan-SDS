namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// A realtime alert pushed to ONE payor when staff encode the Official Receipt (OR) for their online
/// payment — turning the provisional acknowledgment into a complete digital receipt. Transport-agnostic
/// (SignalR lives in the API layer behind <c>IPayorRealtimeNotifier</c>).
/// </summary>
public sealed record PayorOrIssuedNotification(
    string Reference,
    string OrNumber,
    decimal Amount,
    string Period,
    Guid StallId);
