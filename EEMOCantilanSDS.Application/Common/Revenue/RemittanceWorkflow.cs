using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Remittance and liquidation (IA-052): recording money already collected and turned over, against whole posted
/// Collections, exactly once. It derives the expected amount from those Collections, never asks staff to retype a
/// collection total, and never writes a Collection, a line or any figure a revenue report reads. It invents no Treasury
/// workflow: the Head and Administrators record and void, and a reference is optional evidence.
/// </summary>
public sealed class RemittanceWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    ICollectorReportQueries? legacyQueries = null,
    IClock? clock = null) : ICollectorCollectionFacts
{
    private const string Origin = "WebRemittance";
    private const int IntentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DateOnly BusinessToday => clock?.PhilippineToday ?? PhilippineTime.Today;
    private DateTime UtcNow => clock?.UtcNow ?? DateTime.UtcNow;

    // ── What can be remitted ───────────────────────────────────────────────────────────────────────────

    public Task<Result<RemittanceScopeDto>> GetScopeAsync(
        Guid collectorId, DateOnly from, DateOnly to, RevenueInstrumentType? instrument, CancellationToken ct = default) =>
        Run<RemittanceScopeDto>(async actor =>
        {
            if (Validate(from, to) is { } problem) return Result<RemittanceScopeDto>.Failure(problem, ResultStatus.Invalid);
            if (!await CollectorExistsAsync(actor.TenantId, collectorId, ct)) return Result<RemittanceScopeDto>.NotFound();
            var eligible = await EligibleAsync(actor.TenantId, collectorId, from, to, instrument, ct);
            return Result<RemittanceScopeDto>.Success(BuildScope(collectorId, from, to, instrument, eligible));
        }, ct);

    // ── Record / void ──────────────────────────────────────────────────────────────────────────────────

    public Task<Result<RemittanceDetailDto>> RecordAsync(RecordRemittanceRequest request, CancellationToken ct = default) =>
        Run<RemittanceDetailDto>(async actor =>
        {
            var prepared = await PrepareAsync(actor, request, ct);
            if (prepared.Failure is { } failure) return failure;
            if (prepared.PriorId is { } priorId) return await DetailAsync(actor.TenantId, priorId, ct);
            var remittance = prepared.Remittance!;
            var fingerprint = remittance.IntentFingerprint;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                // Either a concurrent retry with this identity won, or another remittance took one of these collections.
                var winner = await db.CollectionRemittances.AsNoTracking().SingleOrDefaultAsync(x =>
                    x.MunicipalityId == actor.TenantId && x.ClientOperationId == request.ClientOperationId, ct);
                if (winner is not null && winner.IntentFingerprint == fingerprint)
                    return await DetailAsync(actor.TenantId, winner.Id, ct);
                return Result<RemittanceDetailDto>.Failure(
                    "A collection was remitted by another record while this one was being saved. Reload and review again.", ResultStatus.Conflict);
            }
            return await DetailAsync(actor.TenantId, remittance.Id, ct);
        }, ct);

    /// <summary>
    /// Records one independent remittance per collector in a single save: each keeps its own collector, collections,
    /// amount, reference and ClientOperationId, and each collection is still covered at most once. Either every new record
    /// is saved or none is; a retry with the same operation identities returns the records already saved.
    /// </summary>
    public Task<Result<IReadOnlyList<RemittanceDetailDto>>> RecordBatchAsync(RecordRemittanceBatchRequest request, CancellationToken ct = default) =>
        Run<IReadOnlyList<RemittanceDetailDto>>(async actor =>
        {
            var items = request.Remittances ?? [];
            if (items.Count == 0 || items.Count > MaximumRemittancesPerBatch)
                return Result<IReadOnlyList<RemittanceDetailDto>>.Failure(
                    $"Record between 1 and {MaximumRemittancesPerBatch} collectors' remittances at once.", ResultStatus.Invalid);
            if (items.Select(x => x.CollectorId).Distinct().Count() != items.Count
                || items.Select(x => x.ClientOperationId).Distinct().Count() != items.Count)
                return Result<IReadOnlyList<RemittanceDetailDto>>.Failure(
                    "Each collector appears once in a batch, each with its own operation identity.", ResultStatus.Invalid);
            var ids = new List<Guid>(items.Count);
            foreach (var item in items)
            {
                var prepared = await PrepareAsync(actor, item, ct);
                if (prepared.Failure is { } failure)
                {
                    db.ChangeTracker.Clear();
                    var name = (await CollectorNamesAsync(actor.TenantId, [item.CollectorId], ct)).GetValueOrDefault(item.CollectorId, "A collector");
                    return Result<IReadOnlyList<RemittanceDetailDto>>.Failure(
                        $"{name}: {failure.Error} Nothing was recorded.", failure.Status);
                }
                ids.Add(prepared.PriorId ?? prepared.Remittance!.Id);
            }
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                return Result<IReadOnlyList<RemittanceDetailDto>>.Failure(
                    "A collection was remitted by another record while these were being saved. Nothing was recorded; reload and review again.",
                    ResultStatus.Conflict);
            }
            var details = new List<RemittanceDetailDto>(ids.Count);
            foreach (var id in ids)
            {
                var detail = await DetailAsync(actor.TenantId, id, ct);
                if (!detail.IsSuccess) return Result<IReadOnlyList<RemittanceDetailDto>>.Failure(detail.Error ?? "A remittance could not be read back.", detail.Status);
                details.Add(detail.Value!);
            }
            return Result<IReadOnlyList<RemittanceDetailDto>>.Success(details);
        }, ct);

    private const int MaximumRemittancesPerBatch = 50;

    /// <summary>The outcome of checking one remittance request: a failure, an identical earlier record, or a new tracked record.</summary>
    private sealed record PreparedRemittance(Result<RemittanceDetailDto>? Failure, Guid? PriorId, CollectionRemittance? Remittance);

    /// <summary>
    /// Validates one request against posted collections and adds the new remittance and its coverage to the context
    /// without saving. A request already recorded with the same intent resolves to that record (idempotent retry).
    /// </summary>
    private async Task<PreparedRemittance> PrepareAsync(Actor actor, RecordRemittanceRequest request, CancellationToken ct)
    {
        static PreparedRemittance Fail(string error, ResultStatus status) =>
            new(Result<RemittanceDetailDto>.Failure(error, status), null, null);

        if (request.ClientOperationId == Guid.Empty)
            return Fail("A valid ClientOperationId is required.", ResultStatus.Invalid);
        if (Validate(request.From, request.To) is { } problem)
            return Fail(problem, ResultStatus.Invalid);
        if (request.RemittanceDate > BusinessToday)
            return Fail("A remittance cannot be dated in the future.", ResultStatus.Invalid);
        if (request.Instrument is { } i && i is not (RevenueInstrumentType.OfficialReceipt or RevenueInstrumentType.CashTicket))
            return Fail("Choose an Official Receipt or Cash Ticket scope.", ResultStatus.Invalid);
        if (!await CollectorExistsAsync(actor.TenantId, request.CollectorId, ct))
            return new(Result<RemittanceDetailDto>.NotFound(), null, null);

        var fingerprint = Fingerprint(actor, request);
        var prior = await db.CollectionRemittances.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.TenantId && x.ClientOperationId == request.ClientOperationId, ct);
        if (prior is not null)
            return prior.IntentFingerprint == fingerprint
                ? new(null, prior.Id, null)
                : Fail("IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to a different remittance.", ResultStatus.Conflict);

        var eligible = await EligibleAsync(actor.TenantId, request.CollectorId, request.From, request.To, request.Instrument, ct);
        IReadOnlyList<RemittanceCollectionDto> chosen;
        if (request.CollectionIds is { Count: > 0 } ids)
        {
            var wanted = ids.Distinct().ToHashSet();
            chosen = eligible.Where(x => wanted.Contains(x.CollectionId)).ToList();
            if (chosen.Count != wanted.Count)
                return Fail(
                    "A selected collection is not available to remit: it is not this collector's, is outside the period or instrument, has no money left after corrections, or is already covered by a remittance.",
                    ResultStatus.Conflict);
        }
        else chosen = eligible;
        if (chosen.Count == 0)
            return Fail("There is no unremitted collection in this scope.", ResultStatus.Conflict);

        var expected = chosen.Sum(x => x.NetAmount);
        if (request.AmountRemitted <= 0m || decimal.Round(request.AmountRemitted, 2) != request.AmountRemitted)
            return Fail("Enter the remitted amount in whole centavos.", ResultStatus.Invalid);
        if (request.AmountRemitted > expected)
            return Fail($"The remitted amount cannot exceed what was collected (₱{expected:N2}).", ResultStatus.Invalid);

        CollectionRemittance remittance;
        try
        {
            remittance = CollectionRemittance.Record(actor.TenantId, request.CollectorId, request.RemittanceDate,
                request.From, request.To, request.Instrument, expected, request.AmountRemitted, chosen.Count,
                request.Reference, request.Remarks, request.ClientOperationId, fingerprint, actor.Username,
                actor.ActorId, UtcNow);
        }
        catch (ArgumentException ex)
        {
            return Fail(ex.Message, ResultStatus.Invalid);
        }
        db.CollectionRemittances.Add(remittance);
        foreach (var collection in chosen)
            db.CollectionRemittanceCoverages.Add(
                CollectionRemittanceCoverage.Cover(actor.TenantId, remittance.Id, collection.CollectionId, collection.NetAmount));
        return new(null, null, remittance);
    }

    public Task<Result<RemittanceDetailDto>> VoidAsync(Guid id, VoidRemittanceRequest request, CancellationToken ct = default) =>
        Run<RemittanceDetailDto>(async actor =>
        {
            var remittance = await db.CollectionRemittances.SingleOrDefaultAsync(x =>
                x.MunicipalityId == actor.TenantId && x.Id == id, ct);
            if (remittance is null) return Result<RemittanceDetailDto>.NotFound();
            if (remittance.Status == RemittanceStatus.Voided)
                return Result<RemittanceDetailDto>.Failure("This remittance is already voided.", ResultStatus.Conflict);
            try { remittance.Void(request.Reason, actor.Username, UtcNow); }
            catch (ArgumentException ex) { return Result<RemittanceDetailDto>.Failure(ex.Message, ResultStatus.Invalid); }
            // Voiding frees the covered collections so the correct record can cover them. The remittance and its
            // coverage rows stay as history; nothing is deleted and no collection is touched.
            var coverages = await db.CollectionRemittanceCoverages.Where(x =>
                x.MunicipalityId == actor.TenantId && x.RemittanceId == id).ToListAsync(ct);
            foreach (var coverage in coverages) coverage.Release();
            await db.SaveChangesAsync(ct);
            return await DetailAsync(actor.TenantId, id, ct);
        }, ct);

    // ── Reads ──────────────────────────────────────────────────────────────────────────────────────────

    public Task<Result<IReadOnlyList<RemittanceRowDto>>> GetRegisterAsync(
        DateOnly from, DateOnly to, Guid? collectorId, RevenueInstrumentType? instrument, RemittanceStatus? status,
        CancellationToken ct = default) =>
        Run<IReadOnlyList<RemittanceRowDto>>(async actor =>
        {
            if (Validate(from, to) is { } problem) return Result<IReadOnlyList<RemittanceRowDto>>.Failure(problem, ResultStatus.Invalid);
            var query = db.CollectionRemittances.AsNoTracking().Where(x =>
                x.MunicipalityId == actor.TenantId && x.RemittanceDate >= from && x.RemittanceDate <= to);
            if (collectorId is { } c) query = query.Where(x => x.CollectorId == c);
            if (instrument is { } i) query = query.Where(x => x.Instrument == i);
            if (status is { } s) query = query.Where(x => x.Status == s);
            var rows = await query.OrderByDescending(x => x.RemittanceDate).ThenByDescending(x => x.RecordedAtUtc).ToListAsync(ct);
            var names = await CollectorNamesAsync(actor.TenantId, rows.Select(x => x.CollectorId).Distinct().ToArray(), ct);
            return Result<IReadOnlyList<RemittanceRowDto>>.Success(rows.Select(x => ToRow(x, names)).ToList());
        }, ct);

    public Task<Result<RemittanceDetailDto>> GetDetailAsync(Guid id, CancellationToken ct = default) =>
        Run<RemittanceDetailDto>(actor => DetailAsync(actor.TenantId, id, ct), ct);

    /// <summary>
    /// Every collector's money and forms position for a period. Collected, remitted and unremitted are derived from posted
    /// Collections and active coverage only; legacy-authoritative collections that cannot be covered exactly are stated
    /// apart, and remaining tickets are a count of forms, never a peso amount.
    /// </summary>
    public Task<Result<AccountabilityPositionDto>> GetPositionAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
        Run<AccountabilityPositionDto>(actor => BuildPositionAsync(actor.TenantId, null, from, to, ct), ct);

    /// <summary>
    /// The signed-in collector's own position, read-only. The collector is taken from the authenticated identity, never from
    /// a request value, and the same derivation as the office position is used so the figures cannot disagree. Nothing about
    /// another collector or the office-wide books is returned.
    /// </summary>
    public async Task<Result<CollectorPositionDto>> GetMyPositionAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collectorId
            || collectorId == Guid.Empty)
            return Result<CollectorPositionDto>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<CollectorPositionDto>.Forbidden();
        var result = await BuildPositionAsync(tenantId, collectorId, from, to, ct);
        if (!result.IsSuccess || result.Value is null)
            return Result<CollectorPositionDto>.Failure(result.Error ?? "The position could not be loaded.", result.Status);
        var mine = result.Value.Collectors.FirstOrDefault(x => x.CollectorId == collectorId)
            ?? new CollectorPositionDto(collectorId, currentUser.Username ?? "Collector", 0m, 0m, 0m, 0, 0m, []);
        return Result<CollectorPositionDto>.Success(mine);
    }

    /// <summary>
    /// The signed-in collector's posted Collections for a business-date range, each net of its corrections, from the same facts
    /// the position above sums. The Mobile report reads its canonical side here so its totals and the Position's agree.
    /// </summary>
    public async Task<Result<IReadOnlyList<CollectorCollectionFactDto>>> GetMyCollectionsAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collectorId
            || collectorId == Guid.Empty)
            return Result<IReadOnlyList<CollectorCollectionFactDto>>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<IReadOnlyList<CollectorCollectionFactDto>>.Forbidden();
        if (Validate(from, to) is { } problem)
            return Result<IReadOnlyList<CollectorCollectionFactDto>>.Failure(problem, ResultStatus.Invalid);

        var facts = await LoadCollectionFactsAsync(tenantId, collectorId, from, to, null, ct);
        var payorIds = facts.Where(x => x.PayorId is not null).Select(x => x.PayorId!.Value).Distinct().ToArray();
        var payorNames = payorIds.Length == 0 ? [] : await db.Payors.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && payorIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        IReadOnlyList<CollectorCollectionFactDto> rows = facts
            .OrderBy(x => x.BusinessDate).ThenBy(x => x.DocumentNumber, StringComparer.Ordinal)
            .Select(x => new CollectorCollectionFactDto(x.CollectionId, x.BusinessDate, x.DocumentNumber, x.Instrument,
                x.PayorId, x.PayorId is { } id ? payorNames.GetValueOrDefault(id) : null, x.Net, x.Lines))
            .ToList();
        return Result<IReadOnlyList<CollectorCollectionFactDto>>.Success(rows);
    }

    private async Task<Result<AccountabilityPositionDto>> BuildPositionAsync(
        Guid tenantId, Guid? onlyCollector, DateOnly from, DateOnly to, CancellationToken ct)
    {
        {
            if (Validate(from, to) is { } problem) return Result<AccountabilityPositionDto>.Failure(problem, ResultStatus.Invalid);
            var facts = await LoadCollectionFactsAsync(tenantId, onlyCollector, from, to, null, ct);
            var covered = facts.Where(x => x.Covered).ToList();
            var remitQuery = db.CollectionRemittances.AsNoTracking().Where(x =>
                x.MunicipalityId == tenantId && x.Status == RemittanceStatus.Recorded
                && x.DifferenceAmount != 0m && x.RemittanceDate >= from && x.RemittanceDate <= to);
            if (onlyCollector is { } only) remitQuery = remitQuery.Where(x => x.CollectorId == only);
            var remittances = await remitQuery.ToListAsync(ct);

            var forms = (await FormPositionsAsync(tenantId, ct))
                .Where(k => onlyCollector is null || k.Key.CollectorId == onlyCollector).ToDictionary(k => k.Key, k => k.Value);
            var collectorIds = facts.Select(x => x.CollectorId!.Value).Concat(forms.Keys.Select(k => k.CollectorId))
                .Concat(remittances.Select(x => x.CollectorId)).Distinct().ToArray();
            var names = await CollectorNamesAsync(tenantId, collectorIds, ct);

            var rows = new List<CollectorPositionDto>();
            foreach (var collectorId in collectorIds)
            {
                var mine = facts.Where(x => x.CollectorId == collectorId).ToList();
                var collected = mine.Sum(x => x.Net);
                var remitted = mine.Where(x => x.Covered).Sum(x => x.Net);
                var review = mine.Count(x => x.Covered && x.CorrectedAfterRemittance)
                    + remittances.Count(x => x.CollectorId == collectorId);
                var legacy = 0m;
                if (legacyQueries is not null)
                {
                    var data = await legacyQueries.GetCollectionsAsync(collectorId, from, to, ct);
                    legacy = data.Lines.Where(x => !x.IsCanonical).Sum(x => x.Amount);
                }
                rows.Add(new CollectorPositionDto(collectorId, names.GetValueOrDefault(collectorId, "Collector"),
                    collected, remitted, collected - remitted, review, legacy,
                    forms.Where(k => k.Key.CollectorId == collectorId).Select(k => k.Value).OrderBy(x => x.Instrument).ToList()));
            }
            rows = rows.Where(x => x.Collected != 0m || x.Forms.Any(f => f.Assigned > 0) || x.NeedsReviewCount > 0)
                .OrderBy(x => x.CollectorName, StringComparer.OrdinalIgnoreCase).ToList();
            return Result<AccountabilityPositionDto>.Success(new AccountabilityPositionDto(from, to,
                rows.Sum(x => x.Collected), rows.Sum(x => x.Remitted), rows.Sum(x => x.Unremitted),
                rows.Sum(x => x.NeedsReviewCount), rows.Sum(x => x.LegacyCollectionsOutsideRemittance), rows));
        }
    }

    // ── Internals ──────────────────────────────────────────────────────────────────────────────────────

    private sealed record CollectionFact(
        Guid CollectionId, Guid? CollectorId, DateOnly BusinessDate, string? DocumentNumber, RevenueInstrumentType? Instrument,
        string? PayerName, decimal Net, IReadOnlyList<RemittanceBreakdownDto> Lines, bool Covered, Guid? CoveringRemittanceId,
        bool CorrectedAfterRemittance, Guid? PayorId = null);

    /// <summary>The Collections a collector could still remit: posted, in scope, money left after corrections, not actively covered.</summary>
    private async Task<IReadOnlyList<RemittanceCollectionDto>> EligibleAsync(
        Guid tenantId, Guid collectorId, DateOnly from, DateOnly to, RevenueInstrumentType? instrument, CancellationToken ct) =>
        (await LoadCollectionFactsAsync(tenantId, collectorId, from, to, instrument, ct))
            .Where(x => !x.Covered && x.Net > 0m)
            .OrderBy(x => x.BusinessDate).ThenBy(x => x.DocumentNumber, StringComparer.Ordinal)
            .Select(ToCollectionDto).ToList();

    private async Task<List<CollectionFact>> LoadCollectionFactsAsync(
        Guid tenantId, Guid? collectorId, DateOnly from, DateOnly to, RevenueInstrumentType? instrument, CancellationToken ct,
        DateTime? correctedAfterUtc = null)
    {
        var collections = db.Collections.AsNoTracking().Where(x =>
            x.MunicipalityId == tenantId && x.CollectorId != null && x.BusinessDate >= from && x.BusinessDate <= to);
        if (collectorId is { } c) collections = collections.Where(x => x.CollectorId == c);
        var list = await collections.Select(x => new { x.Id, x.CollectorId, x.BusinessDate, x.PayerName, x.PayorId, x.TotalAmount, x.RecordedAtUtc })
            .ToListAsync(ct);
        if (list.Count == 0) return [];
        var ids = list.Select(x => x.Id).ToArray();
        var lines = await db.CollectionLines.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && ids.Contains(x.CollectionId))
            .Select(x => new { x.Id, x.CollectionId, x.RevenueClassificationId, x.Amount }).ToListAsync(ct);
        var lineIds = lines.Select(x => x.Id).ToArray();
        var documents = (await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.CollectionId != null && ids.Contains(x.CollectionId!.Value))
            .Select(x => new { CollectionId = x.CollectionId!.Value, x.DocumentNumber, x.InstrumentType }).ToListAsync(ct))
            .GroupBy(x => x.CollectionId).ToDictionary(g => g.Key, g => g.First());
        var corrections = await db.CollectionCorrections.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && ids.Contains(x.OriginalCollectionId))
            .Select(x => new { x.Id, x.OriginalCollectionId, x.RecordedAtUtc, x.FinancialEffectAmount }).ToListAsync(ct);
        var correctionIds = corrections.Select(x => x.Id).ToArray();
        var correctionLines = correctionIds.Length == 0 ? [] : await db.CollectionCorrectionLines.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && correctionIds.Contains(x.CorrectionId))
            .Select(x => new { x.OriginalCollectionLineId, x.FinancialEffectAmount }).ToListAsync(ct);
        var coverage = (await db.CollectionRemittanceCoverages.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.IsActive && ids.Contains(x.CollectionId))
            .Select(x => new { x.CollectionId, x.RemittanceId }).ToListAsync(ct)).ToDictionary(x => x.CollectionId);
        var coveringIds = coverage.Values.Select(v => v.RemittanceId).Distinct().ToList();
        var remittanceTimes = coverage.Count == 0 ? [] : await db.CollectionRemittances.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && coveringIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.RecordedAtUtc, ct);
        var names = await ClassificationNamesAsync(tenantId, lines.Select(x => x.RevenueClassificationId).Distinct().ToArray(), to, ct);

        var facts = new List<CollectionFact>();
        foreach (var collection in list)
        {
            documents.TryGetValue(collection.Id, out var document);
            if (instrument is { } wanted && document?.InstrumentType != wanted) continue;
            var myCorrections = corrections.Where(x => x.OriginalCollectionId == collection.Id).ToList();
            var net = collection.TotalAmount + myCorrections.Sum(x => x.FinancialEffectAmount);
            var myLines = lines.Where(x => x.CollectionId == collection.Id).Select(line =>
            {
                var effect = correctionLines.Where(x => x.OriginalCollectionLineId == line.Id).Sum(x => x.FinancialEffectAmount);
                return new RemittanceBreakdownDto(line.RevenueClassificationId,
                    names.GetValueOrDefault(line.RevenueClassificationId, "Unclassified"), line.Amount + effect);
            }).ToList();
            var isCovered = coverage.TryGetValue(collection.Id, out var cover);
            var after = correctedAfterUtc is { } reference
                ? myCorrections.Any(x => x.RecordedAtUtc > reference)
                : isCovered && remittanceTimes.TryGetValue(cover!.RemittanceId, out var at)
                && myCorrections.Any(x => x.RecordedAtUtc > at);
            facts.Add(new CollectionFact(collection.Id, collection.CollectorId, collection.BusinessDate,
                document?.DocumentNumber, document?.InstrumentType, collection.PayerName, net, myLines, isCovered,
                isCovered ? cover!.RemittanceId : null, after, collection.PayorId));
        }
        return facts;
    }

    private async Task<Dictionary<Guid, string>> ClassificationNamesAsync(
        Guid tenantId, Guid[] classificationIds, DateOnly asOf, CancellationToken ct)
    {
        if (classificationIds.Length == 0) return [];
        var policies = await db.RevenueClassificationPolicies.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && classificationIds.Contains(x.RevenueClassificationId)
                && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= asOf.AddYears(1))
            .Select(x => new { x.RevenueClassificationId, x.EffectiveDate, x.DisplayName }).ToListAsync(ct);
        var codes = await db.RevenueClassifications.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && classificationIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.SemanticCode, ct);
        return classificationIds.ToDictionary(id => id, id =>
            policies.Where(p => p.RevenueClassificationId == id).OrderByDescending(p => p.EffectiveDate)
                .Select(p => p.DisplayName).FirstOrDefault() ?? codes.GetValueOrDefault(id, "Unclassified"));
    }

    private static RemittanceCollectionDto ToCollectionDto(CollectionFact x) =>
        new(x.CollectionId, x.BusinessDate, x.DocumentNumber, x.Instrument, x.PayerName, x.Net, x.Lines, x.CorrectedAfterRemittance);

    private static RemittanceScopeDto BuildScope(
        Guid collectorId, DateOnly from, DateOnly to, RevenueInstrumentType? instrument, IReadOnlyList<RemittanceCollectionDto> collections) =>
        new(collectorId, from, to, instrument, collections, collections.Sum(x => x.NetAmount), Breakdown(collections),
            FirstDocument(collections), LastDocument(collections));

    private static IReadOnlyList<RemittanceBreakdownDto> Breakdown(IEnumerable<RemittanceCollectionDto> collections) =>
        collections.SelectMany(x => x.Lines).GroupBy(x => x.ClassificationId)
            .Select(g => new RemittanceBreakdownDto(g.Key, g.First().Name, g.Sum(x => x.Amount)))
            .Where(x => x.Amount != 0m).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private static string? FirstDocument(IEnumerable<RemittanceCollectionDto> collections) =>
        collections.Select(x => x.DocumentNumber).Where(x => x is not null).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault();

    private static string? LastDocument(IEnumerable<RemittanceCollectionDto> collections) =>
        collections.Select(x => x.DocumentNumber).Where(x => x is not null).OrderBy(x => x, StringComparer.Ordinal).LastOrDefault();

    private async Task<Result<RemittanceDetailDto>> DetailAsync(Guid tenantId, Guid id, CancellationToken ct)
    {
        var remittance = await db.CollectionRemittances.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.Id == id, ct);
        if (remittance is null) return Result<RemittanceDetailDto>.NotFound();
        var covered = await db.CollectionRemittanceCoverages.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.RemittanceId == id).Select(x => x.CollectionId).ToListAsync(ct);
        var facts = (await LoadCollectionFactsForIdsAsync(tenantId, covered, remittance, ct));
        var names = await CollectorNamesAsync(tenantId, [remittance.CollectorId], ct);
        var collections = facts.Select(ToCollectionDto).OrderBy(x => x.BusinessDate).ThenBy(x => x.DocumentNumber, StringComparer.Ordinal).ToList();
        return Result<RemittanceDetailDto>.Success(new RemittanceDetailDto(
            ToRow(remittance, names), remittance.PeriodFrom, remittance.PeriodTo, remittance.Remarks, remittance.RecordedBy,
            remittance.VoidReason, remittance.VoidedBy, remittance.VoidedAtUtc, collections, Breakdown(collections),
            FirstDocument(collections), LastDocument(collections),
            remittance.NeedsReview || collections.Any(x => x.CorrectedAfterRemittance)));
    }

    /// <summary>The covered collections of one remittance, read with the same net/breakdown rules, whether or not still active.</summary>
    private async Task<List<CollectionFact>> LoadCollectionFactsForIdsAsync(
        Guid tenantId, List<Guid> collectionIds, CollectionRemittance remittance, CancellationToken ct)
    {
        if (collectionIds.Count == 0) return [];
        var earliest = await db.Collections.AsNoTracking().Where(x => x.MunicipalityId == tenantId && collectionIds.Contains(x.Id))
            .Select(x => x.BusinessDate).MinAsync(ct);
        var latest = await db.Collections.AsNoTracking().Where(x => x.MunicipalityId == tenantId && collectionIds.Contains(x.Id))
            .Select(x => x.BusinessDate).MaxAsync(ct);
        var all = await LoadCollectionFactsAsync(tenantId, remittance.CollectorId, earliest, latest, null, ct, remittance.RecordedAtUtc);
        var wanted = collectionIds.ToHashSet();
        return all.Where(x => wanted.Contains(x.CollectionId)).ToList();
    }

    private async Task<Dictionary<(Guid CollectorId, RevenueInstrumentType Instrument), FormAccountabilityDto>> FormPositionsAsync(
        Guid tenantId, CancellationToken ct)
    {
        // The latest assignment of a document says which collector held it, whatever became of it after.
        var assignments = await db.AccountableFormAssignments.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId)
            .Select(x => new { x.AccountableDocumentId, x.AssignedUserId, x.AssignedAtUtc }).ToListAsync(ct);
        if (assignments.Count == 0) return [];
        var latest = assignments.GroupBy(x => x.AccountableDocumentId)
            .Select(g => g.OrderByDescending(x => x.AssignedAtUtc).First()).ToList();
        var docIds = latest.Select(x => x.AccountableDocumentId).ToArray();
        var documents = await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && docIds.Contains(x.Id))
            .Select(x => new { x.Id, x.InstrumentType, x.State }).ToDictionaryAsync(x => x.Id, ct);
        var rows = new Dictionary<(Guid, RevenueInstrumentType), FormAccountabilityDto>();
        foreach (var group in latest.Where(x => documents.ContainsKey(x.AccountableDocumentId))
                     .GroupBy(x => (x.AssignedUserId, documents[x.AccountableDocumentId].InstrumentType)))
        {
            int Count(AccountableDocumentState state) => group.Count(x => documents[x.AccountableDocumentId].State == state);
            rows[group.Key] = new FormAccountabilityDto(group.Key.InstrumentType, group.Count(),
                Count(AccountableDocumentState.Consumed), Count(AccountableDocumentState.Voided),
                Count(AccountableDocumentState.InOffice), Count(AccountableDocumentState.ReconciliationRequired),
                Count(AccountableDocumentState.Assigned));
        }
        return rows;
    }

    private async Task<bool> CollectorExistsAsync(Guid tenantId, Guid collectorId, CancellationToken ct) =>
        collectorId != Guid.Empty && await db.CollectorUsers.AsNoTracking().AnyAsync(x =>
            x.MunicipalityId == tenantId && x.Id == collectorId, ct);

    private async Task<Dictionary<Guid, string>> CollectorNamesAsync(Guid tenantId, Guid[] ids, CancellationToken ct) =>
        ids.Length == 0 ? [] : await db.CollectorUsers.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.FullName ?? x.Username ?? "Collector", ct);

    private static RemittanceRowDto ToRow(CollectionRemittance x, Dictionary<Guid, string> names) => new(
        x.Id, x.RemittanceDate, x.CollectorId, names.GetValueOrDefault(x.CollectorId, "Collector"), x.Instrument,
        x.CollectionCount, x.ExpectedAmount, x.RemittedAmount, x.DifferenceAmount, x.Reference, x.Status, x.RecordedAtUtc);

    private static string? Validate(DateOnly from, DateOnly to) =>
        from > to || to.DayNumber - from.DayNumber > 366 ? "Choose a valid period of no more than 367 days." : null;

    private static string Fingerprint(Actor actor, RecordRemittanceRequest request) =>
        PostingOperation.ComputeIntentFingerprint(IntentVersion, JsonSerializer.Serialize(new
        {
            request.CollectorId,
            Remitted = request.AmountRemitted.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            Date = request.RemittanceDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            From = request.From.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            To = request.To.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Instrument = request.Instrument?.ToString(),
            Collections = request.CollectionIds?.Distinct().OrderBy(x => x).ToArray(),
            Reference = request.Reference?.Trim(),
            Remarks = request.Remarks?.Trim()
        }, JsonOptions), Origin, actor.ActorId);

    private async Task<Result<T>> Run<T>(Func<Actor, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return Result<T>.Unauthorized();
        // The Head and Administrators record remittances (the office's 2026-08-25 answer); collectors never do.
        if (currentUser.Role is not ("Admin" or "SuperAdmin")) return Result<T>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<T>.Forbidden();
        try { return await action(new Actor(tenantId, currentUser.Username ?? "Office User", userId.ToString("N"))); }
        catch (DbUpdateException) { return Result<T>.Failure("The remittance conflicts with another saved transaction.", ResultStatus.Conflict); }
    }

    private sealed record Actor(Guid TenantId, string Username, string ActorId);
}
