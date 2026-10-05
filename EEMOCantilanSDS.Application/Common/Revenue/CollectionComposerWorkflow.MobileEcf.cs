using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
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
        var fingerprint = PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, MobileEcfOrigin, ActorIdentity(actor));

        try
        {
            var prior = await FindPostingOperationAsync(tenantId, request.ClientOperationId, ct);
            if (prior is not null) return await ResolveMobileEcfPriorAsync(prior, fingerprint, actor, ct);

            Task<Result<EcfPostOutcomeDto>> Reject(string code, string message) =>
                RecordMobileEcfRejectionAsync(actor, request, normalized, fingerprint, code, message, ct);

            if (request.ReceivedAmount <= 0m || decimal.Round(request.ReceivedAmount, 2, MidpointRounding.ToZero) != request.ReceivedAmount)
                return await Reject("INVALID_INTENT", "A positive received amount with at most two decimals is required.");
            if (request.BusinessDate > BusinessToday)
                return await Reject("FUTURE_BUSINESS_DATE", "Collection BusinessDate cannot be later than the current Philippine business date.");

            var bill = await UtilityBillQuery(tenantId, tracked: true).SingleOrDefaultAsync(x => x.Id == request.UtilityBillId, ct);
            if (bill is null)
                return await Reject("SOURCE_NOT_FOUND", "The ECF source is not available in this tenant.");
            if (bill.ElectricitySettlementAuthorityState != SettlementAuthority.Canonical)
                return await Reject("SOURCE_NOT_CANONICAL",
                    "This ECF source is not yet under canonical settlement authority; it is collected on its existing legacy path until its controlled activation.");
            if (bill.ElectricitySourceVersion != request.ElectricitySourceVersion)
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
            if (request.ReceivedAmount > facts.Quote.OutstandingAmount)
                return await Reject("AMOUNT_EXCEEDS_OUTSTANDING", "The received amount exceeds the current ECF outstanding balance.");

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
