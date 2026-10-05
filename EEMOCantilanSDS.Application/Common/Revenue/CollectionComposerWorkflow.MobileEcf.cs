using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Collector Mobile ECF writer. It reuses the Web composer's authoritative ECF facts, policy, snapshot and source projection so
/// Web and Mobile converge on one source and one amount truth; it only adds the collector actor, an idempotent one-shot post
/// and no physical document (IA-062). It posts only for a source whose Electricity settlement authority is already Canonical.
/// </summary>
public sealed partial class CollectionComposerWorkflow
{
    private const string MobileEcfOrigin = "MobileEcf";

    public async Task<Result<IReadOnlyList<EcfObligationQuoteDto>>> GetMobileEcfSourcesAsync(Guid? payorId = null, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collector)
            return Result<IReadOnlyList<EcfObligationQuoteDto>>.Forbidden();
        var tenant = municipality.MunicipalityId;
        if (tenant == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenant
            || !await db.CollectorUsers.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenant && x.Id == collector
                && x.IsActive && x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM), ct))
            return Result<IReadOnlyList<EcfObligationQuoteDto>>.Forbidden();
        var date = BusinessToday;
        var stalls = await db.Stalls.AsNoTracking().Include(x => x.Facility).Include(x => x.Contracts).ThenInclude(x => x.Payor)
            .Where(x => x.MunicipalityId == tenant && x.Facility!.Code == FacilityCode.NPM
                && (!payorId.HasValue || x.Contracts.Any(c => c.PayorId == payorId))).ToListAsync(ct);
        var ids = stalls.Select(x => x.Id).ToArray();
        var bills = await UtilityBillQuery(tenant, false).Where(x => ids.Contains(x.StallId)).ToListAsync(ct);
        var rows = new List<EcfObligationQuoteDto>();
        PolicyFacts policy;
        try { policy = await ResolveEcfPolicyAsync(tenant, date, ct); }
        catch (WorkflowProblem) { return Result<IReadOnlyList<EcfObligationQuoteDto>>.Success(rows); }
        foreach (var stall in stalls)
        {
            var current = bills.Where(x => x.StallId == stall.Id).ToList();
            if (!current.Any(x => x.IsForMonth(date.Year, date.Month)))
            {
                var provisional = UtilityBill.Create(stall.Id, date.Year, date.Month, 0, 0, 0, 0, 0, 0, "quote");
                provisional.AttachSourceContext(stall);
                current.Add(provisional);
            }
            foreach (var bill in current)
                try
                {
                    var q = (await BuildFactsAsync(bill, policy, tenant, date, ct)).Quote;
                    if (payorId.HasValue && q.PayorId != payorId) continue;
                    if (!q.CanPostCanonical && !bill.IsForMonth(date.Year, date.Month)) continue;
                    rows.Add(bill.MunicipalityId == Guid.Empty ? q with { UtilityBillId = Guid.Empty, ElectricitySourceVersion = 0 } : q);
                }
                catch (WorkflowProblem) { /* A malformed bill cannot hide another source. */ }
        }
        return Result<IReadOnlyList<EcfObligationQuoteDto>>.Success(rows);
    }

    /// <summary>Bounded, read-only source quote for a checkout; no draft, assessment or Collection is created.</summary>
    public async Task<Result<EcfObligationQuoteDto>> QuoteMobileEcfAsync(Guid billId, DateOnly date, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collectorId)
            return Result<EcfObligationQuoteDto>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<EcfObligationQuoteDto>.Forbidden();
        if (!await db.CollectorUsers.AsNoTracking().AnyAsync(x => x.MunicipalityId == tenantId && x.Id == collectorId
            && x.IsActive && x.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM), ct))
            return Result<EcfObligationQuoteDto>.Forbidden();
        var bill = await UtilityBillQuery(tenantId, tracked: false).SingleOrDefaultAsync(x => x.Id == billId, ct);
        if (bill is null) return Result<EcfObligationQuoteDto>.NotFound();
        try
        {
            var policy = await ResolveEcfPolicyAsync(tenantId, date, ct);
            return Result<EcfObligationQuoteDto>.Success((await BuildFactsAsync(bill, policy, tenantId, date, ct)).Quote);
        }
        catch (WorkflowProblem) { return Result<EcfObligationQuoteDto>.Failure("This electricity bill needs office review.", ResultStatus.Conflict); }
    }

    public async Task<Result<EcfPostOutcomeDto>> PostMobileEcfAsync(MobileEcfPostRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collectorId
            || collectorId == Guid.Empty)
            return Result<EcfPostOutcomeDto>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<EcfPostOutcomeDto>.Forbidden();
        var collector = await db.CollectorUsers.AsNoTracking().Include(x => x.FacilityAssignments)
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == collectorId && x.IsActive, ct);
        if (collector is null || collector.FacilityAssignments.All(x => x.FacilityCode != FacilityCode.NPM))
            return Result<EcfPostOutcomeDto>.Forbidden();
        if (request.ClientOperationId == Guid.Empty)
            return Result<EcfPostOutcomeDto>.Failure("A valid ClientOperationId is required.", ResultStatus.Invalid);

        var actor = new Actor(collectorId, tenantId, collector.Username ?? collector.FullName ?? "Collector", "Collector");
        var normalized = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            OperationType = "MobileEcfCollection",
            TenantId = tenantId,
            ActorId = ActorIdentity(actor),
            request.UtilityBillId,
            request.ElectricitySourceVersion,
            BusinessDate = request.BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ReceivedAmount = request.ReceivedAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        }, JsonOptions);
        if (request.StallId.HasValue)
            normalized = System.Text.Json.JsonSerializer.Serialize(new { SchemaVersion = 2, Original = normalized,
                request.StallId, request.BillingYear, request.BillingMonth }, JsonOptions);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, MobileEcfOrigin, ActorIdentity(actor));

        try
        {
            var prior = await FindPostingOperationAsync(tenantId, request.ClientOperationId, ct);
            if (prior is not null) return await ResolveMobileEcfPriorAsync(prior, fingerprint, actor, ct);

            Task<Result<EcfPostOutcomeDto>> Reject(string code, string message) =>
                RecordMobileEcfRejectionAsync(actor, request, normalized, fingerprint, code, message, ct);

            if (request.ReceivedAmount <= 0m || request.ReceivedAmount > Collection.MaximumMoneyAmount
                || request.BusinessDate == default || decimal.Round(request.ReceivedAmount, 2, MidpointRounding.ToZero) != request.ReceivedAmount)
                return await Reject("INVALID_INTENT", "A positive received amount with at most two decimals is required.");
            if (request.BusinessDate > BusinessToday)
                return await Reject("FUTURE_BUSINESS_DATE", "Collection BusinessDate cannot be later than the current Philippine business date.");

            var bill = await UtilityBillQuery(tenantId, tracked: true).SingleOrDefaultAsync(x => x.Id == request.UtilityBillId, ct);
            if (request.UtilityBillId == Guid.Empty && request.StallId is { } stallId
                && request.BillingYear == request.BusinessDate.Year && request.BillingMonth == request.BusinessDate.Month)
            {
                var stall = await db.Stalls.Include(x => x.Facility).Include(x => x.Contracts).ThenInclude(x => x.Payor)
                    .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == stallId && x.Facility!.Code == FacilityCode.NPM, ct);
                if (stall?.OccupancyAnsweringForMonth(request.BillingYear.Value, request.BillingMonth.Value, request.BusinessDate) is null)
                    return await Reject("SOURCE_NOT_FOUND", "This Electricity source is not available.");
                if (await db.UtilityBills.AnyAsync(x => x.MunicipalityId == tenantId && x.StallId == stallId
                    && x.BillingYear == request.BillingYear && x.BillingMonth == request.BillingMonth, ct))
                    return await Reject("SOURCE_VERSION_STALE", "The Electricity source changed. Refresh before recording.");
                bill = UtilityBill.Create(stallId, request.BillingYear.Value, request.BillingMonth.Value, 0, 0, 0, 0, 0, 0, actor.Username);
                bill.AttachSourceContext(stall);
            }
            if (bill is null)
                return await Reject("SOURCE_NOT_FOUND", "The ECF source is not available in this tenant.");
            if (bill.ElectricitySettlementAuthorityState == SettlementAuthority.PendingCutover)
                return await Reject("SOURCE_NOT_CANONICAL",
                    "This ECF source is not yet under canonical settlement authority; it is collected on its existing legacy path until its controlled activation.");
            if (bill.ElectricitySettlementAuthorityState == SettlementAuthority.Legacy
                && (bill.ElecStatus != PaymentStatus.Unpaid || bill.ElecPartialAmount != 0m
                    || bill.ElecPaidAt is not null || !string.IsNullOrWhiteSpace(bill.ElecORNumber)))
                return await Reject("SOURCE_NOT_CANONICAL", "Historical Electricity evidence needs office review before canonical collection.");
            if (bill.ElectricitySourceVersion != request.ElectricitySourceVersion && request.UtilityBillId != Guid.Empty)
                return await Reject("SOURCE_VERSION_STALE", "The ECF source changed after it was shown. Refresh and try again.");

            PolicyFacts policy;
            SourceFacts facts;
            try
            {
                policy = await ResolveEcfPolicyAsync(tenantId, request.BusinessDate, ct);
                facts = await BuildFactsAsync(bill, policy, tenantId, request.BusinessDate, ct);
            }
            catch (WorkflowProblem problem)
            {
                return await Reject("SOURCE_REVIEW_REQUIRED", problem.Message);
            }
            if (!facts.Quote.CanPostCanonical)
                return await Reject("SOURCE_NOT_COLLECTIBLE", "This ECF source has no collectible balance.");
            if (facts.Quote.ChargeBasis != "DirectCollection" && request.ReceivedAmount > facts.Quote.OutstandingAmount)
                return await Reject("AMOUNT_EXCEEDS_OUTSTANDING", "The received amount exceeds the current ECF outstanding balance.");

            if (bill.ElectricitySettlementAuthorityState == SettlementAuthority.Legacy)
            {
                if (bill.ElecCharge == 0m) bill.FreezeDirectCollection(CollectionSourcePart.Electricity);
                bill.MarkElectricityPendingCutover();
                var now = clock?.UtcNow ?? DateTime.UtcNow;
                var cutover = CollectionSettlementCutover.Freeze(tenantId, CollectionSourceKind.UtilityBill, bill.Id,
                    CollectionSourcePart.Electricity, bill.ElectricitySourceVersion, now, bill.ElecCharge, 0m, bill.ElecCharge,
                    System.Text.Json.JsonSerializer.Serialize(new { Kind = "ProspectiveEcfFieldCollection", request.ClientOperationId, Direct = bill.ElectricityDirectCollection }), collectorId, now);
                db.CollectionSettlementCutovers.Add(cutover);
                bill.ActivateCanonicalElectricitySettlement(cutover);
                if (bill.MunicipalityId == Guid.Empty) db.UtilityBills.Add(bill);
            }
            var line = new CollectionLineDraft(facts.Classification, facts.Policy, request.ReceivedAmount,
                CollectionSourceKind.UtilityBill, bill.Id, CollectionSourcePart.Electricity, facts.SnapshotJson,
                [new CollectionAllocationDraft(CollectionSourceKind.UtilityBill, bill.Id, request.ReceivedAmount,
                    CollectionSourcePart.Electricity, facts.SnapshotJson)]);
            var cumulative = facts.Quote.CumulativeSettledEvidence + request.ReceivedAmount;
            var projection = new Action<DateTime>(now => bill.ApplyCanonicalElectricityProjection(cumulative, null, now, actor.Username));
            var collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(
                tenantId, request.ClientOperationId, IntentVersion, normalized, MobileEcfOrigin, ActorIdentity(actor),
                actor.Username, actor.Role, request.BusinessDate, actor.Username, [line], null,
                collectorId: collectorId, payorId: facts.Quote.PayorId, payerName: facts.Quote.PayerNameSnapshot,
                sourceProjections: [projection], ct: ct);
            return Result<EcfPostOutcomeDto>.Success(new EcfPostOutcomeDto(
                collection.Id, collection.ReferenceCode, "Posted", collection.TotalAmount, collection.Lines.Count, false));
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await FindPostingOperationAsync(tenantId, request.ClientOperationId, ct);
            if (winner is not null) return await ResolveMobileEcfPriorAsync(winner, fingerprint, actor, ct);
            return await RecordMobileEcfRejectionAsync(actor, request, normalized, fingerprint, "POST_CONFLICT",
                "The ECF source or operation identity conflicts with another saved transaction; nothing was posted.", ct);
        }
    }

    private async Task<Result<EcfPostOutcomeDto>> ResolveMobileEcfPriorAsync(
        PostingOperation prior, string fingerprint, Actor actor, CancellationToken ct)
    {
        if (prior.Origin != MobileEcfOrigin || prior.ActorId != ActorIdentity(actor) || prior.IntentFingerprint != fingerprint)
            return Result<EcfPostOutcomeDto>.Failure(
                "IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to a different posting intent.", ResultStatus.Conflict);
        if (prior.Status != PostingOperationStatus.Succeeded || prior.CollectionId is not { } collectionId)
            return Result<EcfPostOutcomeDto>.Failure(prior.OutcomeDetails ?? prior.OutcomeCode ?? "The original posting was rejected.", ResultStatus.Conflict);
        return Result<EcfPostOutcomeDto>.Success(await BuildPostOutcomeAsync(actor.MunicipalityId, collectionId, true, ct));
    }

    private async Task<Result<EcfPostOutcomeDto>> RecordMobileEcfRejectionAsync(
        Actor actor, MobileEcfPostRequest request, string normalized, string fingerprint, string code, string message, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        db.PostingOperations.Add(PostingOperation.Record(actor.MunicipalityId, request.ClientOperationId, IntentVersion, normalized,
            MobileEcfOrigin, ActorIdentity(actor), PostingOperationStatus.Rejected, code, message, null, null, DateTime.UtcNow));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var prior = await FindPostingOperationAsync(actor.MunicipalityId, request.ClientOperationId, ct);
            if (prior is not null) return await ResolveMobileEcfPriorAsync(prior, fingerprint, actor, ct);
            throw;
        }
        return Result<EcfPostOutcomeDto>.Failure(message, ResultStatus.Conflict);
    }
}
