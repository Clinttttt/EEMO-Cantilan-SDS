using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Collectors.GetReportOfCollections;

/// <summary>
/// The Report of Collections for one collector over one period: what they took, receipt by receipt, and what they have
/// since turned in. The office's own document, distinct from the collector's copy in the app.
/// </summary>
public sealed record GetReportOfCollectionsQuery(Guid CollectorId, DateOnly From, DateOnly To)
    : IRequest<Result<ReportOfCollectionsDto>>;

/// <param name="DaysWithCollections">
/// Days in the period on which this collector took money. Stated as a count rather than "so many of so many", because what
/// a collector COULD have collected depends on each facility's own calendar, and a figure the document cannot substantiate
/// has no place on it.
/// </param>
/// <param name="OfficeRecorded">
/// Money taken at the office for the same facilities and period. Not this collector's accountability, and stated so the
/// facility totals and this sheet do not appear to contradict each other.
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
    decimal Remitted,
    decimal NotYetRemitted,
    IReadOnlyList<ReportFacilityLineDto> Facilities,
    IReadOnlyList<ReportDayLineDto> Days,
    IReadOnlyList<ReportReceiptLineDto> Receipts,
    IReadOnlyList<ReportAbsenceLineDto> Absences,
    IReadOnlyList<ReportRemittanceLineDto> Remittances,
    decimal UtilityBilled,
    decimal UtilityCollected);

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

public sealed record ReportRemittanceLineDto(
    DateTime ReceivedAt,
    decimal Amount,
    DateOnly CoversFrom,
    DateOnly CoversTo,
    string ReceivedByName,
    string? ReferenceNo);
