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
    Guid AccountableDocumentId, string DocumentNumber, DateTime? IssuedAtUtc,
    // Direct entry (UtilityBillId empty): the eligible source and billing period the collector selected. The server
    // establishes the Water amount on that source and posts it in one operation; an office-prepared amount wins.
    Guid? StallId = null, int? BillingYear = null, int? BillingMonth = null);

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

/// <summary>Where one eligible Water source stands for Collector Mobile, in operational terms.</summary>
public static class WcfSourceState
{
    /// <summary>No amount is prepared: the collector may enter the Water amount directly.</summary>
    public const string NoAmount = "NoAmount";
    /// <summary>The office prepared an amount; the collector collects against it and cannot change it.</summary>
    public const string Prepared = "Prepared";
    /// <summary>Nothing is outstanding for the period.</summary>
    public const string Settled = "Settled";
    /// <summary>Historical (legacy) settlement or a migration in progress: the office resolves it.</summary>
    public const string NeedsOffice = "NeedsOffice";
}

/// <summary>
/// One eligible WCF source for a collector and billing period: the stall that is its context, the payor answering for the
/// month (by explicit link where one exists), and the Water part as it stands. Amounts are the server's.
/// </summary>
public sealed record WcfMobileSourceDto(
    Guid StallId, string StallNo, string Section, Guid? PayorId, string? PayerName,
    int BillingYear, int BillingMonth,
    Guid? UtilityBillId, long WaterSourceVersion,
    decimal? PreparedAmount, decimal SettledAmount, decimal OutstandingAmount,
    string State, bool CanCollect, bool CanEnterDirect);

/// <summary>WCF Mobile collection status for the office: active or not, and — only while inactive — what blocks it.</summary>
public sealed record WcfMobileStatusDto(
    bool Active, DateOnly? EffectiveFrom, DateTime? ActivatedAtUtc, string? ActivatedBy,
    bool ReadyToEnable,
    IReadOnlyList<WcfReadinessItemDto> Blockers,
    IReadOnlyList<WcfReadinessItemDto> Checks,
    IReadOnlyList<WcfCollectorReadinessDto> Collectors,
    int HistoricalWaterRecords);

/// <summary>One system-derived readiness check; never a fact the office is asked to attest.</summary>
public sealed record WcfReadinessItemDto(string Code, string Title, string Detail, bool Ok, string? ActionLabel, string? ActionHref);

public sealed record WcfCollectorReadinessDto(
    Guid CollectorId, string Name, bool Active, int CashTicketsHeld, int CashTicketsInReview, bool Ready);
