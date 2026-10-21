using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Payments;

public sealed partial class NpmRentMonthWorkflow
{
    private sealed record DatedRentAllocation(Guid Id, decimal Amount);
    private sealed record DatedRentCorrection(Guid OriginalAllocationId, decimal FinancialEffectAmount);
    private sealed record ReportEvidence(List<NpmRentMonth> Months, List<Stall> Stalls, List<PaymentRecord> Payments,
        List<DailyCollection> Days, List<CollectionAllocation> Allocations, List<CollectionCorrectionAllocation> Corrections,
        List<DatedRentAllocation> Selected, List<DatedRentCorrection> DatedCorrections, List<AuditLog> Audits);

    /// <summary>Read-only batch. Writers never receive or reuse its prefetched evidence.</summary>
    public async Task<IReadOnlyDictionary<Guid, NpmRentMonthReportPosition?>> ReadReportPositionsAsync(
        IReadOnlyCollection<Guid> stallIds, DateOnly period, DateOnly asOf, CancellationToken ct = default)
    {
        if (period.Day != 1) throw new ArgumentException("Select the first day of an assessment month.", nameof(period));
        var months = await db.NpmRentMonths.AsNoTracking().Where(x => x.MunicipalityId == Tenant
            && stallIds.Contains(x.StallId) && x.PeriodStart == period
            && (x.Status == NpmRentMonthPreparationStatus.FinanciallyActivated || db.CollectionAllocations.Any(a =>
                a.MunicipalityId == Tenant && a.SourceKind == CollectionSourceKind.NpmRentMonth && a.SourceId == x.Id))).ToListAsync(ct);
        if (months.Count == 0) return new Dictionary<Guid, NpmRentMonthReportPosition?>();
        if (!Enabled) throw new InvalidOperationException("The activated monthly report source is unavailable; legacy fallback is prohibited.");
        var evidence = await ReadReportEvidenceAsync(months, asOf, ct);
        var result = new Dictionary<Guid, NpmRentMonthReportPosition?>();
        foreach (var month in months)
        {
            var loaded = await Load(month.StallId, period, ct, evidence);
            var position = await ReadAsOfPositionAsync(loaded, asOf, ct, evidence);
            result[month.StallId] = position is null ? null : await ReportPositionAsync(loaded, position, asOf, ct);
        }
        return result;
    }

    private async Task<ReportEvidence> ReadReportEvidenceAsync(List<NpmRentMonth> months, DateOnly asOf, CancellationToken ct)
    {
        var cutoff = asOf > clock.PhilippineToday ? clock.PhilippineToday : asOf;
        var boundary = PhilippineTime.DayUtcRange(cutoff).EndUtc;
        var monthIds = months.Select(x => x.Id).ToArray(); var stallIds = months.Select(x => x.StallId).Distinct().ToArray();
        var first = months.Min(x => x.PeriodStart); var last = months.Max(x => x.PeriodStart); var to = last.AddMonths(1);
        var stalls = await db.Stalls.AsNoTracking().Include(x => x.Facility).Include(x => x.Contracts)
            .Where(x => x.MunicipalityId == Tenant && stallIds.Contains(x.Id)).ToListAsync(ct);
        var payments = await db.PaymentRecords.AsNoTracking().Where(x => x.MunicipalityId == Tenant && stallIds.Contains(x.StallId)
            && (x.BillingYear > first.Year || x.BillingYear == first.Year && x.BillingMonth >= first.Month)
            && (x.BillingYear < last.Year || x.BillingYear == last.Year && x.BillingMonth <= last.Month)).ToListAsync(ct);
        var days = await db.DailyCollections.AsNoTracking().Where(x => x.MunicipalityId == Tenant && stallIds.Contains(x.StallId)
            && x.CollectionDate >= first && x.CollectionDate < to).ToListAsync(ct);
        var dayIds = days.Select(x => x.Id).ToArray();
        var allocations = await db.CollectionAllocations.AsNoTracking().Where(x => x.MunicipalityId == Tenant
            && (x.SourceKind == CollectionSourceKind.NpmRentMonth && monthIds.Contains(x.SourceId)
                || x.SourceKind == CollectionSourceKind.DailyCollection && x.SourcePart == CollectionSourcePart.DailyFee && dayIds.Contains(x.SourceId)))
            .ToListAsync(ct);
        var ids = allocations.Select(x => x.Id).ToArray();
        var corrections = await db.CollectionCorrectionAllocations.AsNoTracking().Where(x => x.MunicipalityId == Tenant
            && ids.Contains(x.OriginalAllocationId)).ToListAsync(ct);
        var selected = await (from allocation in db.CollectionAllocations.AsNoTracking()
            join line in db.CollectionLines.AsNoTracking() on new { allocation.MunicipalityId, Id = allocation.CollectionLineId } equals new { line.MunicipalityId, line.Id }
            join collection in db.Collections.AsNoTracking() on new { line.MunicipalityId, Id = line.CollectionId } equals new { collection.MunicipalityId, collection.Id }
            where allocation.MunicipalityId == Tenant && ids.Contains(allocation.Id) && collection.BusinessDate <= cutoff && collection.RecordedAtUtc < boundary
            select new DatedRentAllocation(allocation.Id, allocation.Amount)).ToListAsync(ct);
        var selectedIds = selected.Select(x => x.Id).ToArray();
        var dated = await (from allocation in db.CollectionCorrectionAllocations.AsNoTracking()
            join line in db.CollectionCorrectionLines.AsNoTracking() on new { allocation.MunicipalityId, Id = allocation.CorrectionLineId } equals new { line.MunicipalityId, line.Id }
            join correction in db.CollectionCorrections.AsNoTracking() on new { line.MunicipalityId, Id = line.CorrectionId } equals new { correction.MunicipalityId, correction.Id }
            where allocation.MunicipalityId == Tenant && selectedIds.Contains(allocation.OriginalAllocationId)
                && correction.CorrectionEffectiveDate <= cutoff && correction.RecordedAtUtc < boundary
            select new DatedRentCorrection(allocation.OriginalAllocationId, allocation.FinancialEffectAmount)).ToListAsync(ct);
        var audits = await db.AuditLogs.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.EntityId.HasValue && monthIds.Contains(x.EntityId.Value)
            && x.EntityType == "NpmRentMonth" && x.Action == "ReviseNpmRentMonthCredits").ToListAsync(ct);
        return new(months, stalls, payments, days, allocations, corrections, selected, dated, audits);
    }
}
