using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// Bounded ECF application workflow. UtilityBill remains the assessment authority; one SaveChanges call
/// commits a successful post and its source/document/draft projections as a single EF transaction.
/// </summary>
public sealed class EcfCollectionWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality)
{
    private const string WebOrigin = "Web";
    private const int IntentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<Result<IReadOnlyList<EcfObligationQuoteDto>>> GetObligationsAsync(
        int year, int month, CancellationToken ct = default) => Run(async actor =>
    {
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            throw Problem("A valid billing year and month are required.", ResultStatus.Invalid);

        var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, PhilippineTime.Today, ct);
        var bills = await UtilityBillQuery(actor.MunicipalityId, tracked: false)
            .Where(x => x.BillingYear == year && x.BillingMonth == month)
            .OrderBy(x => x.Stall!.StallNo)
            .ToListAsync(ct);

        var quotes = new List<EcfObligationQuoteDto>(bills.Count);
        foreach (var bill in bills)
        {
            var facts = await BuildFactsAsync(bill, policy, actor.MunicipalityId, ct);
            if (facts.Quote.OutstandingAmount > 0m || facts.Quote.SettlementAuthority == SettlementAuthority.PendingCutover)
                quotes.Add(facts.Quote);
        }

        return Result<IReadOnlyList<EcfObligationQuoteDto>>.Success(quotes);
    }, ct);

    public Task<Result<EcfObligationQuoteDto>> GetObligationAsync(Guid utilityBillId, CancellationToken ct = default) => Run(async actor =>
    {
        var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, PhilippineTime.Today, ct);
        var bill = await UtilityBillQuery(actor.MunicipalityId, tracked: false).SingleOrDefaultAsync(x => x.Id == utilityBillId, ct);
        if (bill is null) return Result<EcfObligationQuoteDto>.NotFound();
        var facts = await BuildFactsAsync(bill, policy, actor.MunicipalityId, ct);
        return Result<EcfObligationQuoteDto>.Success(facts.Quote);
    }, ct);

    public Task<Result<IReadOnlyList<EcfAvailableDocumentDto>>> GetAvailableReceiptsAsync(CancellationToken ct = default) => Run(async actor =>
    {
        var documents = await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.MunicipalityId
                && x.InstrumentType == RevenueInstrumentType.OfficialReceipt
                && (x.State == AccountableDocumentState.InOffice
                    || (x.State == AccountableDocumentState.Assigned && x.AssignedUserId == actor.UserId)))
            .OrderBy(x => x.SerialNumber)
            .Select(x => new EcfAvailableDocumentDto(x.Id, x.DocumentNumber, x.State))
            .ToListAsync(ct);
        return Result<IReadOnlyList<EcfAvailableDocumentDto>>.Success(documents);
    }, ct);

    public Task<Result<EcfCollectionDraftDto>> GetCurrentDraftAsync(CancellationToken ct = default) => Run(async actor =>
    {
        var draft = await db.WebCollectionDrafts.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.MunicipalityId
                && x.OwnerUserId == actor.UserId && x.Status == CollectionDraftStatus.Draft)
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (draft is null) return Result<EcfCollectionDraftDto>.NotFound();
        return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));
    }, ct);

    public Task<Result<EcfCollectionDraftDto>> GetDraftAsync(Guid draftId, CancellationToken ct = default) => Run(async actor =>
    {
        var draft = await FindOwnedDraftAsync(draftId, actor.MunicipalityId, actor.UserId, tracked: false, ct);
        if (draft is null) return Result<EcfCollectionDraftDto>.NotFound();
        return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));
    }, ct);

    public Task<Result<EcfCollectionDraftDto>> CreateDraftAsync(
        CreateEcfCollectionDraftRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, PhilippineTime.Today, ct);
        var bill = await UtilityBillQuery(actor.MunicipalityId, tracked: false).SingleOrDefaultAsync(x => x.Id == request.UtilityBillId, ct);
        if (bill is null) return Result<EcfCollectionDraftDto>.NotFound();
        var facts = await BuildFactsAsync(bill, policy, actor.MunicipalityId, ct);
        EnsureDraftEligible(facts);
        ValidateProposedAmount(request.ProposedAmount, facts.Quote.OutstandingAmount);

        if (request.OneOffPayerName?.Trim().Length > 200)
            throw Problem("Payer name must not exceed 200 characters.", ResultStatus.Invalid);
        if (request.AccountableDocumentId.HasValue)
            await RequireSelectableReceiptAsync(request.AccountableDocumentId.Value, actor, tracked: false, ct);

        // An explicit contract-to-Payor link is authoritative. Otherwise retain the occupant evidence
        // from this exact billing-period occupancy, or a staff-entered one-off snapshot when none exists.
        var payerName = facts.Quote.PayorId.HasValue
            ? facts.Quote.PayerNameSnapshot
            : facts.Quote.PayerNameSnapshot ?? NormalizeOptional(request.OneOffPayerName);
        var draft = WebCollectionDraft.Create(
            actor.MunicipalityId, actor.UserId, PhilippineTime.Today,
            facts.Quote.PayorId, payerName, RevenueInstrumentType.OfficialReceipt,
            request.AccountableDocumentId, actor.Username);
        var line = WebCollectionDraftLine.Create(
            actor.MunicipalityId, draft.Id, 0,
            facts.Policy.RevenueClassificationId, facts.Policy.Id,
            request.ProposedAmount, facts.Policy.DisplayName,
            CollectionSourceKind.UtilityBill, bill.Id, CollectionSourcePart.Electricity,
            facts.SnapshotJson, actor.Username);
        var allocation = WebCollectionDraftAllocation.Create(
            actor.MunicipalityId, line.Id, CollectionSourceKind.UtilityBill,
            bill.Id, CollectionSourcePart.Electricity, request.ProposedAmount,
            facts.SnapshotJson, actor.Username);

        db.WebCollectionDrafts.Add(draft);
        db.WebCollectionDraftLines.Add(line);
        db.WebCollectionDraftAllocations.Add(allocation);
        await db.SaveChangesAsync(ct);
        return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));
    }, ct);

    public Task<Result<EcfCollectionDraftDto>> UpdateAllocationAsync(
        Guid draftId, UpdateEcfDraftAllocationRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        var draft = await FindOwnedDraftAsync(draftId, actor.MunicipalityId, actor.UserId, tracked: true, ct);
        if (draft is null) return Result<EcfCollectionDraftDto>.NotFound();
        EnsureExpectedRevision(draft, request.ExpectedRevision);
        var line = await SingleDraftLineAsync(actor.MunicipalityId, draft.Id, ct);
        var allocation = await SingleDraftAllocationAsync(actor.MunicipalityId, line.Id, ct);
        var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, draft.BusinessDate, ct);
        var bill = await UtilityBillQuery(actor.MunicipalityId, tracked: false).SingleOrDefaultAsync(x => x.Id == line.SourceId, ct);
        if (bill is null) return Result<EcfCollectionDraftDto>.NotFound();
        var facts = await BuildFactsAsync(bill, policy, actor.MunicipalityId, ct);
        EnsureDraftEligible(facts);
        ValidateProposedAmount(request.ProposedAmount, facts.Quote.OutstandingAmount);
        EnsureDraftPayorStillMatches(draft, facts.Quote);

        draft.AdvanceRevision(request.ExpectedRevision, actor.Username);
        line.UpdateFinancialTerms(facts.Policy.RevenueClassificationId, facts.Policy.Id,
            request.ProposedAmount, facts.SnapshotJson, actor.Username);
        allocation.UpdateAmountAndSnapshot(request.ProposedAmount, facts.SnapshotJson, actor.Username);
        await db.SaveChangesAsync(ct);
        return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));
    }, ct);

    public Task<Result<EcfCollectionDraftDto>> SelectDocumentAsync(
        Guid draftId, SelectEcfDraftDocumentRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        var draft = await FindOwnedDraftAsync(draftId, actor.MunicipalityId, actor.UserId, tracked: true, ct);
        if (draft is null) return Result<EcfCollectionDraftDto>.NotFound();
        EnsureExpectedRevision(draft, request.ExpectedRevision);
        if (draft.InstrumentFamily != RevenueInstrumentType.OfficialReceipt)
            throw Problem("This ECF draft is not an Official Receipt collection.", ResultStatus.Conflict);
        if (request.AccountableDocumentId.HasValue)
            await RequireSelectableReceiptAsync(request.AccountableDocumentId.Value, actor, tracked: false, ct);

        draft.SelectAccountableDocument(request.ExpectedRevision, request.AccountableDocumentId, actor.Username);
        await db.SaveChangesAsync(ct);
        return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));
    }, ct);

    public Task<Result<EcfCollectionDraftDto>> ReviewAsync(
        Guid draftId, EcfDraftRevisionRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        var draft = await FindOwnedDraftAsync(draftId, actor.MunicipalityId, actor.UserId, tracked: true, ct);
        if (draft is null) return Result<EcfCollectionDraftDto>.NotFound();
        EnsureExpectedRevision(draft, request.ExpectedRevision);
        var line = await SingleDraftLineAsync(actor.MunicipalityId, draft.Id, ct);
        var allocation = await SingleDraftAllocationAsync(actor.MunicipalityId, line.Id, ct);
        var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, draft.BusinessDate, ct);
        var bill = await UtilityBillQuery(actor.MunicipalityId, tracked: false).SingleOrDefaultAsync(x => x.Id == line.SourceId, ct);
        if (bill is null) return Result<EcfCollectionDraftDto>.NotFound();
        var facts = await BuildFactsAsync(bill, policy, actor.MunicipalityId, ct);
        EnsureDraftEligible(facts);
        EnsureDraftPayorStillMatches(draft, facts.Quote);
        EnsureLineMatchesCurrentSource(line, allocation, facts);
        ValidateProposedAmount(line.Amount, facts.Quote.OutstandingAmount);
        if (!draft.AccountableDocumentId.HasValue)
            throw Problem("Select an available Official Receipt before review.", ResultStatus.Invalid);
        var document = await RequireSelectableReceiptAsync(draft.AccountableDocumentId.Value, actor, tracked: false, ct);
        if (draft.InstrumentFamily != RevenueInstrumentType.OfficialReceipt)
            throw Problem("ECF policy requires an Official Receipt.", ResultStatus.Conflict);

        var normalizedIntent = NormalizeIntent(draft, line, allocation, document.DocumentNumber,
            facts.Snapshot.SourceVersion);
        draft.Review(request.ExpectedRevision, FingerprintFinancialContent(normalizedIntent),
            actor.UserId, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
        return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));
    }, ct);

    public Task<Result<EcfCollectionDraftDto>> DiscardAsync(
        Guid draftId, EcfDraftRevisionRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        var draft = await FindOwnedDraftAsync(draftId, actor.MunicipalityId, actor.UserId, tracked: true, ct);
        if (draft is null) return Result<EcfCollectionDraftDto>.NotFound();
        EnsureExpectedRevision(draft, request.ExpectedRevision);
        draft.Discard(request.ExpectedRevision, actor.Username);
        await db.SaveChangesAsync(ct);
        return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));
    }, ct);

    public Task<Result<EcfPostOutcomeDto>> PostAsync(
        Guid draftId, PostEcfCollectionDraftRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        if (request.ClientOperationId == Guid.Empty)
            throw Problem("A valid ClientOperationId is required.", ResultStatus.Invalid);
        var draft = await FindOwnedDraftAsync(draftId, actor.MunicipalityId, actor.UserId, tracked: true, ct);
        if (draft is null) return Result<EcfPostOutcomeDto>.NotFound();
        var line = await SingleDraftLineAsync(actor.MunicipalityId, draft.Id, ct);
        var allocation = await SingleDraftAllocationAsync(actor.MunicipalityId, line.Id, ct);
        var selectedDocument = draft.AccountableDocumentId is { } selectedId
            ? await db.AccountableDocuments.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == selectedId && x.MunicipalityId == actor.MunicipalityId, ct)
            : null;
        var normalizedIntent = NormalizeIntent(draft, line, allocation,
            selectedDocument?.DocumentNumber, ReadSnapshot(line.CalculationSnapshot).SourceVersion);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(
            IntentVersion, normalizedIntent, WebOrigin, ActorIdentity(actor));

        var prior = await FindPostingOperationAsync(actor.MunicipalityId, request.ClientOperationId, ct);
        if (prior is not null)
            return await ResolvePriorOperationAsync(prior, fingerprint, actor, ct);

        // A new key cannot turn an already-posted DraftId into another Collection.
        if (draft.Status == CollectionDraftStatus.Posted && draft.CollectionId is { } postedCollectionId)
            return Result<EcfPostOutcomeDto>.Success(await BuildPostOutcomeAsync(
                actor.MunicipalityId, postedCollectionId, true, ct));
        if (draft.Status != CollectionDraftStatus.Draft)
            throw Problem("This collection draft is no longer available for posting.", ResultStatus.Conflict);

        EnsureExpectedRevision(draft, request.ExpectedRevision);
        if (!draft.IsReviewedForCurrentRevision
            || draft.ReviewedFingerprint != FingerprintFinancialContent(normalizedIntent))
            throw Problem("This exact draft revision has not been reviewed. Review it again before posting.", ResultStatus.Conflict);

        if (!draft.AccountableDocumentId.HasValue || selectedDocument is null)
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "OR_UNAVAILABLE", "Select a valid available Official Receipt and review again.", ct);

        var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, draft.BusinessDate, ct);
        var source = await UtilityBillQuery(actor.MunicipalityId, tracked: true).SingleOrDefaultAsync(x => x.Id == line.SourceId, ct);
        if (source is null) return Result<EcfPostOutcomeDto>.NotFound();
        var facts = await BuildFactsAsync(source, policy, actor.MunicipalityId, ct);
        var draftSnapshot = ReadSnapshot(line.CalculationSnapshot);

        if (source.ElectricitySettlementAuthorityState == SettlementAuthority.Legacy)
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "ECF_SOURCE_LEGACY",
                "ECF remains on Legacy settlement. Canonical posting is blocked until the controlled source cutover is complete.", ct);
        if (source.ElectricitySettlementAuthorityState != SettlementAuthority.Canonical)
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "ECF_SOURCE_CUTOVER_PENDING",
                "ECF source is quiesced for reconciliation and cannot be posted.", ct);
        if (draftSnapshot.SourceVersion != facts.Snapshot.SourceVersion
            || line.RevenueClassificationId != facts.Policy.RevenueClassificationId
            || line.RevenueClassificationPolicyId != facts.Policy.Id
            || facts.Policy.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt)
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "ECF_REVIEW_REQUIRED",
                "ECF source or policy changed after review. Refresh the allocation and review the new revision.", ct);
        if (draft.AccountableDocumentId != selectedDocument.Id
            || selectedDocument.InstrumentType != RevenueInstrumentType.OfficialReceipt
            || selectedDocument.State is not (AccountableDocumentState.InOffice or AccountableDocumentState.Assigned)
            || (selectedDocument.State == AccountableDocumentState.Assigned && selectedDocument.AssignedUserId != actor.UserId))
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "OR_UNAVAILABLE",
                "The selected Official Receipt is unavailable or assigned to another staff member.", ct);
        if (draft.InstrumentFamily != RevenueInstrumentType.OfficialReceipt
            || allocation.SourceKind != CollectionSourceKind.UtilityBill
            || allocation.SourceId != source.Id
            || allocation.SourcePart != CollectionSourcePart.Electricity
            || allocation.Amount != line.Amount
            || line.SourceKind != CollectionSourceKind.UtilityBill
            || line.SourcePart != CollectionSourcePart.Electricity
            || line.SourceId != source.Id)
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "ECF_INTENT_INVALID",
                "The reviewed draft no longer matches the ECF Electricity allocation and Official Receipt policy.", ct);
        if (line.Amount <= 0m || line.Amount > facts.Quote.OutstandingAmount)
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "ECF_OUTSTANDING_CHANGED",
                "The ECF outstanding amount changed. Refresh the allocation and review the new revision.", ct);
        EnsureDraftPayorStillMatches(draft, facts.Quote);

        var document = await RequireSelectableReceiptAsync(draft.AccountableDocumentId.Value, actor, tracked: true, ct);
        var now = DateTime.UtcNow;
        var collection = Collection.Post(
            draft.BusinessDate, now, ActorIdentity(actor), actor.Username, actor.Role,
            [new CollectionLineDraft(facts.Classification, facts.Policy, line.Amount,
                CollectionSourceKind.UtilityBill, source.Id, CollectionSourcePart.Electricity,
                line.CalculationSnapshot,
                [new CollectionAllocationDraft(CollectionSourceKind.UtilityBill, source.Id,
                    allocation.Amount, CollectionSourcePart.Electricity, allocation.SourceSnapshot)])],
            payerName: draft.PayerNameSnapshot, clientOperationId: request.ClientOperationId,
            payorId: draft.PayorId);
        document.Consume(collection.Id, request.ClientOperationId, now, actor.Username);
        source.ApplyCanonicalElectricityProjection(
            facts.Quote.CumulativeSettledEvidence + allocation.Amount,
            document.DocumentNumber, now, actor.Username);
        draft.MarkPosted(request.ExpectedRevision, collection.Id, actor.Username);
        var operation = PostingOperation.Record(actor.MunicipalityId, request.ClientOperationId,
            IntentVersion, normalizedIntent, WebOrigin, ActorIdentity(actor), PostingOperationStatus.Succeeded,
            null, null, collection.Id, document.Id, now);

        db.Collections.Add(collection);
        db.PostingOperations.Add(operation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var winner = await FindPostingOperationAsync(actor.MunicipalityId, request.ClientOperationId, ct);
            if (winner is not null)
                return await ResolvePriorOperationAsync(winner, fingerprint, actor, ct);
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "POST_CONCURRENCY_CONFLICT",
                "The ECF source, draft, or Official Receipt changed while posting. Reload and review again.", ct);
        }
        catch (DbUpdateException)
        {
            var winner = await FindPostingOperationAsync(actor.MunicipalityId, request.ClientOperationId, ct);
            if (winner is not null)
                return await ResolvePriorOperationAsync(winner, fingerprint, actor, ct);
            throw Problem("The Official Receipt or ECF source was consumed by another transaction. Reload and review again.", ResultStatus.Conflict);
        }

        return Result<EcfPostOutcomeDto>.Success(new EcfPostOutcomeDto(
            collection.Id, document.DocumentNumber, "Posted", collection.TotalAmount,
            collection.Lines.Count, false));
    }, ct);

    public Task<Result<IReadOnlyList<EcfCollectionActivityDto>>> GetActivityAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default) => Run(async actor =>
    {
        if (from > to || to.DayNumber - from.DayNumber > 366)
            throw Problem("Choose a valid collection period of no more than 367 days.", ResultStatus.Invalid);
        var classification = await db.RevenueClassifications.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SemanticCode == RevenueClassificationCodes.Ecf
                && x.MunicipalityId == actor.MunicipalityId, ct);
        if (classification is null)
            return Result<IReadOnlyList<EcfCollectionActivityDto>>.Success([]);

        var lines = await db.CollectionLines.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.MunicipalityId
                && x.RevenueClassificationId == classification.Id
                && x.SourceKind == CollectionSourceKind.UtilityBill
                && x.SourcePart == CollectionSourcePart.Electricity)
            .ToListAsync(ct);
        var groupedIds = lines.Select(x => x.CollectionId).Distinct().ToArray();
        var collections = await db.Collections.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.MunicipalityId
                && groupedIds.Contains(x.Id) && x.BusinessDate >= from && x.BusinessDate <= to)
            .ToListAsync(ct);
        var collectionMap = collections.ToDictionary(x => x.Id);
        var includedLines = lines.Where(x => collectionMap.ContainsKey(x.CollectionId)).ToList();
        var ids = collectionMap.Keys.ToArray();
        var docs = await db.AccountableDocuments.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.MunicipalityId
                && x.CollectionId.HasValue && ids.Contains(x.CollectionId.Value))
            .ToListAsync(ct);
        var correctionRows = await db.CollectionCorrections.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.MunicipalityId && ids.Contains(x.OriginalCollectionId))
            .Select(x => new { x.OriginalCollectionId, x.CorrectionType, x.FinancialEffectAmount })
            .ToListAsync(ct);
        var policyIds = includedLines.Select(x => x.RevenueClassificationPolicyId).Distinct().ToArray();
        var names = await db.RevenueClassificationPolicies.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.MunicipalityId && policyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var activity = includedLines.GroupBy(x => x.CollectionId).Select(group =>
        {
            var collection = collectionMap[group.Key];
            var correction = correctionRows.Where(x => x.OriginalCollectionId == collection.Id).ToList();
            var disposition = correction.Any(x => x.FinancialEffectAmount < 0m)
                ? "Reversed"
                : correction.Any(x => x.CorrectionType == CollectionCorrectionType.DocumentCorrection)
                    ? "Document corrected" : "Posted";
            var detail = group.Select(line =>
            {
                var snapshot = ReadSnapshot(line.CalculationSnapshot);
                return new EcfCollectionActivityLineDto(
                    names.GetValueOrDefault(line.RevenueClassificationPolicyId, "ECF"), line.Amount,
                    snapshot.BillingYear, snapshot.BillingMonth, "UtilityBill / Electricity",
                    line.CalculationSnapshot);
            }).ToList();
            return new EcfCollectionActivityDto(collection.Id, collection.BusinessDate,
                collection.RecordedAtUtc,
                docs.FirstOrDefault(x => x.CollectionId == collection.Id)?.DocumentNumber ?? "OR unavailable",
                collection.PayerName, collection.TotalAmount, detail.Count, disposition, detail);
        }).OrderByDescending(x => x.RecordedAtUtc).ToList();

        return Result<IReadOnlyList<EcfCollectionActivityDto>>.Success(activity);
    }, ct);

    private async Task<Result<T>> Run<T>(Func<Actor, Task<Result<T>>> action, CancellationToken ct)
    {
        if (!TryGetActor(out var actor, out var authFailure))
            return authFailure == ResultStatus.Unauthorized ? Result<T>.Unauthorized() : Result<T>.Forbidden();
        try
        {
            return await action(actor);
        }
        catch (WorkflowProblem problem)
        {
            return Result<T>.Failure(problem.Message, problem.Status);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<T>.Failure("The draft, ECF source, or document changed concurrently. Reload before continuing.", ResultStatus.Conflict);
        }
        catch (DbUpdateException)
        {
            return Result<T>.Failure("The ECF draft or document conflicts with another saved change.", ResultStatus.Conflict);
        }
    }

    private bool TryGetActor(out Actor actor, out ResultStatus failure)
    {
        actor = default!;
        failure = ResultStatus.Unauthorized;
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty)
            return false;
        if (currentUser.Role is not ("SuperAdmin" or "Admin"))
        {
            failure = ResultStatus.Forbidden;
            return false;
        }
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
        {
            failure = ResultStatus.Forbidden;
            return false;
        }
        actor = new Actor(userId, tenantId, currentUser.Username ?? "Office User", currentUser.Role);
        return true;
    }

    private async Task<PolicyFacts> ResolveEcfPolicyAsync(Guid tenantId, DateOnly businessDate, CancellationToken ct)
    {
        var classification = await db.RevenueClassifications
            .SingleOrDefaultAsync(x => x.SemanticCode == RevenueClassificationCodes.Ecf
                && x.MunicipalityId == tenantId && x.IsActive, ct);
        if (classification is null)
            throw Problem("ECF has no active revenue classification for this tenant.", ResultStatus.Conflict);
        var policy = await db.RevenueClassificationPolicies
            .Where(x => x.MunicipalityId == tenantId && x.RevenueClassificationId == classification.Id
                && x.EffectiveDate <= businessDate)
            .OrderByDescending(x => x.EffectiveDate)
            .FirstOrDefaultAsync(ct);
        if (policy?.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt)
            throw Problem("The active ECF policy does not approve Official Receipt collection.", ResultStatus.Conflict);
        return new PolicyFacts(classification, policy);
    }

    private IQueryable<UtilityBill> UtilityBillQuery(Guid tenantId, bool tracked)
    {
        IQueryable<UtilityBill> query = db.UtilityBills.Where(x => x.MunicipalityId == tenantId)
            .Include(x => x.Stall!).ThenInclude(x => x.Facility)
            .Include(x => x.Stall!).ThenInclude(x => x.Contracts).ThenInclude(x => x.Payor);
        return tracked ? query : query.AsNoTracking();
    }

    private async Task<SourceFacts> BuildFactsAsync(
        UtilityBill bill, PolicyFacts policy, Guid tenantId, CancellationToken ct)
    {
        if (bill.MunicipalityId != tenantId || bill.Stall is null)
            throw Problem("The ECF source has no accessible stall context in this tenant.", ResultStatus.Conflict);
        if (bill.Stall.Facility?.Code != FacilityCode.NPM)
            throw Problem("This utility bill is not an NPM Electricity source.", ResultStatus.Conflict);

        var settled = bill.ElecAmountPaid;
        if (bill.ElectricitySettlementAuthorityState == SettlementAuthority.Canonical)
        {
            if (bill.ElectricitySettlementCutoverId is not { } cutoverId)
                throw Problem("Canonical ECF source has no frozen cutover evidence.", ResultStatus.Conflict);
            var cutover = await db.CollectionSettlementCutovers.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == cutoverId
                    && x.MunicipalityId == tenantId
                    && x.SourceKind == CollectionSourceKind.UtilityBill
                    && x.SourceId == bill.Id
                    && x.SourcePart == CollectionSourcePart.Electricity, ct);
            if (cutover is null)
                throw Problem("Canonical ECF source cutover evidence does not match the source.", ResultStatus.Conflict);
            var sourceAllocations = db.CollectionAllocations.AsNoTracking().Where(x =>
                x.MunicipalityId == tenantId && x.SourceKind == CollectionSourceKind.UtilityBill
                && x.SourceId == bill.Id && x.SourcePart == CollectionSourcePart.Electricity);
            var allocated = await sourceAllocations.Select(x => (decimal?)x.Amount).SumAsync(ct) ?? 0m;
            var allocationIds = sourceAllocations.Select(x => x.Id);
            var corrected = await db.CollectionCorrectionAllocations.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId
                    && allocationIds.Contains(x.OriginalAllocationId))
                .Select(x => (decimal?)x.FinancialEffectAmount).SumAsync(ct) ?? 0m;
            settled = cutover.OpeningLegacySettledAmount + allocated + corrected;
        }

        var outstanding = Math.Max(0m, bill.ElecCharge - settled);
        var occupancy = bill.Stall.OccupancyAnsweringForMonth(
            bill.BillingYear, bill.BillingMonth, PhilippineTime.Today);
        var contract = occupancy?.Contract;
        var linkedPayor = contract?.PayorId is not null ? contract.Payor : null;
        Guid? payorId = linkedPayor?.MunicipalityId == tenantId ? linkedPayor.Id : null;
        var payerName = linkedPayor?.DisplayName
            ?? (string.IsNullOrWhiteSpace(contract?.ActualOccupant) ? null : contract.ActualOccupant.Trim());
        var facility = bill.Stall.Facility!;
        var section = ResolveSection(facility, bill.Stall);
        var snapshot = new EcfSourceSnapshot(
            1, bill.Id, bill.ElectricitySourceVersion, bill.StallId, bill.Stall.StallNo,
            facility.Id, facility.Name, section, bill.BillingYear, bill.BillingMonth,
            bill.ElecPreviousReading, bill.ElecCurrentReading, bill.ElecConsumption,
            bill.ElecRatePerKwh, bill.ElecCharge, settled, outstanding,
            policy.Classification.Id, policy.Policy.Id, policy.Policy.DisplayName,
            RevenueInstrumentType.OfficialReceipt, "Metered");
        var canAdd = outstanding > 0m && bill.ElectricitySettlementAuthorityState != SettlementAuthority.PendingCutover;
        var quote = new EcfObligationQuoteDto(
            bill.MunicipalityId, bill.Id, CollectionSourceKind.UtilityBill, CollectionSourcePart.Electricity,
            bill.ElectricitySourceVersion, bill.StallId, bill.Stall.StallNo,
            facility.Name, section, bill.BillingYear, bill.BillingMonth,
            bill.ElecPreviousReading, bill.ElecCurrentReading, bill.ElecConsumption,
            bill.ElecRatePerKwh, bill.ElecCharge, settled, outstanding,
            bill.ElectricitySettlementAuthorityState, payorId, payerName,
            policy.Classification.Id, policy.Policy.Id, policy.Policy.DisplayName,
            RevenueInstrumentType.OfficialReceipt, "Metered", canAdd,
            bill.ElectricitySettlementAuthorityState == SettlementAuthority.Canonical && canAdd);
        return new SourceFacts(bill, policy.Classification, policy.Policy, quote, snapshot,
            JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    private async Task<WebCollectionDraft?> FindOwnedDraftAsync(
        Guid id, Guid tenantId, Guid ownerId, bool tracked, CancellationToken ct)
    {
        if (id == Guid.Empty) return null;
        IQueryable<WebCollectionDraft> query = db.WebCollectionDrafts.Where(x =>
            x.Id == id && x.MunicipalityId == tenantId && x.OwnerUserId == ownerId);
        if (!tracked) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(ct);
    }

    private async Task<WebCollectionDraftLine> SingleDraftLineAsync(Guid tenantId, Guid draftId, CancellationToken ct)
    {
        var lines = await db.WebCollectionDraftLines.Where(x =>
                x.MunicipalityId == tenantId && x.DraftId == draftId)
            .OrderBy(x => x.LineOrder).ToListAsync(ct);
        if (lines.Count != 1)
            throw Problem("The ECF draft must contain exactly one Electricity line.", ResultStatus.Conflict);
        return lines[0];
    }

    private async Task<WebCollectionDraftAllocation> SingleDraftAllocationAsync(
        Guid tenantId, Guid lineId, CancellationToken ct)
    {
        var allocations = await db.WebCollectionDraftAllocations.Where(x =>
            x.MunicipalityId == tenantId && x.DraftLineId == lineId).ToListAsync(ct);
        if (allocations.Count != 1)
            throw Problem("The ECF line must have exactly one explicit Electricity allocation.", ResultStatus.Conflict);
        return allocations[0];
    }

    private async Task<EcfCollectionDraftDto> ToDraftDtoAsync(WebCollectionDraft draft, CancellationToken ct)
    {
        var lines = await db.WebCollectionDraftLines.AsNoTracking()
            .Where(x => x.MunicipalityId == draft.MunicipalityId && x.DraftId == draft.Id)
            .OrderBy(x => x.LineOrder).ToListAsync(ct);
        var lineIds = lines.Select(x => x.Id).ToArray();
        var allocations = await db.WebCollectionDraftAllocations.AsNoTracking()
            .Where(x => x.MunicipalityId == draft.MunicipalityId && lineIds.Contains(x.DraftLineId)).ToListAsync(ct);
        var document = draft.AccountableDocumentId is { } documentId
            ? await db.AccountableDocuments.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == documentId && x.MunicipalityId == draft.MunicipalityId, ct)
            : null;
        var dtoLines = lines.Select(line =>
        {
            var snapshot = ReadSnapshot(line.CalculationSnapshot);
            var allocationAmount = allocations.Where(x => x.DraftLineId == line.Id).Sum(x => x.Amount);
            return new EcfCollectionDraftLineDto(line.Id, line.Amount,
                line.Description ?? snapshot.ClassificationName,
                snapshot.UtilityBillId, CollectionSourcePart.Electricity,
                snapshot.SourceVersion, allocationAmount);
        }).ToList();
        return new EcfCollectionDraftDto(draft.Id, draft.Revision, draft.BusinessDate,
            draft.Status.ToString(), draft.IsReviewedForCurrentRevision,
            draft.ReviewedAtUtc, draft.PayerNameSnapshot, draft.PayorId,
            draft.AccountableDocumentId, document?.DocumentNumber,
            dtoLines.Sum(x => x.Amount), draft.CollectionId, dtoLines);
    }

    private async Task<AccountableDocument> RequireSelectableReceiptAsync(
        Guid documentId, Actor actor, bool tracked, CancellationToken ct)
    {
        if (documentId == Guid.Empty)
            throw Problem("Select a valid Official Receipt document.", ResultStatus.Invalid);
        IQueryable<AccountableDocument> query = db.AccountableDocuments.Where(x =>
            x.Id == documentId && x.MunicipalityId == actor.MunicipalityId);
        if (!tracked) query = query.AsNoTracking();
        var document = await query.SingleOrDefaultAsync(ct);
        if (document is null) throw Problem("Official Receipt document was not found in this tenant.", ResultStatus.NotFound);
        if (document.InstrumentType != RevenueInstrumentType.OfficialReceipt
            || document.State is not (AccountableDocumentState.InOffice or AccountableDocumentState.Assigned)
            || (document.State == AccountableDocumentState.Assigned && document.AssignedUserId != actor.UserId))
            throw Problem("Official Receipt document is unavailable or assigned to another staff member.", ResultStatus.Conflict);
        return document;
    }

    private async Task<PostingOperation?> FindPostingOperationAsync(Guid tenantId, Guid operationId, CancellationToken ct) =>
        await db.PostingOperations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.ClientOperationId == operationId, ct);

    private async Task<Result<EcfPostOutcomeDto>> ResolvePriorOperationAsync(
        PostingOperation prior, string expectedFingerprint, Actor actor, CancellationToken ct)
    {
        if (prior.Origin != WebOrigin || prior.ActorId != ActorIdentity(actor)
            || prior.IntentFingerprint != expectedFingerprint)
            return Result<EcfPostOutcomeDto>.Failure(
                "IDEMPOTENCY CONFLICT: this ClientOperationId is already bound to a different posting intent or actor.",
                ResultStatus.Conflict);
        if (prior.Status != PostingOperationStatus.Succeeded || prior.CollectionId is not { } collectionId)
            return Result<EcfPostOutcomeDto>.Failure(
                prior.OutcomeDetails ?? prior.OutcomeCode ?? "The original posting attempt was rejected.",
                ResultStatus.Conflict);
        return Result<EcfPostOutcomeDto>.Success(await BuildPostOutcomeAsync(
            actor.MunicipalityId, collectionId, true, ct));
    }

    private async Task<Result<EcfPostOutcomeDto>> RecordRejectionAsync(
        Actor actor, Guid operationId, string normalizedIntent, Guid? documentId,
        string code, string message, CancellationToken ct)
    {
        var record = PostingOperation.Record(actor.MunicipalityId, operationId,
            IntentVersion, normalizedIntent, WebOrigin, ActorIdentity(actor),
            PostingOperationStatus.Rejected, code, message, null, documentId, DateTime.UtcNow);
        db.PostingOperations.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            var prior = await FindPostingOperationAsync(actor.MunicipalityId, operationId, ct);
            if (prior is not null)
                return await ResolvePriorOperationAsync(prior,
                    PostingOperation.ComputeIntentFingerprint(IntentVersion, normalizedIntent, WebOrigin, ActorIdentity(actor)), actor, ct);
            throw Problem("The ECF source or draft changed while the rejected attempt was being recorded.", ResultStatus.Conflict);
        }
        catch (DbUpdateException)
        {
            var prior = await FindPostingOperationAsync(actor.MunicipalityId, operationId, ct);
            if (prior is not null)
                return await ResolvePriorOperationAsync(prior,
                    PostingOperation.ComputeIntentFingerprint(IntentVersion, normalizedIntent, WebOrigin, ActorIdentity(actor)), actor, ct);
            throw Problem("This ClientOperationId is already in use.", ResultStatus.Conflict);
        }
        return Result<EcfPostOutcomeDto>.Failure(message, ResultStatus.Conflict);
    }

    private async Task<EcfPostOutcomeDto> BuildPostOutcomeAsync(
        Guid tenantId, Guid collectionId, bool replay, CancellationToken ct)
    {
        var collection = await db.Collections.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == collectionId && x.MunicipalityId == tenantId, ct)
            ?? throw Problem("Posted Collection outcome was not found.", ResultStatus.NotFound);
        var document = await db.AccountableDocuments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.CollectionId == collection.Id, ct);
        var corrections = await db.CollectionCorrections.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.OriginalCollectionId == collection.Id)
            .Select(x => new { x.CorrectionType, x.FinancialEffectAmount }).ToListAsync(ct);
        var disposition = corrections.Any(x => x.FinancialEffectAmount < 0m) ? "Reversed"
            : corrections.Any(x => x.CorrectionType == CollectionCorrectionType.DocumentCorrection)
                ? "Document corrected" : "Posted";
        return new EcfPostOutcomeDto(collection.Id, document?.DocumentNumber ?? "OR unavailable",
            disposition, collection.TotalAmount, await db.CollectionLines.CountAsync(
                x => x.MunicipalityId == tenantId && x.CollectionId == collection.Id, ct), replay);
    }

    private async Task<Result<EcfPostOutcomeDto>> RejectOrResolveExistingAsync(
        Actor actor, Guid operationId, string normalized, Guid? docId,
        string code, string message, CancellationToken ct)
    {
        var prior = await FindPostingOperationAsync(actor.MunicipalityId, operationId, ct);
        if (prior is not null)
            return await ResolvePriorOperationAsync(prior,
                PostingOperation.ComputeIntentFingerprint(IntentVersion, normalized, WebOrigin, ActorIdentity(actor)), actor, ct);
        return await RecordRejectionAsync(actor, operationId, normalized, docId, code, message, ct);
    }

    private static void EnsureDraftEligible(SourceFacts facts)
    {
        if (facts.Quote.SettlementAuthority == SettlementAuthority.PendingCutover)
            throw Problem("This ECF source is quiesced for cutover reconciliation and cannot accept new draft settlement.", ResultStatus.Conflict);
        if (facts.Quote.OutstandingAmount <= 0m)
            throw Problem("This ECF source has no outstanding amount.", ResultStatus.Conflict);
    }

    private static void EnsureLineMatchesCurrentSource(
        WebCollectionDraftLine line, WebCollectionDraftAllocation allocation, SourceFacts current)
    {
        var frozen = ReadSnapshot(line.CalculationSnapshot);
        if (line.SourceKind != CollectionSourceKind.UtilityBill
            || line.SourcePart != CollectionSourcePart.Electricity
            || line.SourceId != current.Bill.Id
            || allocation.SourceKind != CollectionSourceKind.UtilityBill
            || allocation.SourcePart != CollectionSourcePart.Electricity
            || allocation.SourceId != current.Bill.Id
            || allocation.Amount != line.Amount
            || frozen.SourceVersion != current.Snapshot.SourceVersion
            || line.RevenueClassificationId != current.Policy.RevenueClassificationId
            || line.RevenueClassificationPolicyId != current.Policy.Id)
            throw Problem("ECF source facts changed. Refresh the allocation and review the new draft revision.", ResultStatus.Conflict);
    }

    private static void EnsureDraftPayorStillMatches(WebCollectionDraft draft, EcfObligationQuoteDto quote)
    {
        if (draft.PayorId != quote.PayorId
            || (quote.PayerNameSnapshot is not null && draft.PayerNameSnapshot != quote.PayerNameSnapshot))
            throw Problem("The ECF payer context changed. Refresh the draft and review the updated payer evidence.", ResultStatus.Conflict);
    }

    private static void EnsureExpectedRevision(WebCollectionDraft draft, long expected)
    {
        if (draft.Status != CollectionDraftStatus.Draft)
            throw Problem("Only an unposted, undiscarded draft can be changed.", ResultStatus.Conflict);
        if (draft.Revision != expected)
            throw Problem("STALE DRAFT: reload the latest revision before continuing.", ResultStatus.Conflict);
    }

    private static void ValidateProposedAmount(decimal amount, decimal outstanding)
    {
        if (amount <= 0m || decimal.Round(amount, 2, MidpointRounding.ToZero) != amount)
            throw Problem("Enter a positive collection amount in whole centavos.", ResultStatus.Invalid);
        if (amount > outstanding)
            throw Problem("The proposed ECF allocation exceeds the current Electricity outstanding amount.", ResultStatus.Conflict);
    }

    private static string NormalizeIntent(
        WebCollectionDraft draft, WebCollectionDraftLine line,
        WebCollectionDraftAllocation allocation, string? documentNumber, long sourceVersion)
    {
        var intent = new NormalizedEcfIntent(
            IntentVersion, draft.MunicipalityId, draft.Id, draft.OwnerUserId, draft.BusinessDate,
            draft.PayorId, draft.PayerNameSnapshot, draft.InstrumentFamily,
            line.RevenueClassificationId, line.RevenueClassificationPolicyId,
            line.Amount, line.SourceKind, line.SourceId, line.SourcePart, sourceVersion,
            allocation.SourceKind, allocation.SourceId, allocation.SourcePart, allocation.Amount,
            draft.AccountableDocumentId, documentNumber);
        return JsonSerializer.Serialize(intent, JsonOptions);
    }

    private static string FingerprintFinancialContent(string normalizedIntent) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedIntent)));

    private static EcfSourceSnapshot ReadSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw Problem("The draft has no server-generated ECF source snapshot.", ResultStatus.Conflict);
        try
        {
            return JsonSerializer.Deserialize<EcfSourceSnapshot>(json, JsonOptions)
                ?? throw Problem("The ECF source snapshot is invalid.", ResultStatus.Conflict);
        }
        catch (JsonException)
        {
            throw Problem("The ECF source snapshot is invalid.", ResultStatus.Conflict);
        }
    }

    private static string ResolveSection(Facility facility, Stall stall)
    {
        if (stall.Section is { } section)
        {
            var custom = facility.SectionLabel(section);
            return !string.IsNullOrWhiteSpace(custom) ? custom
                : section switch
                {
                    MarketSection.VegetableArea => "Vegetable Area",
                    MarketSection.FishSection => "Fish Area",
                    MarketSection.MeatSection => "Meat Area",
                    _ => string.Empty
                };
        }
        return stall.CustomSectionName ?? string.Empty;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ActorIdentity(Actor actor) => actor.UserId.ToString("N");
    private static WorkflowProblem Problem(string message, ResultStatus status) => new(message, status);

    private sealed record Actor(Guid UserId, Guid MunicipalityId, string Username, string Role);
    private sealed record PolicyFacts(RevenueClassification Classification, RevenueClassificationPolicy Policy);
    private sealed record SourceFacts(
        UtilityBill Bill, RevenueClassification Classification, RevenueClassificationPolicy Policy,
        EcfObligationQuoteDto Quote, EcfSourceSnapshot Snapshot, string SnapshotJson);
    private sealed record EcfSourceSnapshot(
        int SchemaVersion, Guid UtilityBillId, long SourceVersion, Guid StallId, string StallNo,
        Guid FacilityId, string FacilityName, string Section, int BillingYear, int BillingMonth,
        decimal PreviousReading, decimal CurrentReading, decimal Consumption, decimal RatePerKwh,
        decimal AssessedAmount, decimal CumulativeSettledEvidence, decimal OutstandingAmount,
        Guid ClassificationId, Guid PolicyId, string ClassificationName,
        RevenueInstrumentType Instrument, string ChargeBasis);
    private sealed record NormalizedEcfIntent(
        int SchemaVersion, Guid MunicipalityId, Guid DraftId, Guid OwnerUserId, DateOnly BusinessDate,
        Guid? PayorId, string? PayerName, RevenueInstrumentType? Instrument,
        Guid ClassificationId, Guid PolicyId, decimal LineAmount,
        CollectionSourceKind? LineSourceKind, Guid? LineSourceId, CollectionSourcePart? LineSourcePart,
        long SourceVersion, CollectionSourceKind AllocationSourceKind, Guid AllocationSourceId,
        CollectionSourcePart? AllocationSourcePart, decimal AllocationAmount,
        Guid? AccountableDocumentId, string? DocumentNumber);

    private sealed class WorkflowProblem(string message, ResultStatus status) : Exception(message)
    {
        public ResultStatus Status { get; } = status;
    }
}
