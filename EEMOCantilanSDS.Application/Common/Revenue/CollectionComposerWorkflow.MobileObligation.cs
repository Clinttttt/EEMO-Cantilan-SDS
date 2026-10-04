using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Collector Mobile Fish / Meat Vendor Fee (IA-050/IA-062). The authority is the existing obligation account: an NPM Fish/Meat stall's
/// explicit Payor, an approved effective-dated monthly amount, and periods assessed from it. This reuses
/// <see cref="ObligationCollectionSource"/> (the same facts, classification, instrument policy and snapshot the Web composer uses) so the
/// two channels share one balance authority. It is a distinct source: never NPM daily stall fees, Tabo, Market Fees or Weight and Measure.
/// </summary>
public sealed partial class CollectionComposerWorkflow
{
    private const string MobileObligationOrigin = "MobileObligation";

    /// <summary>The vendor-fee periods with a remaining balance that an NPM-assigned collector may collect, oldest first.</summary>
    public async Task<Result<IReadOnlyList<MobileVendorFeeDueDto>>> GetMobileVendorFeeDuesAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collectorId
            || collectorId == Guid.Empty)
            return Result<IReadOnlyList<MobileVendorFeeDueDto>>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<IReadOnlyList<MobileVendorFeeDueDto>>.Forbidden();
        var collector = await db.CollectorUsers.AsNoTracking().Include(x => x.FacilityAssignments)
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == collectorId && x.IsActive, ct);
        if (collector is null || collector.FacilityAssignments.All(x => x.FacilityCode != FacilityCode.NPM))
            return Result<IReadOnlyList<MobileVendorFeeDueDto>>.Forbidden();

        var today = BusinessToday;
        var accounts = await db.ObligationAccounts.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.Kind == ObligationKind.FishMeatVendorFee).ToListAsync(ct);
        var quotes = await new ObligationCollectionSource(db).GetQuotesAsync(tenantId, accounts, today, ct);
        var stallIds = accounts.Where(x => x.StallId.HasValue).Select(x => x.StallId!.Value).ToArray();
        var stallNos = await db.Stalls.AsNoTracking().Where(x => x.MunicipalityId == tenantId && stallIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.StallNo, ct);
        var stallByAccount = accounts.ToDictionary(x => x.Id, x => x.StallId);
        var dues = quotes.Where(x => x.OutstandingAmount > 0m && x.PeriodStart <= today)
            .Select(x => new MobileVendorFeeDueDto(x.AccountId, x.SubjectLabel,
                stallByAccount[x.AccountId] is { } sid ? stallNos.GetValueOrDefault(sid) : null, x.PayerName, x.PeriodStart,
                x.AssessedAmount, x.SettledAmount, x.OutstandingAmount)).ToList();
        return Result<IReadOnlyList<MobileVendorFeeDueDto>>.Success(dues);
    }

    public async Task<Result<EcfPostOutcomeDto>> PostMobileObligationAsync(MobileObligationPostRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector" || currentUser.CollectorId is not { } collectorId
            || collectorId == Guid.Empty)
            return Result<EcfPostOutcomeDto>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<EcfPostOutcomeDto>.Forbidden();
        if (request.ClientOperationId == Guid.Empty)
            return Result<EcfPostOutcomeDto>.Failure("A valid ClientOperationId is required.", ResultStatus.Invalid);
        var collector = await db.CollectorUsers.AsNoTracking().Include(x => x.FacilityAssignments)
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == collectorId && x.IsActive, ct);
        if (collector is null || collector.FacilityAssignments.All(x => x.FacilityCode != FacilityCode.NPM))
            return Result<EcfPostOutcomeDto>.Forbidden();

        var actor = new Actor(collectorId, tenantId, collector.Username ?? collector.FullName ?? "Collector", "Collector");
        var normalized = System.Text.Json.JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            OperationType = "MobileObligationCollection",
            TenantId = tenantId,
            ActorId = ActorIdentity(actor),
            request.AccountId,
            request.BillingYear,
            request.BillingMonth,
            BusinessDate = request.BusinessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ReceivedAmount = request.ReceivedAmount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
        }, JsonOptions);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, MobileObligationOrigin, ActorIdentity(actor));

        try
        {
            var prior = await FindPostingOperationAsync(tenantId, request.ClientOperationId, ct);
            if (prior is not null) return await ResolveMobileObligationPriorAsync(prior, fingerprint, actor, ct);

            Task<Result<EcfPostOutcomeDto>> Reject(string code, string message) =>
                RecordMobileObligationRejectionAsync(actor, request, normalized, fingerprint, code, message, ct);

            if (request.ReceivedAmount <= 0m || decimal.Round(request.ReceivedAmount, 2, MidpointRounding.ToZero) != request.ReceivedAmount)
                return await Reject("INVALID_INTENT", "A positive received amount with at most two decimals is required.");
            if (request.BusinessDate > BusinessToday)
                return await Reject("FUTURE_BUSINESS_DATE", "Collection BusinessDate cannot be later than the current Philippine business date.");
            if (request.BillingYear is < 2000 or > 2200 || request.BillingMonth is < 1 or > 12)
                return await Reject("INVALID_INTENT", "A valid billing year and month are required.");

            var account = await db.ObligationAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
                x.MunicipalityId == tenantId && x.Id == request.AccountId && x.Kind == ObligationKind.FishMeatVendorFee, ct);
            if (account is null)
                return await Reject("SOURCE_NOT_FOUND", "The vendor fee account is not available in this tenant.");
            var periodStart = new DateOnly(request.BillingYear, request.BillingMonth, 1);

            ObligationSourceFacts? facts;
            try
            {
                facts = await new ObligationCollectionSource(db).LoadAsync(tenantId, account.Id, periodStart, request.BusinessDate,
                    tracked: true, assess: true, actor: actor.Username, ct: ct);
            }
            catch (InvalidOperationException problem)
            {
                return await Reject("POLICY_NOT_EFFECTIVE", problem.Message);
            }
            if (facts is null)
                return await Reject("SOURCE_NOT_BILLABLE", "This period is not billable: the account is not active for it or no approved amount is in force.");
            if (!facts.Quote.CanAddToDraft)
                return await Reject("SOURCE_NOT_COLLECTIBLE", "This period has no remaining balance to collect.");
            if (request.ReceivedAmount > facts.Quote.OutstandingAmount)
                return await Reject("AMOUNT_EXCEEDS_OUTSTANDING", "The received amount exceeds the remaining balance of this period.");

            var lineSnapshot = $"{{\"schemaVersion\":1,\"lineKind\":\"Obligation\",\"kind\":{(int)account.Kind}}}";
            var line = new CollectionLineDraft(facts.Classification, facts.Policy, request.ReceivedAmount,
                null, null, null, lineSnapshot,
                [new CollectionAllocationDraft(CollectionSourceKind.ObligationPeriod, facts.Period.Id, request.ReceivedAmount, null, facts.SnapshotJson)]);
            var period = facts.Period;
            var projection = new Action<DateTime>(_ => period.ApplyCanonicalSettlement());
            var collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(
                tenantId, request.ClientOperationId, IntentVersion, normalized, MobileObligationOrigin, ActorIdentity(actor),
                actor.Username, actor.Role, request.BusinessDate, actor.Username, [line], null,
                collectorId: collectorId, payorId: facts.Quote.PayorId, payerName: facts.Quote.PayerName,
                sourceProjections: [projection], ct: ct);
            return Result<EcfPostOutcomeDto>.Success(new EcfPostOutcomeDto(
                collection.Id, collection.ReferenceCode, "Posted", collection.TotalAmount, collection.Lines.Count, false));
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var winner = await FindPostingOperationAsync(tenantId, request.ClientOperationId, ct);
            if (winner is not null) return await ResolveMobileObligationPriorAsync(winner, fingerprint, actor, ct);
            return await RecordMobileObligationRejectionAsync(actor, request, normalized, fingerprint, "POST_CONFLICT",
                "The vendor fee period or operation identity conflicts with another saved transaction; nothing was posted.", ct);
        }
    }

    private async Task<Result<EcfPostOutcomeDto>> ResolveMobileObligationPriorAsync(
        PostingOperation prior, string fingerprint, Actor actor, CancellationToken ct)
    {
        if (prior.Origin != MobileObligationOrigin || prior.ActorId != ActorIdentity(actor) || prior.IntentFingerprint != fingerprint)
            return Result<EcfPostOutcomeDto>.Failure(
                "IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to a different posting intent.", ResultStatus.Conflict);
        if (prior.Status != PostingOperationStatus.Succeeded || prior.CollectionId is not { } collectionId)
            return Result<EcfPostOutcomeDto>.Failure(prior.OutcomeDetails ?? prior.OutcomeCode ?? "The original posting was rejected.", ResultStatus.Conflict);
        return Result<EcfPostOutcomeDto>.Success(await BuildPostOutcomeAsync(actor.MunicipalityId, collectionId, true, ct));
    }

    private async Task<Result<EcfPostOutcomeDto>> RecordMobileObligationRejectionAsync(
        Actor actor, MobileObligationPostRequest request, string normalized, string fingerprint, string code, string message, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        db.PostingOperations.Add(PostingOperation.Record(actor.MunicipalityId, request.ClientOperationId, IntentVersion, normalized,
            MobileObligationOrigin, ActorIdentity(actor), PostingOperationStatus.Rejected, code, message, null, null, DateTime.UtcNow));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            var prior = await FindPostingOperationAsync(actor.MunicipalityId, request.ClientOperationId, ct);
            if (prior is not null) return await ResolveMobileObligationPriorAsync(prior, fingerprint, actor, ct);
            throw;
        }
        return Result<EcfPostOutcomeDto>.Failure(message, ResultStatus.Conflict);
    }
}
