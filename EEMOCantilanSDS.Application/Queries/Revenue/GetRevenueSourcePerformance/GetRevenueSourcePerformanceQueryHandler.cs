using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueSourcePerformance;

/// <summary>Revenue-source performance for one month, or for a whole year when <paramref name="Month"/> is null.</summary>
public sealed record GetRevenueSourcePerformanceQuery(int Year, int? Month) : IRequest<Result<RevenueSourcePerformanceDto>>;

/// <summary>
/// Builds the complete revenue-source register from the official Monthly Income, so its money is the statement's money —
/// legacy-authoritative cash before cutover plus canonical cash after it, exactly once — and never a second total. Every
/// source the statement knows is listed, facility or not. Counts come from the period's posted Collections and are left
/// null for any row that also carries legacy money, because they could not describe that money.
/// </summary>
public sealed class GetRevenueSourcePerformanceQueryHandler(
    IRequestHandler<GetOfficialMonthlyIncomeQuery, Result<OfficialMonthlyIncomeDto>> statement,
    IAppDbContext db,
    ICurrentMunicipalityAccessor municipality)
    : IRequestHandler<GetRevenueSourcePerformanceQuery, Result<RevenueSourcePerformanceDto>>
{
    private sealed record CanonicalFact(string RowKey, Guid CollectionId, Guid? DocumentId, Guid? CollectorId);

    public async Task<Result<RevenueSourcePerformanceDto>> Handle(GetRevenueSourcePerformanceQuery request, CancellationToken ct)
    {
        // The statement enforces the role, tenant and period rules; this register adds none of its own.
        var income = await statement.Handle(new GetOfficialMonthlyIncomeQuery(request.Year, request.Month), ct);
        if (!income.IsSuccess || income.Value is null)
            return Result<RevenueSourcePerformanceDto>.Failure(income.Error ?? "The Monthly Income could not be read.", income.Status);

        var tenantId = municipality.MunicipalityId;
        var from = request.Month is { } m ? new DateOnly(request.Year, m, 1) : new DateOnly(request.Year, 1, 1);
        var to = request.Month is { } mm ? new DateOnly(request.Year, mm, 1).AddMonths(1).AddDays(-1) : new DateOnly(request.Year, 12, 31);
        var facts = await CanonicalFactsAsync(tenantId, from, to, ct);
        var instruments = await InstrumentsAsync(tenantId, to, ct);

        var rows = new List<RevenueSourcePerformanceRowDto>();
        foreach (var group in income.Value.Groups)
        foreach (var row in group.Rows)
        {
            var cell = request.Month is { } month ? row.Months[month - 1] : row.Total;
            var entry = RevenueSourceCatalog.For(row.Key);
            var awaiting = group.Key == OfficialMonthlyIncomeStructure.Pending;
            var mine = facts.Where(f => f.RowKey == row.Key).ToList();
            // Counts describe canonical collections only, so a row holding legacy money in this period has none it can state.
            var countable = cell.Legacy == 0m;
            rows.Add(new RevenueSourcePerformanceRowDto(
                row.Key, row.Label, entry.GroupKey, RevenueSourceCatalog.GroupLabel(entry.GroupKey), entry.Model,
                row.ClassificationCode is { } code ? instruments.GetValueOrDefault(code) : null,
                entry.Facility,
                cell.Total, cell.Legacy, cell.Canonical,
                countable ? mine.Select(f => f.CollectionId).Distinct().Count() : null,
                null, // A physical document count is no longer derivable: collections are identified by SRC (IA-062).
                countable ? mine.Where(f => f.CollectorId is not null).Select(f => f.CollectorId).Distinct().Count() : null,
                RevenueSourceCatalog.StatusFor(entry.Model, cell.Total, awaiting),
                awaiting));
        }

        var order = RevenueSourceCatalog.Groups.Select((g, i) => (g.Key, i)).ToDictionary(x => x.Key, x => x.i);
        rows = rows.OrderBy(r => order.GetValueOrDefault(r.GroupKey, 99)).ToList();
        var groups = RevenueSourceCatalog.Groups
            .Where(g => rows.Any(r => r.GroupKey == g.Key))
            .Select(g => new RevenueSourceGroupDto(g.Key, g.Label, rows.Where(r => r.GroupKey == g.Key).Sum(r => r.Collected)))
            .ToList();

        var notes = new List<string>
        {
            "Collected is the official Monthly Income for the same period: each real collection counted once, under the authority of its source.",
            "Counts are read from posted collections; a source that still holds legacy records in the period shows no count rather than a partial one.",
            "A source paid on service has no assessment, so it never shows an unpaid amount or a collection rate.",
        };
        return Result<RevenueSourcePerformanceDto>.Success(new RevenueSourcePerformanceDto(
            request.Year, request.Month, groups, rows, rows.Sum(r => r.Collected), notes, income.Value.GeneratedAtUtc));
    }

    private async Task<List<CanonicalFact>> CanonicalFactsAsync(Guid tenantId, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var codes = await db.RevenueClassifications.AsNoTracking().Where(x => x.MunicipalityId == tenantId)
            .ToDictionaryAsync(x => x.Id, x => x.SemanticCode, ct);
        var lines = await (
            from line in db.CollectionLines.AsNoTracking()
            join collection in db.Collections.AsNoTracking() on new { line.MunicipalityId, Id = line.CollectionId } equals new { collection.MunicipalityId, collection.Id }
            where line.MunicipalityId == tenantId && collection.BusinessDate >= start && collection.BusinessDate <= end
            select new { line.Id, line.CollectionId, line.RevenueClassificationId, collection.CollectorId }).ToListAsync(ct);
        if (lines.Count == 0) return [];

        var collectionIds = lines.Select(x => x.CollectionId).Distinct().ToArray();

        // Stall rent is placed by the facility of the rent source, as the statement places it.
        var rentLineIds = lines.Where(x => codes.GetValueOrDefault(x.RevenueClassificationId) == RevenueClassificationCodes.PermanentStallRent)
            .Select(x => x.Id).ToArray();
        var rentFacility = new Dictionary<Guid, FacilityCode?>();
        if (rentLineIds.Length > 0)
        {
            var placed = await (
                from allocation in db.CollectionAllocations.AsNoTracking()
                join record in db.PaymentRecords.AsNoTracking() on allocation.SourceId equals record.Id
                join stall in db.Stalls.AsNoTracking() on record.StallId equals stall.Id
                join facility in db.Facilities.AsNoTracking() on stall.FacilityId equals facility.Id
                where allocation.MunicipalityId == tenantId && rentLineIds.Contains(allocation.CollectionLineId)
                    && allocation.SourceKind == CollectionSourceKind.PaymentRecord
                select new { allocation.CollectionLineId, facility.Code }).ToListAsync(ct);
            foreach (var p in placed) rentFacility.TryAdd(p.CollectionLineId, p.Code);
        }

        return lines.Select(line =>
        {
            var code = codes.GetValueOrDefault(line.RevenueClassificationId, "UNKNOWN_CLASSIFICATION");
            var facility = code == RevenueClassificationCodes.PermanentStallRent ? rentFacility.GetValueOrDefault(line.Id) : null;
            return new CanonicalFact(OfficialMonthlyIncomeStructure.RowKeyFor(code, facility), line.CollectionId,
                null, line.CollectorId);
        }).ToList();
    }

    /// <summary>The instrument(s) the classification policy in force at the period end permits, by classification code.</summary>
    private async Task<Dictionary<string, string>> InstrumentsAsync(Guid tenantId, DateOnly asOf, CancellationToken ct)
    {
        var policies = await (
            from policy in db.RevenueClassificationPolicies.AsNoTracking()
            join classification in db.RevenueClassifications.AsNoTracking() on policy.RevenueClassificationId equals classification.Id
            where policy.MunicipalityId == tenantId && policy.EffectiveDate <= asOf
            select new { classification.SemanticCode, policy.BusinessContext, policy.EffectiveDate, policy.PermittedInstrumentType })
            .ToListAsync(ct);

        // The latest policy per business context; a classification with OR and CT contexts (Vegetable / Fruit) states both.
        return policies
            .GroupBy(p => p.SemanticCode)
            .ToDictionary(g => g.Key, g => string.Join(" · ", g
                .GroupBy(p => p.BusinessContext)
                .Select(c => c.OrderByDescending(p => p.EffectiveDate).First().PermittedInstrumentType)
                .Where(i => i is not null)
                .Select(i => i == RevenueInstrumentType.OfficialReceipt ? "OR" : i == RevenueInstrumentType.CashTicket ? "CT" : i!.Value.ToString())
                .Distinct()
                .OrderBy(s => s, StringComparer.Ordinal)))
            .Where(kv => kv.Value.Length > 0)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
