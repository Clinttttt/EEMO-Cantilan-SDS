using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Revenue;

public sealed record WcfObligationQuoteDto(
    Guid MunicipalityId, Guid UtilityBillId, long WaterSourceVersion,
    Guid StallId, string StallNo, string FacilityName, string Section,
    int BillingYear, int BillingMonth, decimal PreviousReading, decimal CurrentReading,
    decimal Consumption, decimal RatePerCubicMeter, decimal AssessedAmount,
    decimal CumulativeSettledEvidence, decimal OutstandingAmount,
    SettlementAuthority SettlementAuthority, Guid? PayorId, string? PayerNameSnapshot,
    Guid RevenueClassificationId, Guid RevenueClassificationPolicyId, string ClassificationName,
    RevenueInstrumentType Instrument, string ChargeBasis, bool CanCollectCanonical);

public sealed record CashTicketDocumentDto(
    Guid DocumentId, string DocumentNumber, AccountableDocumentState State,
    Guid? AssignedUserId, long SerialNumber);

/// <summary>Versioned received-money intent for one assigned physical Cash Ticket.</summary>
public sealed record WcfCollectionPostRequest(
    int SchemaVersion, Guid ClientOperationId, DateOnly BusinessDate,
    Guid UtilityBillId, decimal ReceivedAmount, long WaterSourceVersion,
    Guid AccountableDocumentId, string DocumentNumber, DateTime? IssuedAtUtc);

public sealed record WcfCollectionOutcomeDto(
    Guid CollectionId, Guid AccountableDocumentId, string DocumentNumber,
    DateOnly BusinessDate, decimal Amount, string Disposition, bool ExistingOutcome);

public sealed record WcfCollectionActivityLineDto(
    string ClassificationName, decimal Amount, int? BillingYear, int? BillingMonth,
    string? SourceLabel, string? CalculationSnapshot, IReadOnlyList<string> AllocationSnapshots);

public sealed record WcfCollectionActivityDto(
    Guid CollectionId, DateOnly BusinessDate, DateTime RecordedAtUtc,
    string DocumentNumber, string? PayerName, decimal TotalAmount, int ItemCount,
    string Disposition, IReadOnlyList<WcfCollectionActivityLineDto> Lines);

public sealed record ReceiveAccountableFormBookRequest(
    RevenueInstrumentType InstrumentType, string SeriesName, string NumberPrefix,
    long FirstSerialNumber, long LastSerialNumber, int SerialWidth);

public sealed record AssignAccountableFormRangeRequest(
    Guid FormBookId, Guid AssignedUserId, long FirstSerialNumber, long LastSerialNumber);

public sealed record AccountableFormBookDto(
    Guid BookId, RevenueInstrumentType InstrumentType, string SeriesName,
    string NumberPrefix, long FirstSerialNumber, long LastSerialNumber,
    IReadOnlyList<CashTicketDocumentDto> Documents);

public sealed record LegacyWaterSyncResolution(
    bool RequiresReconciliation, string? Message, bool PreserveWaterSource = false);

public sealed record WcfReconciliationExceptionDto(
    Guid ClientOperationId, string Origin, DateTime RecordedAtUtc,
    string OutcomeCode, string OutcomeDetails, Guid? AccountableDocumentId,
    string? DocumentNumber);

/// <summary>
/// Head/Admin request to establish (or, before any settlement, revise) the direct approved Water amount for one source and
/// billing period (IA-053). It writes only the Water part of the single stall/month UtilityBill; Electricity is untouched.
/// </summary>
public sealed record WcfObligationSetupRequest(Guid StallId, int BillingYear, int BillingMonth, decimal ApprovedAmount);

/// <summary>
/// A Water source a Head/Admin may set up for a period: the stall that is the source context, the payor answering for the
/// month, and the Water part as it stands. <see cref="Editable"/> is false once settlement or cutover has begun.
/// </summary>
public sealed record WcfSetupSourceDto(
    Guid StallId, string StallNo, string Section, string? PayerName,
    Guid? UtilityBillId, decimal? ApprovedAmount, string? ChargeBasis,
    SettlementAuthority? SettlementAuthority, decimal SettledAmount, bool Editable, string? LockedReason);

/// <summary>The activation dry-run request for one Water obligation.</summary>
public sealed record WcfActivationReadinessRequest(Guid UtilityBillId, SettlementCutoverReconciliationEvidence? Evidence);
