using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.Application.Common.Interface.Persistence;

/// <summary>
/// The receipt-level record behind the Report of Collections: what one collector took, day by day, in the period.
///
/// <para>
/// Every line is selected on the collection's business date when that canonical date exists; legacy rows retain their
/// established source-date fallback. Canonical WCF Cash Tickets are receipt lines. Legacy utility source projections remain
/// separate, while canonical Water projections contribute only to outstanding position.
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
    decimal UtilityCollected,
    decimal UtilityOutstanding = 0m,
    /// <summary>
    /// Canonical collections of governed operations (Market Fees, Landing/Berthing, ...) that this collector took. They have
    /// no facility, so they are stated apart and never folded into a facility total.
    /// </summary>
    IReadOnlyList<CollectorOperationCollection>? OperationCollections = null);

public sealed record CollectorOperationCollection(
    string DocumentNumber,
    DateTime TakenAtUtc,
    DateOnly BusinessDate,
    string OperationCode,
    string OperationName,
    RevenueInstrumentType? Instrument,
    string? PayerName,
    string? Reference,
    decimal Amount);

/// <param name="FeeDay">The day an NPM daily fee answers for, which is not the day it was taken when arrears are settled.</param>
/// <param name="BilledMonth">
/// The first day of the billing month answered for by a monthly source. Null for non-monthly sources. Held as a date rather
/// than a label so the sheet can compare the obligation period with the cash business date.
/// </param>
public sealed record CollectorCollectionLine(
    string? DocumentNumber,
    DateTime TakenAtUtc,
    string PayorName,
    string? StallNo,
    FacilityCode Facility,
    string Nature,
    decimal Amount,
    DateOnly? FeeDay,
    DateOnly? BilledMonth,
    DateOnly? BusinessDate = null,
    // True when the line is read from a posted canonical Collection; false for a legacy source row (IA-052 coverage).
    bool IsCanonical = false);

public sealed record CollectorAbsenceLine(
    DateOnly Day,
    string PayorName,
    string? StallNo,
    FacilityCode Facility);
