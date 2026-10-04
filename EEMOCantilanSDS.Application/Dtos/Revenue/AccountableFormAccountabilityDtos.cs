using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

/// <summary>
/// Registers a received range of pre-numbered accountable forms from the serials exactly as printed (for example
/// "2315601 A" to "2315650 A"). The quantity is derived from the range; when supplied it must agree. The variant is the printed
/// form designation (for example "51-A") and is unrelated to any letter after the serial digits (IA-059).
/// </summary>
public sealed record RegisterAccountableFormsRequest(
    RevenueInstrumentType InstrumentType, string FirstSerial, string LastSerial, int? Quantity = null,
    string? FormVariant = null, DateOnly? ReceivedOn = null, string? SourceAuthority = null, string? SourceReference = null);

/// <summary>Reports a serial or inclusive range lost or missing, whole or by copy. External evidence is added later.</summary>
public sealed record ReportFormLossRequest(
    Guid FormBookId, long FirstSerialNumber, long LastSerialNumber, AccountableFormCopies Copies, DateOnly LostOn,
    string? Place, string Narrative);

/// <summary>Adds an external follow-up reference (RCD for a cancellation, notice for a loss) to a serial or range.</summary>
public sealed record AddFormReferenceRequest(
    Guid FormBookId, long FirstSerialNumber, long LastSerialNumber, AccountableFormReferenceKind Kind, string Reference, string? Note);

public sealed record FormLossResultDto(int Reported, int BlockedFromIssue, string FirstNumber, string LastNumber);

public sealed record FormReferenceResultDto(int Updated, string FirstNumber, string LastNumber);

/// <summary>One cancelled or lost form (or copy) with its reason, actor and follow-up state.</summary>
public sealed record AccountableFormExceptionDto(
    Guid AccountableDocumentId, string DocumentNumber, RevenueInstrumentType Instrument, string Kind, string Detail,
    string? Place, DateOnly? OccurredOn, string ActorName, DateTime RecordedAtUtc, Guid? CustodianUserId, string? CustodianName,
    bool NeedsFollowUp, IReadOnlyList<string> References);

/// <summary>Physical form counts (not pesos) for one scope.</summary>
public sealed record AccountableFormCountsDto(
    int Registered, int InOffice, int Assigned, int Issued, int Cancelled, int Lost, int NeedsReview, int Skipped, int NeedsFollowUp);

public sealed record AccountableFormCustodianPositionDto(
    Guid? CollectorId, string Name, int Assigned, int Issued, int Cancelled, int Lost, int NeedsReview, IReadOnlyList<SerialRangeDto> OnHand);

public sealed record AccountableFormPositionDto(
    RevenueInstrumentType Instrument, AccountableFormCountsDto Totals, IReadOnlyList<AccountableFormCustodianPositionDto> Custodians,
    IReadOnlyList<SerialRangeDto> InOfficeRanges);

public sealed record AccountableFormHistoryEventDto(
    DateTime AtUtc, string Event, string Serials, int Quantity, string? Actor, string? From, string? To, string? Detail);

/// <summary>
/// One row of the operational accountability support view for a period, from the form ledger: beginning balance, receipts,
/// issued, cancelled, lost and ending balance, each as a quantity with its inclusive serials. It supports preparing the
/// prescribed Report of Accountability for Accountable Forms; it is not that report and does not replace it.
/// </summary>
public sealed record AccountableFormRaafRowDto(
    Guid FormBookId, string Form, int Beginning, int Receipts, int Issued, int Cancelled, int Lost, int Ending,
    IReadOnlyList<SerialRangeDto> BeginningRanges, IReadOnlyList<SerialRangeDto> ReceiptRanges, IReadOnlyList<SerialRangeDto> IssuedRanges,
    IReadOnlyList<SerialRangeDto> CancelledRanges, IReadOnlyList<SerialRangeDto> LostRanges, IReadOnlyList<SerialRangeDto> EndingRanges);

public sealed record AccountableFormRaafSupportDto(
    RevenueInstrumentType Instrument, int Year, int Month, IReadOnlyList<AccountableFormRaafRowDto> Rows);
