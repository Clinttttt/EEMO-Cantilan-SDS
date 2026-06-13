using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Dtos.Mobile;

/// <summary>
/// One month's rental-collection snapshot for a monthly-billed facility (TCC, NCC, BBQ, ICE),
/// scoped to the stalls a collector is assigned to. Mirrors <see cref="MobileNpmCollectionDto"/>
/// but for monthly PaymentRecords rather than daily collections.
/// </summary>
public sealed record MobileMonthlyCollectionDto(
    FacilityCode Facility,
    string FacilityName,
    int Year,
    int Month,
    DateOnly CollectionDate,
    int TotalStalls,
    int PaidCount,
    int PartialCount,
    int UnpaidCount,
    decimal CollectedAmount,
    decimal OutstandingAmount,
    IReadOnlyList<MobileMonthlyStallCollectionDto> Stalls);

public sealed record MobileMonthlyStallCollectionDto(
    Guid StallId,
    string StallNo,
    string PayorName,
    string ContractName,
    string AreaLabel,
    decimal MonthlyRate,
    PaymentStatus Status,
    decimal AmountPaid,
    decimal Balance,
    string? ORNumber,
    bool IsRecorded,
    bool PaidOnline = false,
    bool AwaitingOr = false,
    Guid? OnlinePaymentTransactionId = null);
