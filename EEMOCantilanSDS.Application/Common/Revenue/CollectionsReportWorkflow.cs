using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// The Collections register, the RCD-style summary and the serial trace (IA-052). Everything is read from posted canonical
/// Collections: the summary is derived from the same rows the register lists, so no total is typed and none is rebuilt in
/// the browser. Legacy-authoritative sources are read by their own legacy reports until they go canonical; this register
/// never mixes them in. Read-only: it writes nothing.
/// </summary>
public sealed class CollectionsReportWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality)
{
    private const int RowLimit = 2000;

    public Task<Result<CollectionsRegisterDto>> GetRegisterAsync(
        DateOnly from, DateOnly to, Guid? collectorId, RevenueInstrumentType? instrument, Guid? classificationId,
        CancellationToken ct = default) =>
        Run<CollectionsRegisterDto>(async tenantId =>
        {
            if (from > to || to.DayNumber - from.DayNumber > 366)
                return Result<CollectionsRegisterDto>.Failure("Choose a valid period of no more than 367 days.", ResultStatus.Invalid);
            var rows = (await LoadRowsAsync(tenantId, from, to, collectorId, instrument, classificationId, ct)).Select(x => x.Row).ToList();
            var summary = rows.GroupBy(x => x.ClassificationId).Select(g => new CollectionsSummaryLineDto(
                g.Key, g.First().ClassificationName, g.Select(x => x.CollectionId).Distinct().Count(),
                g.Sum(x => x.Amount), g.Sum(x => x.CorrectionEffect), g.Sum(x => x.Amount + x.CorrectionEffect),
                g.Select(x => x.ReferenceCode).OrderBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal).FirstOrDefault(),
                g.Select(x => x.ReferenceCode).OrderBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal).LastOrDefault()))
                .OrderBy(x => x.ClassificationName, StringComparer.OrdinalIgnoreCase).ToList();
            var ordered = rows.OrderByDescending(x => x.BusinessDate).ThenBy(x => x.ReferenceCode.Length).ThenBy(x => x.ReferenceCode, StringComparer.Ordinal).ToList();
            return Result<CollectionsRegisterDto>.Success(new CollectionsRegisterDto(from, to,
                ordered.Take(RowLimit).ToList(), summary, rows.Sum(x => x.Amount), rows.Sum(x => x.CorrectionEffect),
                rows.Sum(x => x.Amount + x.CorrectionEffect), ordered.Count > RowLimit));
        }, ct);

    /// <summary>
    /// The signed-in collector's own canonical collections for a period, read-only. The collector comes from the token, never
    /// from a request value, so a collector cannot read another collector's or the office's register. Legacy-authoritative
    /// sources are not included: they keep their own Records feed until they go canonical.
    /// </summary>
    public async Task<Result<CollectionsRegisterDto>> GetMyRegisterAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collectorId
            || collectorId == Guid.Empty)
            return Result<CollectionsRegisterDto>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<CollectionsRegisterDto>.Forbidden();
        if (from > to || to.DayNumber - from.DayNumber > 366)
            return Result<CollectionsRegisterDto>.Failure("Choose a valid period of no more than 367 days.", ResultStatus.Invalid);
        var rows = (await LoadRowsAsync(tenantId, from, to, collectorId, null, null, ct)).Select(x => x.Row)
            .OrderByDescending(x => x.BusinessDate).ThenBy(x => x.ReferenceCode.Length).ThenBy(x => x.ReferenceCode, StringComparer.Ordinal).ToList();
        return Result<CollectionsRegisterDto>.Success(new CollectionsRegisterDto(from, to, rows.Take(RowLimit).ToList(), [],
            rows.Sum(x => x.Amount), rows.Sum(x => x.CorrectionEffect), rows.Sum(x => x.Amount + x.CorrectionEffect), rows.Count > RowLimit));
    }

    public Task<Result<CollectionDocumentDto>> GetCollectionAsync(Guid collectionId, CancellationToken ct = default) =>
        Run<CollectionDocumentDto>(async tenantId =>
        {
            var document = await DocumentAsync(tenantId, collectionId, ct);
            return document is null ? Result<CollectionDocumentDto>.NotFound() : Result<CollectionDocumentDto>.Success(document);
        }, ct);

    /// <summary>Traces a document number to its custody, collection, revenue lines, amount and remittance coverage.</summary>
    public Task<Result<DocumentTraceDto>> TraceAsync(string documentNumber, CancellationToken ct = default) =>
        Run<DocumentTraceDto>(async tenantId =>
        {
            var number = (documentNumber ?? string.Empty).Trim();
            if (number.Length is 0 or > 50) return Result<DocumentTraceDto>.Failure("Enter a document number.", ResultStatus.Invalid);
            var document = await db.AccountableDocuments.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == tenantId && x.DocumentNumber == number, ct);
            if (document is null) return Result<DocumentTraceDto>.NotFound();
            var latestAssignee = await db.AccountableFormAssignments.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && x.AccountableDocumentId == document.Id)
                .OrderByDescending(x => x.AssignedAtUtc).Select(x => (Guid?)x.AssignedUserId).FirstOrDefaultAsync(ct);
            var custodian = latestAssignee is { } id
                ? await db.CollectorUsers.AsNoTracking().Where(x => x.MunicipalityId == tenantId && x.Id == id)
                    .Select(x => x.FullName).FirstOrDefaultAsync(ct)
                : document.State == AccountableDocumentState.InOffice ? "Office" : null;
            var spoiled = await db.AccountableFormSpoilages.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && x.AccountableDocumentId == document.Id)
                .Select(x => x.Reason).FirstOrDefaultAsync(ct);
            var collection = document.CollectionId is { } collectionId ? await DocumentAsync(tenantId, collectionId, ct) : null;
            return Result<DocumentTraceDto>.Success(new DocumentTraceDto(document.DocumentNumber, document.InstrumentType,
                StateLabel(document.State), custodian, spoiled, collection));
        }, ct);

    private async Task<List<(Guid LineId, CollectionRegisterRowDto Row)>> LoadRowsAsync(
        Guid tenantId, DateOnly from, DateOnly to, Guid? collectorId, RevenueInstrumentType? instrument, Guid? classificationId,
        CancellationToken ct)
    {
        var collections = db.Collections.AsNoTracking().Where(x => x.MunicipalityId == tenantId && x.BusinessDate >= from && x.BusinessDate <= to);
        if (collectorId is { } c) collections = collections.Where(x => x.CollectorId == c);
        var list = await collections.Select(x => new { x.Id, x.BusinessDate, x.PayerName, x.CollectorId, x.ReferenceCode }).ToListAsync(ct);
        if (list.Count == 0) return [];
        var ids = list.Select(x => x.Id).ToArray();
        var lines = await db.CollectionLines.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && ids.Contains(x.CollectionId))
            .Select(x => new { x.Id, x.CollectionId, x.RevenueClassificationId, x.RevenueClassificationPolicyId, x.Amount, x.SourceKind, x.SourceId, x.CalculationSnapshot })
            .ToListAsync(ct);
        var lineIds = lines.Select(x => x.Id).ToArray();
        var documents = (await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.CollectionId != null && ids.Contains(x.CollectionId!.Value))
            .Select(x => new { CollectionId = x.CollectionId!.Value, x.DocumentNumber, x.InstrumentType }).ToListAsync(ct))
            .GroupBy(x => x.CollectionId).ToDictionary(g => g.Key, g => g.First());
        var policyIds = lines.Select(x => x.RevenueClassificationPolicyId).Distinct().ToArray();
        var policyInstruments = await db.RevenueClassificationPolicies.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && policyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.PermittedInstrumentType, ct);
        var effects = (await db.CollectionCorrectionLines.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && lineIds.Contains(x.OriginalCollectionLineId))
            .Select(x => new { x.OriginalCollectionLineId, x.FinancialEffectAmount }).ToListAsync(ct))
            .GroupBy(x => x.OriginalCollectionLineId).ToDictionary(g => g.Key, g => g.Sum(x => x.FinancialEffectAmount));
        var names = await ClassificationNamesAsync(tenantId, lines.Select(x => x.RevenueClassificationId).Distinct().ToArray(), to, ct);
        var collectors = await CollectorNamesAsync(tenantId, list.Where(x => x.CollectorId != null).Select(x => x.CollectorId!.Value).Distinct().ToArray(), ct);
        var labels = await SourceLabelsAsync(tenantId, lines.Select(x => (x.Id, x.SourceKind, x.SourceId, (string?)x.CalculationSnapshot)).ToList(), ct);

        var rows = new List<(Guid LineId, CollectionRegisterRowDto Row)>();
        foreach (var line in lines)
        {
            if (classificationId is { } wanted && line.RevenueClassificationId != wanted) continue;
            var collection = list.Single(x => x.Id == line.CollectionId);
            documents.TryGetValue(collection.Id, out var document);
            var rowInstrument = document?.InstrumentType ?? policyInstruments.GetValueOrDefault(line.RevenueClassificationPolicyId);
            if (instrument is { } i && rowInstrument != i) continue;
            var effect = effects.GetValueOrDefault(line.Id);
            rows.Add((line.Id, new CollectionRegisterRowDto(collection.Id, collection.BusinessDate, collection.ReferenceCode, document?.DocumentNumber, rowInstrument,
                collection.PayerName, collection.CollectorId, collection.CollectorId is { } cid ? collectors.GetValueOrDefault(cid) : null,
                line.RevenueClassificationId, names.GetValueOrDefault(line.RevenueClassificationId, "Unclassified"),
                labels.GetValueOrDefault(line.Id, "—"), line.Amount, effect,
                effect == 0m ? "Posted" : line.Amount + effect <= 0m ? "Reversed" : "Corrected")));
        }
        return rows;
    }

    private async Task<CollectionDocumentDto?> DocumentAsync(Guid tenantId, Guid collectionId, CancellationToken ct)
    {
        var collection = await db.Collections.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == collectionId, ct);
        if (collection is null) return null;
        var rows = (await LoadRowsAsync(tenantId, collection.BusinessDate, collection.BusinessDate, collection.CollectorId, null, null, ct))
            .Where(x => x.Row.CollectionId == collectionId).ToList();
        var document = await db.AccountableDocuments.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.CollectionId == collectionId, ct);
        var coverage = await (
            from cover in db.CollectionRemittanceCoverages.AsNoTracking()
            join remittance in db.CollectionRemittances.AsNoTracking()
                on new { cover.MunicipalityId, Id = cover.RemittanceId } equals new { remittance.MunicipalityId, remittance.Id }
            where cover.MunicipalityId == tenantId && cover.CollectionId == collectionId && cover.IsActive
            select new { remittance.Id, remittance.Status }).FirstOrDefaultAsync(ct);

        // Audit detail: the allocations behind each line. The primary document view stays a clean itemized list.
        var lineIds = rows.Select(x => x.LineId).ToArray();
        var allocations = await db.CollectionAllocations.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && lineIds.Contains(x.CollectionLineId))
            .Select(x => new { x.CollectionLineId, x.SourceKind, x.SourceId, x.Amount }).ToListAsync(ct);
        var pseudo = allocations.Select(a => (Guid.NewGuid(), (CollectionSourceKind?)a.SourceKind, (Guid?)a.SourceId, (string?)null)).ToList();
        var pseudoLabels = await SourceLabelsAsync(tenantId, pseudo, ct);
        var lines = rows.Select(row =>
        {
            var details = allocations.Select((a, i) => (a, label: pseudoLabels[pseudo[i].Item1]))
                .Where(x => x.a.CollectionLineId == row.LineId)
                .Select(x => $"{x.label}: ₱{x.a.Amount.ToString("N2", CultureInfo.InvariantCulture)}").ToList();
            return new CollectionDocumentLineDto(row.Row.ClassificationName, row.Row.SourceLabel, row.Row.Amount, row.Row.CorrectionEffect, details);
        }).ToList();
        return new CollectionDocumentDto(collection.Id, collection.ReferenceCode, document?.DocumentNumber, document?.InstrumentType ?? rows.Select(x => x.Row.Instrument).FirstOrDefault(x => x is not null),
            document is null ? null : StateLabel(document.State), collection.BusinessDate, collection.RecordedAtUtc,
            collection.PayerName, (rows.Count > 0 ? rows[0].Row.CollectorName : null), collection.TotalAmount,
            rows.Sum(x => x.Row.CorrectionEffect), lines, coverage?.Id, coverage?.Status.ToString());
    }

    private async Task<Dictionary<Guid, string>> SourceLabelsAsync(
        Guid tenantId, List<(Guid LineId, CollectionSourceKind? Kind, Guid? SourceId, string? Snapshot)> lines, CancellationToken ct)
    {
        var labels = new Dictionary<Guid, string>();
        var lineIds = lines.Select(x => x.LineId).ToArray();
        // Lines with no anchor (stall rent) carry their sources on allocations.
        var allocations = await db.CollectionAllocations.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && lineIds.Contains(x.CollectionLineId))
            .Select(x => new { x.CollectionLineId, x.SourceKind, x.SourceId }).ToListAsync(ct);
        var services = await db.GovernedServices.AsNoTracking().Where(x => x.MunicipalityId == tenantId)
            .ToDictionaryAsync(x => x.Id, x => x.OperationCode, ct);
        var penalties = await db.PenaltyDefinitions.AsNoTracking().Where(x => x.MunicipalityId == tenantId)
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var periods = await (
            from period in db.ObligationPeriods.AsNoTracking()
            join account in db.ObligationAccounts.AsNoTracking()
                on new { period.MunicipalityId, Id = period.ObligationAccountId } equals new { account.MunicipalityId, account.Id }
            where period.MunicipalityId == tenantId
            select new { period.Id, account.Kind, account.SubjectLabel, period.PeriodStart }).ToDictionaryAsync(x => x.Id, ct);
        var records = await (
            from record in db.PaymentRecords.AsNoTracking()
            join stall in db.Stalls.AsNoTracking() on record.StallId equals stall.Id
            join facility in db.Facilities.AsNoTracking() on stall.FacilityId equals facility.Id
            where record.MunicipalityId == tenantId
            select new { record.Id, facility.Code, stall.StallNo, record.BillingYear, record.BillingMonth }).ToDictionaryAsync(x => x.Id, ct);
        var bills = await (
            from bill in db.UtilityBills.AsNoTracking()
            join stall in db.Stalls.AsNoTracking() on bill.StallId equals stall.Id
            where bill.MunicipalityId == tenantId
            select new { bill.Id, stall.StallNo, bill.BillingYear, bill.BillingMonth }).ToDictionaryAsync(x => x.Id, ct);

        string Label(CollectionSourceKind kind, Guid id) => kind switch
        {
            CollectionSourceKind.NpmWeighing => "Weight & Measure",
            CollectionSourceKind.FishMeatVendorFee => "Fish / Meat Vendor Fee",
            CollectionSourceKind.GovernedService => GovernedServiceCatalog.Find(services.GetValueOrDefault(id))?.Name ?? "Operation",
            CollectionSourceKind.PenaltyDefinition => $"Penalty · {penalties.GetValueOrDefault(id, "approved penalty")}",
            CollectionSourceKind.ObligationPeriod when periods.TryGetValue(id, out var p) =>
                $"{ObligationCollectionSource.KindLabel(p.Kind)} · {p.SubjectLabel} {p.PeriodStart.ToString("MMM yyyy", CultureInfo.InvariantCulture)}",
            CollectionSourceKind.PaymentRecord when records.TryGetValue(id, out var r) =>
                $"{r.Code} · Stall {r.StallNo} {new DateOnly(r.BillingYear, r.BillingMonth, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture)}",
            CollectionSourceKind.UtilityBill when bills.TryGetValue(id, out var b) =>
                $"Utility · Stall {b.StallNo} {new DateOnly(b.BillingYear, b.BillingMonth, 1).ToString("MMM yyyy", CultureInfo.InvariantCulture)}",
            _ => kind.ToString()
        };

        foreach (var line in lines)
        {
            if (line.Kind is { } kind && line.SourceId is { } sourceId) { labels[line.LineId] = Label(kind, sourceId); continue; }
            var mine = allocations.Where(x => x.CollectionLineId == line.LineId).Select(x => Label(x.SourceKind, x.SourceId)).Distinct().ToList();
            labels[line.LineId] = mine.Count switch { 0 => "—", 1 => mine[0], _ => $"{mine[0]} and {mine.Count - 1} more" };
        }
        return labels;
    }

    private async Task<Dictionary<Guid, string>> ClassificationNamesAsync(Guid tenantId, Guid[] ids, DateOnly asOf, CancellationToken ct)
    {
        if (ids.Length == 0) return [];
        var policies = await db.RevenueClassificationPolicies.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && ids.Contains(x.RevenueClassificationId)
                && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= asOf.AddYears(1))
            .Select(x => new { x.RevenueClassificationId, x.EffectiveDate, x.DisplayName }).ToListAsync(ct);
        var codes = await db.RevenueClassifications.AsNoTracking().Where(x => x.MunicipalityId == tenantId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.SemanticCode, ct);
        return ids.ToDictionary(id => id, id => policies.Where(p => p.RevenueClassificationId == id)
            .OrderByDescending(p => p.EffectiveDate).Select(p => p.DisplayName).FirstOrDefault() ?? codes.GetValueOrDefault(id, "Unclassified"));
    }

    private async Task<Dictionary<Guid, string>> CollectorNamesAsync(Guid tenantId, Guid[] ids, CancellationToken ct) =>
        ids.Length == 0 ? [] : await db.CollectorUsers.AsNoTracking().Where(x => x.MunicipalityId == tenantId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName ?? x.Username ?? "Collector", ct);

    private static string StateLabel(AccountableDocumentState state) => state switch
    {
        AccountableDocumentState.InOffice => "In office",
        AccountableDocumentState.Assigned => "Assigned",
        AccountableDocumentState.Consumed => "Issued / consumed",
        AccountableDocumentState.ReconciliationRequired => "Needs review",
        AccountableDocumentState.Voided => "Spoiled / cancelled",
        AccountableDocumentState.Lost => "Lost",
        _ => state.ToString()
    };

    private async Task<Result<T>> Run<T>(Func<Guid, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty) return Result<T>.Unauthorized();
        if (currentUser.Role is not ("Admin" or "SuperAdmin")) return Result<T>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId) return Result<T>.Forbidden();
        return await action(tenantId);
    }
}
