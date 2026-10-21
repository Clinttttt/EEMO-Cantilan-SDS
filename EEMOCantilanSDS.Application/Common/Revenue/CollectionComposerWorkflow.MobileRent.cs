using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Collector Mobile monthly-rent writer (IA-051/IA-062). It reuses <see cref="MonthlyRentCollectionSourceAdapter"/> (the same
/// facts, classification, policy, snapshot and source projection the Web composer uses) for a PaymentRecord whose settlement
/// authority is already Canonical. No physical OR serial; one idempotent post; the server returns the SRC. A legacy-authority
/// rent row is not written here: it stays on its legacy path until its controlled row cutover.
/// </summary>
public sealed partial class CollectionComposerWorkflow
{
    private const string MobileRentOrigin = "MobileRent";

    public Task<Result<EcfPostOutcomeDto>> PostMobileRentAsync(MobileRentPostRequest request, CancellationToken ct = default)
        => PostRentReceiptAsync(request, office: false, ct);

    public Task<Result<EcfPostOutcomeDto>> PostOfficeRentAsync(MobileRentPostRequest request, CancellationToken ct = default)
        => PostRentReceiptAsync(request, office: true, ct);

    private async Task<Result<EcfPostOutcomeDto>> PostRentReceiptAsync(MobileRentPostRequest request, bool office, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty
            || (office ? currentUser.Role is not ("Admin" or "SuperAdmin")
                : currentUser.Role != "Collector" || currentUser.CollectorId is null || currentUser.CollectorId == Guid.Empty))
            return Result<EcfPostOutcomeDto>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<EcfPostOutcomeDto>.Forbidden();
        if (request.ClientOperationId == Guid.Empty)
            return Result<EcfPostOutcomeDto>.Failure("A valid ClientOperationId is required.", ResultStatus.Invalid);

        var collectorId = office ? (Guid?)null : currentUser.CollectorId;
        var collector = office ? null : await db.CollectorUsers.AsNoTracking().Include(x => x.FacilityAssignments)
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == collectorId && x.IsActive, ct);
        if (!office && collector is null) return Result<EcfPostOutcomeDto>.Forbidden();
        var actor = new Actor(collectorId ?? userId, tenantId,
            office ? currentUser.Username ?? "Office" : collector!.Username ?? collector.FullName ?? "Collector",
            office ? currentUser.Role! : "Collector");
        var origin = office ? "OfficeRent" : MobileRentOrigin;
        var normalized = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            OperationType = office ? "OfficeRentCollection" : "MobileRentCollection",
            TenantId = tenantId,
            ActorId = ActorIdentity(actor),
            request.StallId,
            request.BillingYear,
            request.BillingMonth,
            request.SourceVersion,
            BusinessDate = request.BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ReceivedAmount = request.ReceivedAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        }, JsonOptions);
        if (office) normalized = System.Text.Json.JsonSerializer.Serialize(new { Request = normalized, request.ExpectedContractId }, JsonOptions);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, origin, ActorIdentity(actor));

        try
        {
            var prior = await FindPostingOperationAsync(tenantId, request.ClientOperationId, ct);
            if (prior is not null)
            {
                var replay = await ResolveMobileRentPriorAsync(prior, fingerprint, actor, origin, ct);
                if (replay.IsSuccess) await InvalidateRentViewsAsync(request, ct);
                return replay;
            }

            Task<Result<EcfPostOutcomeDto>> Reject(string code, string message) =>
                RecordMobileRentRejectionAsync(actor, request, normalized, fingerprint, code, message, origin, ct);

            if (request.ReceivedAmount <= 0m || decimal.Round(request.ReceivedAmount, 2, MidpointRounding.ToZero) != request.ReceivedAmount)
                return await Reject("INVALID_INTENT", "A positive received amount with at most two decimals is required.");
            if (request.BusinessDate > BusinessToday)
                return await Reject("FUTURE_BUSINESS_DATE", "Collection BusinessDate cannot be later than the current Philippine business date.");
            if (office && request.BusinessDate != BusinessToday)
                return await Reject("OFFICE_REVIEW_REQUIRED", "This office receipt quote is no longer for today's business date. Reconcile an unconfirmed earlier payment before creating a new receipt.");

            var stallFacility = await db.Stalls.AsNoTracking().Where(x => x.Id == request.StallId && x.MunicipalityId == tenantId)
                .Select(x => x.Facility!.Code).Cast<FacilityCode?>().SingleOrDefaultAsync(ct);
            if (stallFacility is not { } facilityCode || !office && collector!.FacilityAssignments.All(x => x.FacilityCode != facilityCode))
                return await Reject("COLLECTOR_FACILITY_NOT_ASSIGNED", "The collector is not assigned to this stall's facility.");
            if (office && rentOptions?.Allows(tenantId, facilityCode) != true)
                return Result<EcfPostOutcomeDto>.Failure("Office rent receipts remain disabled for this municipality/facility pending source and reporting cutover acceptance.", ResultStatus.Conflict);

            var facts = await new MonthlyRentCollectionSourceAdapter(db).LoadAsync(tenantId, request.StallId,
                request.BillingYear, request.BillingMonth, request.BusinessDate,
                tracked: true, materializeMissingSource: false, actor: actor.Username, ct: ct);
            if (facts?.Record is not { } record)
                return await Reject("SOURCE_NOT_FOUND", "No canonical rent source exists for this stall and month.");
            if (record.SettlementAuthorityState != SettlementAuthority.Canonical)
                return await Reject("SOURCE_NOT_CANONICAL",
                    "This rent row is not yet under canonical settlement authority; it is collected on its existing legacy path until its controlled activation.");
            if (office && request.ExpectedContractId != facts.Contract.Id)
                return await Reject("SOURCE_OWNER_STALE", "The responsible occupancy no longer matches the reviewed office quote. Refresh and reconcile the source before posting.");
            if (facts.Quote.SourceVersion != request.SourceVersion)
                return await Reject("SOURCE_VERSION_STALE", "The rent source changed after it was shown. Refresh and try again.");
            if (facts.Quote.RequiresLegacyReconciliation)
                return await Reject("SOURCE_REVIEW_REQUIRED", "This rent row has mixed legacy components and requires reconciliation first.");
            if (!facts.Quote.CanPostCanonical)
                return await Reject("SOURCE_NOT_COLLECTIBLE", "This rental period has no collectible rent balance.");
            if (request.ReceivedAmount > facts.Quote.OutstandingAmount)
                return await Reject("AMOUNT_EXCEEDS_OUTSTANDING", "The received amount exceeds the current rent outstanding balance.");

            const string lineSnapshot = "{\"schemaVersion\":1,\"lineKind\":\"PermanentStallRent\"}";
            var line = new CollectionLineDraft(facts.Classification, facts.Policy, request.ReceivedAmount,
                null, null, null, lineSnapshot,
                [new CollectionAllocationDraft(CollectionSourceKind.PaymentRecord, record.Id, request.ReceivedAmount, null, facts.SnapshotJson)]);
            var cumulative = facts.Quote.CumulativeSettledEvidence + request.ReceivedAmount;
            var projection = new Action<DateTime>(now => record.ApplyCanonicalRentProjection(cumulative, now, actor.Username));
            var collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(
                tenantId, request.ClientOperationId, IntentVersion, normalized, origin, ActorIdentity(actor),
                actor.Username, actor.Role, request.BusinessDate, actor.Username, [line], null,
                collectorId: collectorId, payorId: facts.Quote.PayorId, payerName: facts.Quote.PayerNameSnapshot,
                sourceProjections: [projection], ct: ct);
            await InvalidateRentViewsAsync(request, ct);
            return Result<EcfPostOutcomeDto>.Success(new EcfPostOutcomeDto(
                collection.Id, collection.ReferenceCode, "Posted", collection.TotalAmount, collection.Lines.Count, false));
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await FindPostingOperationAsync(tenantId, request.ClientOperationId, ct);
            if (winner is not null) return await ResolveMobileRentPriorAsync(winner, fingerprint, actor, origin, ct);
            return await RecordMobileRentRejectionAsync(actor, request, normalized, fingerprint, "POST_CONFLICT",
                "The rent source or operation identity conflicts with another saved transaction; nothing was posted.", origin, ct);
        }
        catch (InvalidOperationException)
        {
            db.ChangeTracker.Clear();
            return Result<EcfPostOutcomeDto>.Failure("Rent source evidence requires office reconciliation. Review its recorded occupancy, approved policy and cutover evidence; no legacy payment was substituted.", ResultStatus.Conflict);
        }
    }

    private async Task InvalidateRentViewsAsync(MobileRentPostRequest request, CancellationToken ct)
    {
        if (cache is null || string.IsNullOrWhiteSpace(currentUser.MunicipalityCode)) return;
        try
        {
            await cache.InvalidatePaymentAffectedViewsAsync(currentUser.MunicipalityCode, null, request.BillingYear, request.BillingMonth, ct);
            if (request.BusinessDate.Year != request.BillingYear || request.BusinessDate.Month != request.BillingMonth)
                await cache.InvalidatePaymentAffectedViewsAsync(currentUser.MunicipalityCode, null, request.BusinessDate.Year, request.BusinessDate.Month, ct);
        }
        catch (Exception ex)
        {
            // The receipt is already committed; a cache failure must not become a false payment rejection.
            logger?.LogWarning(ex, "Monthly rent receipt saved; cached views need refresh.");
        }
    }

    private async Task<Result<EcfPostOutcomeDto>> ResolveMobileRentPriorAsync(
        PostingOperation prior, string fingerprint, Actor actor, string origin, CancellationToken ct)
    {
        if (prior.Origin != origin || prior.ActorId != ActorIdentity(actor) || prior.IntentFingerprint != fingerprint)
            return Result<EcfPostOutcomeDto>.Failure(
                "IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to a different posting intent.", ResultStatus.Conflict);
        if (prior.Status != PostingOperationStatus.Succeeded || prior.CollectionId is not { } collectionId)
            return Result<EcfPostOutcomeDto>.Failure(prior.OutcomeDetails ?? prior.OutcomeCode ?? "The original posting was rejected.", ResultStatus.Conflict);
        return Result<EcfPostOutcomeDto>.Success(await BuildPostOutcomeAsync(actor.MunicipalityId, collectionId, true, ct));
    }

    private async Task<Result<EcfPostOutcomeDto>> RecordMobileRentRejectionAsync(
        Actor actor, MobileRentPostRequest request, string normalized, string fingerprint, string code, string message, string origin, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        db.PostingOperations.Add(PostingOperation.Record(actor.MunicipalityId, request.ClientOperationId, IntentVersion, normalized,
            origin, ActorIdentity(actor), PostingOperationStatus.Rejected, code, message, null, null, DateTime.UtcNow));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var prior = await FindPostingOperationAsync(actor.MunicipalityId, request.ClientOperationId, ct);
            if (prior is not null) return await ResolveMobileRentPriorAsync(prior, fingerprint, actor, origin, ct);
            throw;
        }
        return Result<EcfPostOutcomeDto>.Failure(message, ResultStatus.Conflict);
    }
}
