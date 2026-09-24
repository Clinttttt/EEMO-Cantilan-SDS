using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetReportOfCollections;

/// <summary>
/// The Report of Collections for one collector over one period: collections attributed to them, receipt by receipt.
/// The office's own document, distinct from the collector's copy in the app; it does not represent a separate remittance/deposit event.
/// </summary>
public sealed record GetReportOfCollectionsQuery(Guid CollectorId, DateOnly From, DateOnly To)
    : IRequest<Result<ReportOfCollectionsDto>>;

/// <param name="DaysWithCollections">
/// Days in the period on which this collector took money. Stated as a count rather than "so many of so many", because what
/// a collector COULD have collected depends on each facility's own calendar, and a figure the document cannot substantiate
/// has no place on it.
/// </param>
/// <param name="OfficeRecorded">
/// Money recorded at the office for the same facilities and period. It is not attributed to this collector, and is stated
/// separately so the facility totals and this sheet do not appear to contradict each other.
/// </param>
public sealed record ReportOfCollectionsDto(
    Guid CollectorId,
    string CollectorName,
    string EmployeeId,
    IReadOnlyList<FacilityCode> AssignedFacilities,
    DateOnly From,
    DateOnly To,
    decimal TotalCollected,
    int ReceiptsIssued,
    int PayorsServed,
    int DaysWithCollections,
    decimal OfficeRecorded,
    int OfficeReceipts,
    IReadOnlyList<ReportFacilityLineDto> Facilities,
    IReadOnlyList<ReportDayLineDto> Days,
    IReadOnlyList<ReportReceiptLineDto> Receipts,
    IReadOnlyList<ReportAbsenceLineDto> Absences,
    decimal UtilityBilled,
    decimal UtilityCollected,

    // Of TotalCollected, the part that answered for a period BEFORE this one — an owed market day, or a rental paid after
    // its month. The office ruled on 2026-09-10 that a month must not appear to have earned what it merely caught up on:
    // ₱566 taken in September of which ₱30 settled an August day means September itself earned ₱536. Stated beside the
    // total rather than moved out of it, because this report describes collections attributed to the collector, not a
    // separate remittance/deposit event. Stating the earlier-period portion separately preserves when it was collected.
    decimal CollectedForEarlierPeriods = 0m);

public sealed record ReportFacilityLineDto(
    FacilityCode Facility,
    int Receipts,
    int Payors,
    decimal Amount);

/// <param name="ReceiptSpan">
/// The receipt numbers of the day as a range where they run unbroken, and a plain count where they do not. Numbers are
/// entered one by one, so the document must not claim a booklet series it cannot prove.
/// </param>
/// <param name="ForEarlierDays">
/// How much of the day's money answered for days before it. Without this a day appears to collect more than it could
/// possibly owe, now that a payor may settle several owed days at once.
/// </param>
public sealed record ReportDayLineDto(
    DateOnly Day,
    string ReceiptSpan,
    int Payors,
    decimal ForEarlierDays,
    decimal Amount);

public sealed record ReportReceiptLineDto(
    string OrNumber,
    DateTime TakenAt,
    string PayorName,
    string? StallNo,
    FacilityCode Facility,
    string FeeFor,
    decimal Amount);

public sealed record ReportAbsenceLineDto(
    DateOnly Day,
    string PayorName,
    string? StallNo,
    FacilityCode Facility);
