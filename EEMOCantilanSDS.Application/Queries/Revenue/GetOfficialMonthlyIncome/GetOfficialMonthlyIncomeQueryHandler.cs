using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Domain.Entities.Revenue;
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
        var revisions = await db.OfficialReportRevisions.AsNoTracking().Where(x =>
            x.MunicipalityId == tenantId && x.Year == request.Year).ToListAsync(ct);
        var currentRevisions = revisions.GroupBy(x => (x.Kind, x.RowKey, x.Month))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Revision).First());
        foreach (var group in OfficialMonthlyIncomeStructure.Groups)
        {
            var rows = OfficialMonthlyIncomeStructure.Rows.Where(r => r.GroupKey == group.Key)
                .Concat(pendingExtras.Where(r => r.GroupKey == group.Key)).ToList();
            var rowDtos = rows.Select(row => ToRow(row, rowFacts.GetValueOrDefault(row.Key) ?? [], currentRevisions,
                request.Month ?? (request.Year < clock.PhilippineToday.Year ? 12 : request.Year == clock.PhilippineToday.Year ? clock.PhilippineToday.Month : 0))).ToList();
            // Pending rows with no cash are not listed: nothing has been recorded that needs a home.
            if (group.Key == OfficialMonthlyIncomeStructure.Pending) rowDtos = rowDtos.Where(r =>
                r.Total.Total != 0m || r.AnnualTarget.HasValue || r.Months.Any(m => m.IsAdjusted)).ToList();
            if (rowDtos.Count == 0) continue;
            groups.Add(new(group.Key, group.Label, rowDtos, SumMonths(rowDtos.Select(r => r.Months)), SumCell(rowDtos.Select(r => r.Total))));
        }
        var monthTotals = SumMonths(groups.Select(g => g.MonthTotals));
        var notes = new List<string>
        {
            "Each row counts a real collection once: cash whose authoritative record is still a legacy source before its cutover, plus the posted Collection after it. A converted source row's legacy fields are never added beside its Collection.",
            "Remittance is not income: a remittance records money already collected and never changes this statement.",
            "Historical weighing without frozen rate evidence remains unresolved; current rates never re-price history.",
            "Targets are office-approved annual amounts. Attainment uses official report actuals, not collection efficiency. Report adjustments never alter collection ledgers.",
        };
        var allRows = groups.SelectMany(g => g.Rows).ToArray();
        var targeted = allRows.Where(r => r.AnnualTarget.HasValue).ToArray();
        var coverage = targeted.Length == 0 ? TargetCoverageState.None : targeted.Length == allRows.Length ? TargetCoverageState.Complete : TargetCoverageState.Partial;
        var targetTotal = targeted.Sum(r => r.AnnualTarget ?? 0m);
        var through = request.Month ?? (request.Year < clock.PhilippineToday.Year ? 12 : request.Year == clock.PhilippineToday.Year ? clock.PhilippineToday.Month : 0);
        var coveredActual = targeted.Sum(r => r.Months.Take(through).Sum(c => c.Total));
        var sections = new List<OfficialMonthlyIncomeSectionDto>();
        foreach (var (key, label, keys) in new[] {
            ("A", "Income From Market", new[] { OfficialMonthlyIncomeStructure.Market, OfficialMonthlyIncomeStructure.Rent, OfficialMonthlyIncomeStructure.Space }),
            ("B", "Income From Terminal", new[] { OfficialMonthlyIncomeStructure.Terminal }),
            ("C", "Income from Slaughterhouse", new[] { OfficialMonthlyIncomeStructure.Slaughterhouse }) })
        {
            var members = groups.Where(g => keys.Contains(g.Key)).ToArray();
            sections.Add(new(key, label, keys, SumMonths(members.Select(g => g.MonthTotals)), SumCell(members.Select(g => g.Total))));
        }
        var configured = await db.Municipalities.AsNoTracking().Where(x => x.Id == tenantId).Select(x => x.ReportSignatories).SingleOrDefaultAsync(ct);
        IReadOnlyList<EEMOCantilanSDS.Application.Command.Municipalities.SetReportSignatories.ReportSignatoryDto> signatories = [];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(configured);
                var lines = json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array ? json.RootElement : json.RootElement.GetProperty("Lines");
                signatories = System.Text.Json.JsonSerializer.Deserialize<List<EEMOCantilanSDS.Application.Command.Municipalities.SetReportSignatories.ReportSignatoryDto>>(lines.GetRawText()) ?? [];
            }
            catch (System.Text.Json.JsonException) { notes.Add("Report signatories need office configuration."); }
            catch (KeyNotFoundException) { notes.Add("Report signatories need office configuration."); }
            catch (InvalidOperationException) { notes.Add("Report signatories need office configuration."); }
        }
        return Result<OfficialMonthlyIncomeDto>.Success(new OfficialMonthlyIncomeDto(
            request.Year, request.Month, groups, monthTotals, SumCell(monthTotals), targeted.Length > 0, notes, clock.UtcNow,
            new(coverage, targeted.Length, allRows.Length, targetTotal, coveredActual,
                coverage == TargetCoverageState.Complete && targetTotal > 0m ? coveredActual / targetTotal * 100m : null), sections, signatories));
    }

    private static OfficialMonthlyIncomeRowDto ToRow(OfficialMonthlyIncomeStructure.Row row, List<Fact> facts,
        Dictionary<(OfficialReportRevisionKind Kind, string RowKey, int Month), OfficialReportRevision> revisions, int through)
    {
        var months = Enumerable.Range(1, 12).Select(m => new MonthlyIncomeCellDto(
            facts.Where(f => f.Month == m && !f.Canonical).Sum(f => f.Amount),
            facts.Where(f => f.Month == m && f.Canonical).Sum(f => f.Amount),
            revisions.GetValueOrDefault((OfficialReportRevisionKind.MonthlyAdjustment, row.Key, m))?.Amount ?? 0m,
            revisions.TryGetValue((OfficialReportRevisionKind.MonthlyAdjustment, row.Key, m), out var adjustment)
                ? ReportGovernanceWorkflow.ToDto(adjustment) : null)).ToList();
        var total = SumCell(months);
        var authority = total.Legacy != 0m && total.Canonical != 0m ? "Mixed"
            : total.Canonical != 0m ? "Canonical" : total.Legacy != 0m ? "Legacy" : "None";
        var target = revisions.GetValueOrDefault((OfficialReportRevisionKind.AnnualTarget, row.Key, 0))?.Amount;
        return new(row.Key, row.Label, row.ClassificationCode, months, total, authority, target,
            target is > 0m ? months.Take(through).Sum(c => c.Total) / target.Value * 100m : null);
    }

    private static MonthlyIncomeCellDto SumCell(IEnumerable<MonthlyIncomeCellDto> cells)
    {
        var list = cells.ToList();
        return new(list.Sum(x => x.Legacy), list.Sum(x => x.Canonical), list.Sum(x => x.AdjustmentAmount));
    }

    private static IReadOnlyList<MonthlyIncomeCellDto> SumMonths(IEnumerable<IReadOnlyList<MonthlyIncomeCellDto>> rows)
    {
        var list = rows.ToList();
        return Enumerable.Range(0, 12).Select(i => new MonthlyIncomeCellDto(list.Sum(r => r[i].Legacy), list.Sum(r => r[i].Canonical), list.Sum(r => r[i].AdjustmentAmount))).ToList();
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
            select new { line.Id, line.RevenueClassificationId, collection.BusinessDate, line.Amount, line.SourceKind, line.SourceId, line.SourcePart }).ToListAsync(ct);

        // A canonical line counts only when the authority map says the canonical representation of its source row is the
        // authoritative money (IA-050). A line against a source row whose legacy money still counts is shadow evidence:
        // the legacy reader already reports that row, so counting the line too would report one collection twice. The
        // test is the line's source identity, never its amount, payor or date, so two genuine collections are never merged.
        var counted = await AuthoritativeLineIdsAsync(tenantId, lines.Select(x => (x.Id, x.SourceKind, x.SourceId, x.SourcePart)).ToList(), ct);
        lines = lines.Where(x => counted.Contains(x.Id)).ToList();
        var lineCorrections = await (
            from correctionLine in db.CollectionCorrectionLines.AsNoTracking()
            join line in db.CollectionLines.AsNoTracking() on new { correctionLine.MunicipalityId, Id = correctionLine.OriginalCollectionLineId } equals new { line.MunicipalityId, line.Id }
            join collection in db.Collections.AsNoTracking() on new { line.MunicipalityId, Id = line.CollectionId } equals new { collection.MunicipalityId, collection.Id }
            where correctionLine.MunicipalityId == tenantId && collection.BusinessDate >= first && collection.BusinessDate <= last
            select new { line.Id, line.RevenueClassificationId, collection.BusinessDate, correctionLine.FinancialEffectAmount }).ToListAsync(ct);
        lineCorrections = lineCorrections.Where(x => counted.Contains(x.Id)).ToList();

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
                    && (allocation.SourceKind == CollectionSourceKind.PaymentRecord || allocation.SourceKind == CollectionSourceKind.DailyCollection)
                select new { allocation.Id, allocation.SourceId, collection.BusinessDate, allocation.Amount, line.RevenueClassificationId }).ToListAsync(ct);
            var rentAllocations = allocations.Where(x => rentIds.Contains(x.RevenueClassificationId)).ToList();
            var recordIds = rentAllocations.Select(x => x.SourceId).Distinct().ToList();
            var facilityOf = await (
                from record in db.PaymentRecords.AsNoTracking()
                join stall in db.Stalls.AsNoTracking() on record.StallId equals stall.Id
                join facility in db.Facilities.AsNoTracking() on stall.FacilityId equals facility.Id
                where record.MunicipalityId == tenantId && recordIds.Contains(record.Id)
                select new { record.Id, facility.Code }).ToDictionaryAsync(x => x.Id, x => x.Code, ct);
            // A canonical NPM daily stall fee is stall rent too: its day row names the stall, hence the facility's row.
            foreach (var day in await (
                from daily in db.DailyCollections.AsNoTracking()
                join stall in db.Stalls.AsNoTracking() on daily.StallId equals stall.Id
                join facility in db.Facilities.AsNoTracking() on stall.FacilityId equals facility.Id
                where daily.MunicipalityId == tenantId && recordIds.Contains(daily.Id)
                select new { daily.Id, facility.Code }).ToListAsync(ct))
                facilityOf[day.Id] = day.Code;
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

    /// <summary>
    /// The ids of the lines whose canonical money is authoritative. Same rule as the Collection Activity feed: the line's own
    /// source and every allocation's source must be one whose canonical representation counts. Only stall-rent and utility
    /// source rows can still be legacy-authoritative; a line against any other source, or one whose source row cannot be read
    /// (still posted money), is counted.
    /// </summary>
    private async Task<HashSet<Guid>> AuthoritativeLineIdsAsync(Guid tenantId,
        IReadOnlyList<(Guid Id, CollectionSourceKind? Kind, Guid? SourceId, CollectionSourcePart? Part)> lines, CancellationToken ct)
    {
        var lineIds = lines.Select(x => x.Id).ToList();
        var allocations = lineIds.Count == 0 ? [] : await db.CollectionAllocations.AsNoTracking()
            .Where(a => a.MunicipalityId == tenantId && lineIds.Contains(a.CollectionLineId)
                && (a.SourceKind == CollectionSourceKind.PaymentRecord || a.SourceKind == CollectionSourceKind.UtilityBill))
            .Select(a => new { a.CollectionLineId, a.SourceKind, a.SourceId, a.SourcePart }).ToListAsync(ct);

        var recordIds = lines.Where(l => l.Kind == CollectionSourceKind.PaymentRecord && l.SourceId.HasValue).Select(l => l.SourceId!.Value)
            .Concat(allocations.Where(a => a.SourceKind == CollectionSourceKind.PaymentRecord).Select(a => a.SourceId)).Distinct().ToList();
        var billIds = lines.Where(l => l.Kind == CollectionSourceKind.UtilityBill && l.SourceId.HasValue).Select(l => l.SourceId!.Value)
            .Concat(allocations.Where(a => a.SourceKind == CollectionSourceKind.UtilityBill).Select(a => a.SourceId)).Distinct().ToList();
        var records = await db.PaymentRecords.AsNoTracking()
            .Where(p => p.MunicipalityId == tenantId && recordIds.Contains(p.Id))
            .Select(p => new { p.Id, p.SettlementAuthorityState }).ToDictionaryAsync(p => p.Id, p => p.SettlementAuthorityState, ct);
        var bills = await db.UtilityBills.AsNoTracking()
            .Where(b => b.MunicipalityId == tenantId && billIds.Contains(b.Id))
            .Select(b => new { b.Id, b.ElectricitySettlementAuthorityState, b.WaterSettlementAuthorityState }).ToDictionaryAsync(b => b.Id, ct);

        bool Counts(CollectionSourceKind? kind, Guid? sourceId, CollectionSourcePart? part)
        {
            if (kind is not { } k) return true;
            var authority = k switch
            {
                CollectionSourceKind.PaymentRecord => sourceId is { } r && records.TryGetValue(r, out var state) ? state : SettlementAuthority.Canonical,
                CollectionSourceKind.UtilityBill => sourceId is { } b && bills.TryGetValue(b, out var bill)
                    ? part == CollectionSourcePart.Water ? bill.WaterSettlementAuthorityState : bill.ElectricitySettlementAuthorityState
                    : SettlementAuthority.Canonical,
                _ => SettlementAuthority.Canonical
            };
            return CollectionSourceAuthorityMap.CanonicalMoneyCounts(k, authority);
        }

        var allocationsByLine = allocations.ToLookup(a => a.CollectionLineId);
        return lines.Where(l => Counts(l.Kind, l.SourceId, l.Part)
                && allocationsByLine[l.Id].All(a => Counts(a.SourceKind, a.SourceId, a.SourcePart)))
            .Select(l => l.Id).ToHashSet();
    }

    private static string CodeOf(Dictionary<Guid, string> codes, Guid id) => codes.GetValueOrDefault(id, "UNKNOWN_CLASSIFICATION");
}
