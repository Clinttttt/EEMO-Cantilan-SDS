using System.Text.Json;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Common.Billing;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Payments;

public sealed partial class NpmRentMonthWorkflow
{
    /// <summary>Earned liability at the requested cutoff, not today's earned position projected backward.</summary>
    public async Task<NpmRentMonthReportPosition?> ReadReportPositionAsync(Guid stallId, DateOnly period, DateOnly asOf, CancellationToken ct = default)
    {
        if (!await IsMonthActivatedAsync(stallId, period, ct)) return null;
        var loaded = await Load(stallId, period, ct);
        var position = await ReadAsOfPositionAsync(loaded, asOf, ct);
        if (position is null) return null;
        return await ReportPositionAsync(loaded, position, asOf, ct);
    }

    private async Task<NpmRentMonthReportPosition> ReportPositionAsync(LoadedMonth loaded, NpmRentMonthPosition position, DateOnly asOf, CancellationToken ct)
    {
        var period = loaded.Month.PeriodStart;
        var cutoff = asOf > clock.PhilippineToday ? clock.PhilippineToday : asOf;
        using var policy = JsonDocument.Parse(loaded.Month.RateEvidenceJson);
        decimal earned;
        if (OpeningEvidence(loaded.Month) is { } opening)
            earned = position.EarnedInstallmentLiability(opening.ApprovedDailyInstallment, opening.OccupancyStart, cutoff);
        else if (policy.RootElement.TryGetProperty("monthlyObligationDueFrom", out var due))
        {
            var dueDate = DateOnly.ParseExact(due.GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            earned = cutoff >= dueDate ? position.Assessment - position.Credits : 0m;
        }
        else if (cutoff >= period.AddMonths(1)) earned = position.Assessment - position.Credits;
        else
        {
            if (!Approved(policy, "dailyInstallmentsApproved") || assessments is null)
                throw new InvalidOperationException("An approved earned assessment/due-date policy is required for this report.");
            var assessment = await assessments.ResolveAssessmentAsOfAsync(loaded.Stall, period.Year, period.Month, cutoff, ct);
            earned = Math.Min(position.Assessment - position.Credits, Math.Max(0, assessment.Obligation - position.Credits));
        }
        return new(position, earned, OpeningEvidence(loaded.Month));
    }

    /// <summary>Signed cash movements on their actual collection/correction dates; never spread over invented days.</summary>
    public async Task<IReadOnlyList<NpmRentMonthCashMovement>> ReadCashMovementsAsync(Guid facilityId, DateOnly fromDate, DateOnly to, CancellationToken ct = default, DateOnly? asOf = null)
    {
        var cutoff = asOf ?? to;
        if (cutoff > clock.PhilippineToday) cutoff = clock.PhilippineToday;
        if (fromDate > cutoff || fromDate > to) return [];
        var boundary = PhilippineTime.DayUtcRange(cutoff).EndUtc;
        var originals = await (from allocation in db.CollectionAllocations.AsNoTracking()
            join source in db.NpmRentMonths.AsNoTracking() on new { allocation.MunicipalityId, Id = allocation.SourceId } equals new { source.MunicipalityId, source.Id }
            join line in db.CollectionLines.AsNoTracking() on new { allocation.MunicipalityId, Id = allocation.CollectionLineId } equals new { line.MunicipalityId, line.Id }
            join collection in db.Collections.AsNoTracking() on new { line.MunicipalityId, Id = line.CollectionId } equals new { collection.MunicipalityId, collection.Id }
            where allocation.MunicipalityId == Tenant && allocation.SourceKind == EEMOCantilanSDS.Domain.Enums.CollectionSourceKind.NpmRentMonth
                && source.FacilityId == facilityId && collection.BusinessDate >= fromDate && collection.BusinessDate <= to && collection.BusinessDate <= cutoff && collection.RecordedAtUtc < boundary
            select new NpmRentMonthCashMovement(source.Id, source.StallId, source.ContractId, source.PeriodStart, collection.Id, collection.BusinessDate, allocation.Amount, allocation.SourceSnapshot)).ToListAsync(ct);
        var effects = await (from correctionAllocation in db.CollectionCorrectionAllocations.AsNoTracking()
            join allocation in db.CollectionAllocations.AsNoTracking() on new { correctionAllocation.MunicipalityId, Id = correctionAllocation.OriginalAllocationId } equals new { allocation.MunicipalityId, allocation.Id }
            join source in db.NpmRentMonths.AsNoTracking() on new { allocation.MunicipalityId, Id = allocation.SourceId } equals new { source.MunicipalityId, source.Id }
            join line in db.CollectionCorrectionLines.AsNoTracking() on new { correctionAllocation.MunicipalityId, Id = correctionAllocation.CorrectionLineId } equals new { line.MunicipalityId, line.Id }
            join correction in db.CollectionCorrections.AsNoTracking() on new { line.MunicipalityId, Id = line.CorrectionId } equals new { correction.MunicipalityId, correction.Id }
            where allocation.MunicipalityId == Tenant && allocation.SourceKind == EEMOCantilanSDS.Domain.Enums.CollectionSourceKind.NpmRentMonth
                && source.FacilityId == facilityId && correction.CorrectionEffectiveDate >= fromDate && correction.CorrectionEffectiveDate <= to && correction.CorrectionEffectiveDate <= cutoff && correction.RecordedAtUtc < boundary
            select new NpmRentMonthCashMovement(source.Id, source.StallId, source.ContractId, source.PeriodStart, correction.OriginalCollectionId, correction.CorrectionEffectiveDate, correctionAllocation.FinancialEffectAmount, allocation.SourceSnapshot)).ToListAsync(ct);
        var movements = originals.Concat(effects).ToArray();
        var sources = movements.DistinctBy(x => x.MonthId).ToArray();
        ReportEvidence? evidence = null;
        if (sources.Length > 1)
        {
            if (!Enabled) throw new InvalidOperationException("The activated cash report source is unavailable; legacy fallback is prohibited.");
            var ids = sources.Select(x => x.MonthId).ToArray();
            var months = await db.NpmRentMonths.AsNoTracking().Where(x => x.MunicipalityId == Tenant && ids.Contains(x.Id)).ToListAsync(ct);
            evidence = await ReadReportEvidenceAsync(months, cutoff, ct);
        }
        foreach (var source in sources) await Load(source.StallId, source.PeriodStart, ct, evidence);
        return movements;
    }
    /// <summary>Real month-source cash evidence, including original amounts and separately linked corrections.</summary>
    public async Task<IReadOnlyList<NpmRentMonthCashEvent>> ReadCashEventsAsync(Guid stallId, DateOnly period, CancellationToken ct = default)
    {
        var loaded = await Load(stallId, period, ct);
        var ids = loaded.Allocations.Where(x => x.SourceKind == EEMOCantilanSDS.Domain.Enums.CollectionSourceKind.NpmRentMonth)
            .Select(x => x.Id).ToArray();
        var rows = await (from allocation in db.CollectionAllocations.AsNoTracking()
            join line in db.CollectionLines.AsNoTracking() on new { allocation.MunicipalityId, Id = allocation.CollectionLineId } equals new { line.MunicipalityId, line.Id }
            join collection in db.Collections.AsNoTracking() on new { line.MunicipalityId, Id = line.CollectionId } equals new { collection.MunicipalityId, collection.Id }
            where allocation.MunicipalityId == Tenant && ids.Contains(allocation.Id)
            orderby collection.BusinessDate, collection.RecordedAtUtc, allocation.Id
            select new { AllocationId = allocation.Id, allocation.SourceSnapshot, allocation.Amount, CollectionId = collection.Id, collection.ReferenceCode,
                collection.BusinessDate, collection.RecordedAtUtc, collection.CollectorId }).ToListAsync(ct);
        return rows.Select(x =>
        {
            using var snapshot = JsonDocument.Parse(x.SourceSnapshot ?? "");
            var date = snapshot.RootElement.TryGetProperty("installmentDate", out var element) && element.ValueKind == JsonValueKind.String
                ? DateOnly.ParseExact(element.GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : (DateOnly?)null;
            return new NpmRentMonthCashEvent(x.AllocationId, x.CollectionId, x.ReferenceCode, x.BusinessDate, x.RecordedAtUtc, date, x.Amount,
                loaded.Corrections.Where(c => c.OriginalAllocationId == x.AllocationId).Sum(c => c.FinancialEffectAmount), x.CollectorId);
        }).ToArray();
    }
    /// <summary>
    /// Activated-source position at a business-date cutoff. Before activation, authority remains historical/legacy.
    /// Opening legacy cash is the explicitly reviewed activation baseline; later cash/corrections use their own dates.
    /// </summary>
    public async Task<NpmRentMonthPosition?> ReadAsOfPositionAsync(Guid stallId, DateOnly period, DateOnly asOf, CancellationToken ct = default)
    {
        if (!await IsMonthActivatedAsync(stallId, period, ct)) return null;
        var loaded = await Load(stallId, period, ct);
        return await ReadAsOfPositionAsync(loaded, asOf, ct);
    }

    private async Task<NpmRentMonthPosition?> ReadAsOfPositionAsync(LoadedMonth loaded, DateOnly asOf, CancellationToken ct, ReportEvidence? report = null)
    {
        var month = loaded.Month;
        var cutoff = asOf > clock.PhilippineToday ? clock.PhilippineToday : asOf;
        var boundary = PhilippineTime.DayUtcRange(cutoff).EndUtc;
        if (cutoff < DateOnly.FromDateTime(PhilippineTime.ToPhilippineTime(month.SettlementActivatedAtUtc!.Value))) return null;
        var ids = loaded.Allocations.Select(x => x.Id).ToArray();
        var selected = report is null ? await (from allocation in db.CollectionAllocations.AsNoTracking()
            join line in db.CollectionLines.AsNoTracking() on new { allocation.MunicipalityId, Id = allocation.CollectionLineId }
                equals new { line.MunicipalityId, line.Id }
            join collection in db.Collections.AsNoTracking() on new { line.MunicipalityId, Id = line.CollectionId }
                equals new { collection.MunicipalityId, collection.Id }
            where allocation.MunicipalityId == Tenant && ids.Contains(allocation.Id) && collection.BusinessDate <= cutoff && collection.RecordedAtUtc < boundary
            select new DatedRentAllocation(allocation.Id, allocation.Amount)).ToListAsync(ct)
            : report.Selected.Where(x => ids.Contains(x.Id)).ToList();
        var selectedIds = selected.Select(x => x.Id).ToArray();
        var corrections = report is null ? await (from allocation in db.CollectionCorrectionAllocations.AsNoTracking()
            join line in db.CollectionCorrectionLines.AsNoTracking() on new { allocation.MunicipalityId, Id = allocation.CorrectionLineId }
                equals new { line.MunicipalityId, line.Id }
            join correction in db.CollectionCorrections.AsNoTracking() on new { line.MunicipalityId, Id = line.CorrectionId }
                equals new { correction.MunicipalityId, correction.Id }
            where allocation.MunicipalityId == Tenant && selectedIds.Contains(allocation.OriginalAllocationId)
                && correction.CorrectionEffectiveDate <= cutoff && correction.RecordedAtUtc < boundary
            select new DatedRentCorrection(allocation.OriginalAllocationId, allocation.FinancialEffectAmount)).ToListAsync(ct)
            : report.DatedCorrections.Where(x => selectedIds.Contains(x.OriginalAllocationId)).ToList();
        decimal canonical = 0;
        foreach (var allocation in selected)
        {
            var net = allocation.Amount + corrections.Where(x => x.OriginalAllocationId == allocation.Id).Sum(x => x.FinancialEffectAmount);
            if (net < 0) throw new InvalidOperationException("Historical correction exceeds original month evidence.");
            canonical += net;
        }
        var credits = month.CreditedAmount;
        var audits = report is null ? await db.AuditLogs.AsNoTracking().Where(x => x.MunicipalityId == Tenant && x.EntityId == month.Id
            && x.EntityType == "NpmRentMonth" && x.Action == "ReviseNpmRentMonthCredits").ToListAsync(ct)
            : report.Audits.Where(x => x.EntityId == month.Id).ToList();
        var revisions = audits.Select(x =>
        {
            using var next = JsonDocument.Parse(x.NewValues ?? "");
            using var old = JsonDocument.Parse(x.OldValues ?? "");
            return new { Utc = next.RootElement.GetProperty("UpdatedAtUtc").GetDateTime(),
                Version = next.RootElement.GetProperty("SettlementVersion").GetInt64(),
                Credits = old.RootElement.GetProperty("CreditedAmount").GetDecimal() };
        }).OrderBy(x => x.Utc).ThenBy(x => x.Version);
        var subsequent = revisions.FirstOrDefault(x => x.Utc >= boundary);
        if (subsequent is not null) credits = subsequent.Credits;
        if (credits < 0 || credits > month.AssessedAmount) throw new InvalidOperationException("Invalid historical approved credit evidence.");
        var received = loaded.Position.LegacyCash + canonical;
        var liability = month.AssessedAmount - credits;
        return loaded.Position with { Credits = credits, CanonicalCash = canonical,
            Outstanding = Math.Max(0, liability - received), HistoricalExcess = Math.Max(0, received - liability) };
    }
}
