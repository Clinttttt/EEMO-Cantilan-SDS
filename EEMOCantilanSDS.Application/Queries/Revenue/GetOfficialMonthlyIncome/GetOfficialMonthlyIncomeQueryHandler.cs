using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;

/// <summary>
/// Assembles the official Monthly Income (IA-051). Canonical cash is the Collection line money net of linked corrections,
/// attributed to the ORIGINAL line's classification and business month, and stall rent is read at allocation level so it
/// can be placed by the facility of the rent source. Legacy cash comes from <see cref="ILegacyMonthlyIncomeReader"/>, which
/// excludes every source row that has gone canonical. Remittances, drafts, assessments, opening settlement and shadow
/// rows are never read, so a remittance can never change this report.
/// </summary>
public sealed class GetOfficialMonthlyIncomeQueryHandler(
    IAppDbContext db,
    ILegacyMonthlyIncomeReader legacy,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock clock)
    : IRequestHandler<GetOfficialMonthlyIncomeQuery, Result<OfficialMonthlyIncomeDto>>
{
    private sealed record Fact(int Month, string Code, FacilityCode? Facility, decimal Amount, bool Canonical);

    public async Task<Result<OfficialMonthlyIncomeDto>> Handle(GetOfficialMonthlyIncomeQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return Result<OfficialMonthlyIncomeDto>.Unauthorized();
        if (currentUser.Role is not ("Admin" or "SuperAdmin")) return Result<OfficialMonthlyIncomeDto>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<OfficialMonthlyIncomeDto>.Forbidden();
        if (request.Year is < 2000 or > 2200 || request.Month is < 1 or > 12)
            return Result<OfficialMonthlyIncomeDto>.Failure("Choose a valid year and month.", ResultStatus.Invalid);

        var facts = new List<Fact>();
        facts.AddRange((await legacy.GetAsync(request.Year, ct)).Select(x => new Fact(x.Month, x.ClassificationCode, x.Facility, x.Amount, false)));
        facts.AddRange(await CanonicalAsync(tenantId, request.Year, ct));

        var rowFacts = facts.GroupBy(x => OfficialMonthlyIncomeStructure.RowKeyFor(x.Code, x.Facility))
            .ToDictionary(g => g.Key, g => g.ToList());
        var defined = OfficialMonthlyIncomeStructure.Rows.ToDictionary(x => x.Key);
        var pendingExtras = rowFacts.Keys.Where(k => !defined.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => new OfficialMonthlyIncomeStructure.Row(k, k.Replace("OTHER_", string.Empty), OfficialMonthlyIncomeStructure.Pending,
                k.Replace("OTHER_", string.Empty))).ToList();

        var groups = new List<OfficialMonthlyIncomeGroupDto>();
        foreach (var group in OfficialMonthlyIncomeStructure.Groups)
        {
            var rows = OfficialMonthlyIncomeStructure.Rows.Where(r => r.GroupKey == group.Key)
                .Concat(pendingExtras.Where(r => r.GroupKey == group.Key)).ToList();
            var rowDtos = rows.Select(row => ToRow(row, rowFacts.GetValueOrDefault(row.Key) ?? [])).ToList();
            // Pending rows with no cash are not listed: nothing has been recorded that needs a home.
            if (group.Key == OfficialMonthlyIncomeStructure.Pending) rowDtos = rowDtos.Where(r => r.Total.Total != 0m).ToList();
            if (rowDtos.Count == 0) continue;
            groups.Add(new(group.Key, group.Label, rowDtos, SumMonths(rowDtos.Select(r => r.Months)), SumCell(rowDtos.Select(r => r.Total))));
        }
        var monthTotals = SumMonths(groups.Select(g => g.MonthTotals));
        var notes = new List<string>
        {
            "Each row counts a real collection once: cash whose authoritative record is still a legacy source before its cutover, plus the posted Collection after it. A converted source row's legacy fields are never added beside its Collection.",
            "Remittance is not income: a remittance records money already collected and never changes this statement.",
            "Legacy Fish weighing without frozen rate evidence is priced at the current Fish rate; only newly recorded rows carry a frozen amount.",
            "No annual target is configured, so target and attainment are not shown. Attainment is never replaced by collection efficiency.",
        };
        return Result<OfficialMonthlyIncomeDto>.Success(new OfficialMonthlyIncomeDto(
            request.Year, request.Month, groups, monthTotals, SumCell(monthTotals), false, notes, clock.UtcNow));
    }

    private static OfficialMonthlyIncomeRowDto ToRow(OfficialMonthlyIncomeStructure.Row row, List<Fact> facts)
    {
        var months = Enumerable.Range(1, 12).Select(m => new MonthlyIncomeCellDto(
            facts.Where(f => f.Month == m && !f.Canonical).Sum(f => f.Amount),
            facts.Where(f => f.Month == m && f.Canonical).Sum(f => f.Amount))).ToList();
        var total = SumCell(months);
        var authority = total.Legacy != 0m && total.Canonical != 0m ? "Mixed"
            : total.Canonical != 0m ? "Canonical" : total.Legacy != 0m ? "Legacy" : "None";
        return new(row.Key, row.Label, row.ClassificationCode, months, total, authority, null, null);
    }

    private static MonthlyIncomeCellDto SumCell(IEnumerable<MonthlyIncomeCellDto> cells)
    {
        var list = cells.ToList();
        return new(list.Sum(x => x.Legacy), list.Sum(x => x.Canonical));
    }

    private static IReadOnlyList<MonthlyIncomeCellDto> SumMonths(IEnumerable<IReadOnlyList<MonthlyIncomeCellDto>> rows)
    {
        var list = rows.ToList();
        return Enumerable.Range(0, 12).Select(i => new MonthlyIncomeCellDto(list.Sum(r => r[i].Legacy), list.Sum(r => r[i].Canonical))).ToList();
    }

    private async Task<List<Fact>> CanonicalAsync(Guid tenantId, int year, CancellationToken ct)
    {
        var first = new DateOnly(year, 1, 1);
        var last = new DateOnly(year, 12, 31);
        var codes = await db.RevenueClassifications.AsNoTracking().Where(x => x.MunicipalityId == tenantId)
            .ToDictionaryAsync(x => x.Id, x => x.SemanticCode, ct);
        var facts = new List<Fact>();

        var lines = await (
            from line in db.CollectionLines.AsNoTracking()
            join collection in db.Collections.AsNoTracking() on new { line.MunicipalityId, Id = line.CollectionId } equals new { collection.MunicipalityId, collection.Id }
            where line.MunicipalityId == tenantId && collection.BusinessDate >= first && collection.BusinessDate <= last
            select new { line.Id, line.RevenueClassificationId, collection.BusinessDate, line.Amount }).ToListAsync(ct);
        var lineCorrections = await (
            from correctionLine in db.CollectionCorrectionLines.AsNoTracking()
            join line in db.CollectionLines.AsNoTracking() on new { correctionLine.MunicipalityId, Id = correctionLine.OriginalCollectionLineId } equals new { line.MunicipalityId, line.Id }
            join collection in db.Collections.AsNoTracking() on new { line.MunicipalityId, Id = line.CollectionId } equals new { collection.MunicipalityId, collection.Id }
            where correctionLine.MunicipalityId == tenantId && collection.BusinessDate >= first && collection.BusinessDate <= last
            select new { line.Id, line.RevenueClassificationId, collection.BusinessDate, correctionLine.FinancialEffectAmount }).ToListAsync(ct);

        var rentIds = codes.Where(x => x.Value == RevenueClassificationCodes.PermanentStallRent).Select(x => x.Key).ToHashSet();
        foreach (var line in lines.Where(x => !rentIds.Contains(x.RevenueClassificationId)))
            facts.Add(new(line.BusinessDate.Month, CodeOf(codes, line.RevenueClassificationId), null, line.Amount, true));
        foreach (var effect in lineCorrections.Where(x => !rentIds.Contains(x.RevenueClassificationId)))
            facts.Add(new(effect.BusinessDate.Month, CodeOf(codes, effect.RevenueClassificationId), null, effect.FinancialEffectAmount, true));

        // Stall rent is read at allocation level so each period is placed on its own facility's row.
        var rentLineIds = lines.Where(x => rentIds.Contains(x.RevenueClassificationId)).Select(x => x.Id).ToHashSet();
        if (rentLineIds.Count > 0)
        {
            var allocations = await (
                from allocation in db.CollectionAllocations.AsNoTracking()
                join line in db.CollectionLines.AsNoTracking() on new { allocation.MunicipalityId, Id = allocation.CollectionLineId } equals new { line.MunicipalityId, line.Id }
                join collection in db.Collections.AsNoTracking() on new { line.MunicipalityId, Id = line.CollectionId } equals new { collection.MunicipalityId, collection.Id }
                where allocation.MunicipalityId == tenantId && collection.BusinessDate >= first && collection.BusinessDate <= last
                    && allocation.SourceKind == CollectionSourceKind.PaymentRecord
                select new { allocation.Id, allocation.SourceId, collection.BusinessDate, allocation.Amount, line.RevenueClassificationId }).ToListAsync(ct);
            var rentAllocations = allocations.Where(x => rentIds.Contains(x.RevenueClassificationId)).ToList();
            var recordIds = rentAllocations.Select(x => x.SourceId).Distinct().ToList();
            var facilityOf = await (
                from record in db.PaymentRecords.AsNoTracking()
                join stall in db.Stalls.AsNoTracking() on record.StallId equals stall.Id
                join facility in db.Facilities.AsNoTracking() on stall.FacilityId equals facility.Id
                where record.MunicipalityId == tenantId && recordIds.Contains(record.Id)
                select new { record.Id, facility.Code }).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
            var allocationIds = rentAllocations.Select(x => x.Id).ToList();
            var allocationCorrections = allocationIds.Count == 0 ? [] : await db.CollectionCorrectionAllocations.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && allocationIds.Contains(x.OriginalAllocationId))
                .Select(x => new { x.OriginalAllocationId, x.FinancialEffectAmount }).ToListAsync(ct);
            var byId = rentAllocations.ToDictionary(x => x.Id);
            foreach (var a in rentAllocations)
                facts.Add(new(a.BusinessDate.Month, RevenueClassificationCodes.PermanentStallRent, facilityOf.GetValueOrDefault(a.SourceId), a.Amount, true));
            foreach (var c in allocationCorrections)
            {
                var a = byId[c.OriginalAllocationId];
                facts.Add(new(a.BusinessDate.Month, RevenueClassificationCodes.PermanentStallRent, facilityOf.GetValueOrDefault(a.SourceId), c.FinancialEffectAmount, true));
            }
        }
        return facts;
    }

    private static string CodeOf(Dictionary<Guid, string> codes, Guid id) => codes.GetValueOrDefault(id, "UNKNOWN_CLASSIFICATION");
}
