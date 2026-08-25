using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// The receipt-level record behind the Report of Collections: what one collector took, day by day, in the period.
///
/// <para>
/// Every line is selected on WHEN THE MONEY WAS TAKEN, the same basis as the collector's own feed and as a remittance, so
/// the document, the app and the cash reconciliation all describe the same event. Amounts are fee money only, the office
/// banking electricity and water separately; those are returned as their own totals so the sheet can state them without
/// mixing them into a collector's fee accountability.
/// </para>
/// </summary>
public interface ICollectorReportQueries
{
    Task<CollectorCollectionsData> GetCollectionsAsync(
        Guid collectorId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <param name="OfficeRecorded">
/// Money taken at the office for the same facilities and period, which is NOT this collector's accountability. Stated so
/// the facility totals and this sheet can be reconciled rather than appearing to contradict each other.
/// </param>
public sealed record CollectorCollectionsData(
    IReadOnlyList<CollectorCollectionLine> Lines,
    IReadOnlyList<CollectorAbsenceLine> Absences,
    decimal OfficeRecorded,
    int OfficeReceipts,
    decimal UtilityBilled,
    decimal UtilityCollected);

/// <param name="FeeDay">The day an NPM daily fee answers for, which is not the day it was taken when arrears are settled.</param>
/// <param name="BilledMonth">
/// The first day of the month a rental answers for. Null for anything not billed monthly. Held as a date rather than a
/// label so the sheet can tell a rental paid within its own month from one paid after it, which a label could not.
/// </param>
public sealed record CollectorCollectionLine(
    string? OrNumber,
    DateTime TakenAtUtc,
    string PayorName,
    string? StallNo,
    FacilityCode Facility,
    string Nature,
    decimal Amount,
    DateOnly? FeeDay,
    DateOnly? BilledMonth);

public sealed record CollectorAbsenceLine(
    DateOnly Day,
    string PayorName,
    string? StallNo,
    FacilityCode Facility);
