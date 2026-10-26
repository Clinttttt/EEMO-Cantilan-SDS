using EEMOCantilanSDS.Application.Dtos.Mobile;

namespace EEMOCantilanSDS.Application.Dtos.Payors;

/// <summary>
/// Staff request to issue a code for a HISTORICAL liable account. <see cref="LiableId"/> is the ORIGINAL contract id (stall rent) or the obligation account
/// id (space rental); <see cref="ConfirmedPayer"/> is the liable party's name as the issuer saw it on the Arrears row, so a stale or mistaken row is refused.
/// </summary>
public sealed record IssueHistoricalAccessRequest(ArrearsAccountKind Kind, Guid LiableId, string? ContactNumber, string? ConfirmedPayer, int? ValidityDays);

/// <summary>
/// The issued code. <see cref="Mode"/> is "Activation" when the contact number has no payor account yet (the payor activates with this code) or "Link" when
/// an account already exists (the signed-in payor links the account with this code); the server decides, the issuer never picks.
/// </summary>
public sealed record HistoricalAccessCodeDto(
    ArrearsAccountKind Kind, Guid LiableId, string OperationName, string Space, string PayerName, string Code, string ContactNumber, string Mode, DateTime ExpiresAt);

public sealed record RedeemHistoricalAccessRequest(string? ActivationCode, string? ContactNumber, string? FullName, string? Password);
public sealed record LinkHistoricalAccessRequest(string? ActivationCode);

/// <summary>
/// One unpaid obligation of the payor's OWN historical account, read-only. <see cref="PayableOnline"/> is always false here: online checkout for a lapsed,
/// closed, renewed or legacy debt is not authorized; <see cref="Channel"/> says where it is paid.
/// </summary>
public sealed record PayorHistoricalObligationDto(
    string AccountKey, string Kind, string OperationName, string Space, string Status, string? Context,
    string Period, decimal Assessed, decimal Paid, decimal Balance, string Settlement, string Channel, bool PayableOnline);
