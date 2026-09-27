using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
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
/// Shared Office Collection composer and posting coordinator. Specialized source adapters remain responsible for
/// authoritative assessments; one SaveChanges call commits all compatible lines, allocations, source projections,
/// accountable-document consumption, durable operation outcome, and draft disposition atomically.
/// </summary>
public sealed class CollectionComposerWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock? clock = null)
{
    private const string WebOrigin = "Web";
    private const int IntentVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions IntentJsonOptions = CreateIntentJsonOptions();
    private DateOnly BusinessToday => clock?.PhilippineToday ?? PhilippineTime.Today;

    public Task<Result<IReadOnlyList<EcfObligationQuoteDto>>> GetObligationsAsync(
        int year, int month, CancellationToken ct = default) => Run(async actor =>
    {
        if (year is < 2000 or > 2200 || month is < 1 or > 12)
            throw Problem("A valid billing year and month are required.", ResultStatus.Invalid);

        var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, BusinessToday, ct);
        var bills = await UtilityBillQuery(actor.MunicipalityId, tracked: false)
            .Where(x => x.BillingYear == year && x.BillingMonth == month)
            .OrderBy(x => x.Stall!.StallNo)
            .ToListAsync(ct);

        var quotes = new List<EcfObligationQuoteDto>(bills.Count);
        foreach (var bill in bills)
        {
            var facts = await BuildFactsAsync(bill, policy, actor.MunicipalityId, BusinessToday, ct);
            if (facts.Quote.OutstandingAmount > 0m || facts.Quote.SettlementAuthority == SettlementAuthority.PendingCutover)
                quotes.Add(facts.Quote);
        }

        return Result<IReadOnlyList<EcfObligationQuoteDto>>.Success(quotes);
    }, ct);

    public Task<Result<EcfObligationQuoteDto>> GetObligationAsync(Guid utilityBillId, CancellationToken ct = default) => Run(async actor =>
    {
        var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, BusinessToday, ct);
        var bill = await UtilityBillQuery(actor.MunicipalityId, tracked: false).SingleOrDefaultAsync(x => x.Id == utilityBillId, ct);
        if (bill is null) return Result<EcfObligationQuoteDto>.NotFound();
        var facts = await BuildFactsAsync(bill, policy, actor.MunicipalityId, BusinessToday, ct);
        return Result<EcfObligationQuoteDto>.Success(facts.Quote);
    }, ct);

    public Task<Result<RentObligationQuoteDto>> GetRentObligationAsync(
        Guid stallId, int year, int month, CancellationToken ct = default) => Run(async actor =>
    {
        var facts = await new MonthlyRentCollectionSourceAdapter(db).LoadAsync(
            actor.MunicipalityId, stallId, year, month, BusinessToday,
            tracked: false, materializeMissingSource: false, actor: actor.Username, ct: ct);
        return facts is null
            ? Result<RentObligationQuoteDto>.NotFound()
            : Result<RentObligationQuoteDto>.Success(facts.Quote);
    }, ct);

    public Task<Result<IReadOnlyList<CollectionCandidateDto>>> GetPayorObligationsAsync(
        Guid payorId, CancellationToken ct = default) => Run(async actor =>
    {
        if (payorId == Guid.Empty || !await db.Payors.AsNoTracking().AnyAsync(x =>
                x.Id == payorId && x.MunicipalityId == actor.MunicipalityId, ct))
            return Result<IReadOnlyList<CollectionCandidateDto>>.NotFound();

        var candidates = new List<CollectionCandidateDto>();
        var rent = await new MonthlyRentCollectionSourceAdapter(db).GetPayorObligationsAsync(
            actor.MunicipalityId, payorId, BusinessToday, ct);
        candidates.AddRange(rent.Select(x => new CollectionCandidateDto(
            x.SourceKind, x.PaymentRecordId, null, x.StallId, x.StallNo,
            $"{x.FacilityName} · Stall {x.StallNo}", x.BillingYear, x.BillingMonth,
            x.PayorId, x.PayerNameSnapshot, x.OutstandingAmount, x.Instrument, x.CanAddToDraft)));

        var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, BusinessToday, ct);
        var bills = await UtilityBillQuery(actor.MunicipalityId, tracked: false).ToListAsync(ct);
        foreach (var bill in bills)
        {
            var facts = await BuildFactsAsync(bill, policy, actor.MunicipalityId, BusinessToday, ct);
            if (facts.Quote.PayorId == payorId && facts.Quote.OutstandingAmount > 0m)
                candidates.Add(new CollectionCandidateDto(
                    CollectionSourceKind.UtilityBill, bill.Id, CollectionSourcePart.Electricity,
                    bill.StallId, bill.Stall!.StallNo, $"ECF · {bill.Stall.StallNo}",
                    bill.BillingYear, bill.BillingMonth, facts.Quote.PayorId,
                    facts.Quote.PayerNameSnapshot, facts.Quote.OutstandingAmount,
                    RevenueInstrumentType.OfficialReceipt, facts.Quote.CanAddToDraft));
        }

        return Result<IReadOnlyList<CollectionCandidateDto>>.Success(candidates
            .OrderBy(x => x.BillingYear).ThenBy(x => x.BillingMonth)
            .ThenBy(x => x.SourceLabel, StringComparer.OrdinalIgnoreCase).ToList());
    }, ct);

    public Task<Result<IReadOnlyList<CollectionPayorDto>>> SearchCollectionPayorsAsync(
        string? search, CancellationToken ct = default) => Run(async actor =>
    {
        var term = NormalizeOptional(search);
        if (term is null || term.Length < 2)
            return Result<IReadOnlyList<CollectionPayorDto>>.Success([]);
        var payors = await db.Payors.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.MunicipalityId
                && x.DisplayName.Contains(term))
            .OrderBy(x => x.DisplayName)
            .Take(50)
            .Select(x => new CollectionPayorDto(x.Id, x.DisplayName))
            .ToListAsync(ct);
        return Result<IReadOnlyList<CollectionPayorDto>>.Success(payors);
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
        var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, BusinessToday, ct);
        var bill = await UtilityBillQuery(actor.MunicipalityId, tracked: false).SingleOrDefaultAsync(x => x.Id == request.UtilityBillId, ct);
        if (bill is null) return Result<EcfCollectionDraftDto>.NotFound();
        var facts = await BuildFactsAsync(bill, policy, actor.MunicipalityId, BusinessToday, ct);
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
            actor.MunicipalityId, actor.UserId, BusinessToday,
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

    public Task<Result<EcfCollectionDraftDto>> AddEcfLineAsync(
        AddEcfDraftLineRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        var draft = await CurrentDraftTrackedAsync(actor, ct);
        var businessDate = draft?.BusinessDate ?? BusinessToday;
        if (draft is not null) EnsureDraftBusinessDateIsCurrent(draft);
        var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, businessDate, ct);
        var bill = await UtilityBillQuery(actor.MunicipalityId, tracked: false)
            .SingleOrDefaultAsync(x => x.Id == request.UtilityBillId, ct);
        if (bill is null) return Result<EcfCollectionDraftDto>.NotFound();
        var facts = await BuildFactsAsync(bill, policy, actor.MunicipalityId, businessDate, ct);
        EnsureDraftEligible(facts);
        ValidateProposedAmount(request.ProposedAmount, facts.Quote.OutstandingAmount);

        if (draft is null)
        {
            if (request.ExpectedRevision.HasValue)
                throw Problem("The current draft no longer exists. Reload before adding this item.", ResultStatus.Conflict);
            draft = WebCollectionDraft.Create(actor.MunicipalityId, actor.UserId, businessDate,
                facts.Quote.PayorId, facts.Quote.PayerNameSnapshot, RevenueInstrumentType.OfficialReceipt,
                null, actor.Username);
            db.WebCollectionDrafts.Add(draft);
        }
        else
        {
            if (request.ExpectedRevision is not { } expected)
                throw Problem("ExpectedRevision is required when adding to an existing draft.", ResultStatus.Conflict);
            EnsureExpectedRevision(draft, expected);
            EnsureDraftBusinessDateIsCurrent(draft);
            var existingAllocations = await DraftAllocationsAsync(actor.MunicipalityId, draft.Id, ct);
            await EnsureDraftPayerContextAsync(draft, existingAllocations,
                facts.Quote.PayorId, facts.Quote.PayerNameSnapshot, facts.Snapshot.ContractId, ct);
            if (draft.InstrumentFamily != RevenueInstrumentType.OfficialReceipt
                || facts.Policy.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt)
                throw Problem("Only Official Receipt-compatible lines may share this Collection.", ResultStatus.Conflict);

            var alreadyAllocated = existingAllocations.Where(x =>
                x.SourceKind == CollectionSourceKind.UtilityBill && x.SourceId == bill.Id
                && x.SourcePart == CollectionSourcePart.Electricity).Sum(x => x.Amount);
            if (request.ProposedAmount + alreadyAllocated > facts.Quote.OutstandingAmount)
                throw Problem("The proposed ECF allocation exceeds the remaining Electricity outstanding amount.", ResultStatus.Conflict);
            draft.AdvanceRevision(expected, actor.Username);
        }

        var draftLines = await DraftLinesAsync(actor.MunicipalityId, draft.Id, ct);
        var sameLine = draftLines.SingleOrDefault(x =>
            x.RevenueClassificationId == facts.Policy.RevenueClassificationId
            && x.RevenueClassificationPolicyId == facts.Policy.Id
            && x.SourceKind == CollectionSourceKind.UtilityBill && x.SourceId == bill.Id
            && x.SourcePart == CollectionSourcePart.Electricity);
        if (sameLine is null)
        {
            var line = WebCollectionDraftLine.Create(actor.MunicipalityId, draft.Id, draftLines.Count,
                facts.Policy.RevenueClassificationId, facts.Policy.Id, request.ProposedAmount,
                facts.Policy.DisplayName, CollectionSourceKind.UtilityBill, bill.Id,
                CollectionSourcePart.Electricity, facts.SnapshotJson, actor.Username);
            db.WebCollectionDraftLines.Add(line);
            db.WebCollectionDraftAllocations.Add(WebCollectionDraftAllocation.Create(
                actor.MunicipalityId, line.Id, CollectionSourceKind.UtilityBill, bill.Id,
                CollectionSourcePart.Electricity, request.ProposedAmount, facts.SnapshotJson, actor.Username));
        }
        else
        {
            var allocation = await db.WebCollectionDraftAllocations.SingleAsync(x =>
                x.MunicipalityId == actor.MunicipalityId && x.DraftLineId == sameLine.Id
                && x.SourceKind == CollectionSourceKind.UtilityBill && x.SourceId == bill.Id
                && x.SourcePart == CollectionSourcePart.Electricity, ct);
            var amount = sameLine.Amount + request.ProposedAmount;
            sameLine.UpdateFinancialTerms(facts.Policy.RevenueClassificationId, facts.Policy.Id,
                amount, facts.SnapshotJson, actor.Username);
            allocation.UpdateAmountAndSnapshot(allocation.Amount + request.ProposedAmount,
                facts.SnapshotJson, actor.Username);
        }

        await db.SaveChangesAsync(ct);
        return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));
    }, ct);

    public Task<Result<EcfCollectionDraftDto>> AddRentAllocationAsync(
        AddRentDraftAllocationRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        var draft = await CurrentDraftTrackedAsync(actor, ct);
        var businessDate = draft?.BusinessDate ?? BusinessToday;
        if (draft is not null) EnsureDraftBusinessDateIsCurrent(draft);
        var adapter = new MonthlyRentCollectionSourceAdapter(db);
        var facts = await adapter.LoadAsync(actor.MunicipalityId, request.StallId,
            request.BillingYear, request.BillingMonth, businessDate,
            tracked: true, materializeMissingSource: true, actor: actor.Username, ct: ct);
        if (facts?.Record is null) return Result<EcfCollectionDraftDto>.NotFound();
        if (facts.Quote.RequiresLegacyReconciliation)
            throw Problem("This legacy partial PaymentRecord includes other fee components and requires reconciliation before rent allocation.", ResultStatus.Conflict);
        if (!facts.Quote.CanAddToDraft)
            throw Problem("This rental period has no collectible rent balance or is quiesced for cutover.", ResultStatus.Conflict);
        ValidateProposedAmount(request.ProposedAmount, facts.Quote.OutstandingAmount);

        var existingAllocations = draft is null
            ? []
            : await DraftAllocationsAsync(actor.MunicipalityId, draft.Id, ct);
        var alreadyAllocated = existingAllocations.Where(x =>
            x.SourceKind == CollectionSourceKind.PaymentRecord && x.SourceId == facts.Record.Id)
            .Sum(x => x.Amount);
        if (alreadyAllocated + request.ProposedAmount > facts.Quote.OutstandingAmount)
            throw Problem("The proposed rent allocation exceeds this period's remaining outstanding amount.", ResultStatus.Conflict);

        if (draft is null)
        {
            if (request.ExpectedRevision.HasValue)
                throw Problem("The current draft no longer exists. Reload before adding this rent period.", ResultStatus.Conflict);
            var payerName = facts.Quote.PayerNameSnapshot ?? NormalizeOptional(request.OneOffPayerName);
            draft = WebCollectionDraft.Create(actor.MunicipalityId, actor.UserId, businessDate,
                facts.Quote.PayorId, payerName, RevenueInstrumentType.OfficialReceipt, null, actor.Username);
            db.WebCollectionDrafts.Add(draft);
        }
        else
        {
            if (request.ExpectedRevision is not { } expected)
                throw Problem("ExpectedRevision is required when adding to an existing draft.", ResultStatus.Conflict);
            EnsureExpectedRevision(draft, expected);
            EnsureDraftBusinessDateIsCurrent(draft);
            await EnsureDraftPayerContextAsync(draft, existingAllocations,
                facts.Quote.PayorId, facts.Quote.PayerNameSnapshot, facts.Snapshot.ContractId, ct);
            if (draft.InstrumentFamily != RevenueInstrumentType.OfficialReceipt
                || facts.Policy.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt)
                throw Problem("Rent and the current Collection do not resolve to the same Official Receipt instrument.", ResultStatus.Conflict);
            draft.AdvanceRevision(expected, actor.Username);
        }

        var draftLines = await DraftLinesAsync(actor.MunicipalityId, draft.Id, ct);
        var rentLine = draftLines.SingleOrDefault(x =>
            x.RevenueClassificationId == facts.Policy.RevenueClassificationId
            && x.RevenueClassificationPolicyId == facts.Policy.Id
            && x.SourceKind is null && x.SourceId is null && x.SourcePart is null);
        const string lineSnapshot = "{\"schemaVersion\":1,\"lineKind\":\"PermanentStallRent\"}";
        if (rentLine is null)
        {
            rentLine = WebCollectionDraftLine.Create(actor.MunicipalityId, draft.Id, draftLines.Count,
                facts.Policy.RevenueClassificationId, facts.Policy.Id, request.ProposedAmount,
                facts.Policy.DisplayName, null, null, null, lineSnapshot, actor.Username);
            db.WebCollectionDraftLines.Add(rentLine);
        }
        else
        {
            rentLine.UpdateFinancialTerms(facts.Policy.RevenueClassificationId, facts.Policy.Id,
                rentLine.Amount + request.ProposedAmount, lineSnapshot, actor.Username);
        }

        var priorAllocation = existingAllocations.SingleOrDefault(x =>
            x.DraftLineId == rentLine.Id && x.SourceKind == CollectionSourceKind.PaymentRecord
            && x.SourceId == facts.Record.Id && x.SourcePart is null);
        if (priorAllocation is null)
            db.WebCollectionDraftAllocations.Add(WebCollectionDraftAllocation.Create(
                actor.MunicipalityId, rentLine.Id, CollectionSourceKind.PaymentRecord,
                facts.Record.Id, null, request.ProposedAmount, facts.SnapshotJson, actor.Username));
        else
            priorAllocation.UpdateAmountAndSnapshot(priorAllocation.Amount + request.ProposedAmount,
                facts.SnapshotJson, actor.Username);

        await db.SaveChangesAsync(ct);
        return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));
    }, ct);

    public Task<Result<EcfCollectionDraftDto>> UpdateDraftAllocationAsync(
        Guid draftId, UpdateCollectionDraftAllocationRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        var draft = await FindOwnedDraftAsync(draftId, actor.MunicipalityId, actor.UserId, tracked: true, ct);
        if (draft is null) return Result<EcfCollectionDraftDto>.NotFound();
        EnsureExpectedRevision(draft, request.ExpectedRevision);
        EnsureDraftBusinessDateIsCurrent(draft);
        if (request.AllocationId == Guid.Empty)
            throw Problem("A valid draft allocation is required.", ResultStatus.Invalid);
        if (request.ProposedAmount < 0m || request.ProposedAmount > 9_999_999_999_999_999.99m
            || decimal.Round(request.ProposedAmount, 2, MidpointRounding.ToZero) != request.ProposedAmount)
            throw Problem("Enter a non-negative allocation amount with at most two decimal places.", ResultStatus.Invalid);

        var allocation = await db.WebCollectionDraftAllocations.SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.MunicipalityId && x.Id == request.AllocationId, ct);
        if (allocation is null) return Result<EcfCollectionDraftDto>.NotFound();
        var line = await db.WebCollectionDraftLines.SingleOrDefaultAsync(x =>
            x.MunicipalityId == actor.MunicipalityId && x.Id == allocation.DraftLineId
            && x.DraftId == draft.Id, ct);
        if (line is null) return Result<EcfCollectionDraftDto>.NotFound();

        var allAllocations = await DraftAllocationsAsync(actor.MunicipalityId, draft.Id, ct);
        var sameSourceOthers = allAllocations.Where(x => x.Id != allocation.Id
            && x.SourceKind == allocation.SourceKind && x.SourceId == allocation.SourceId
            && x.SourcePart == allocation.SourcePart).Sum(x => x.Amount);
        ResolvedDraftSource current;
        if (allocation.SourceKind == CollectionSourceKind.UtilityBill
            && allocation.SourcePart == CollectionSourcePart.Electricity)
        {
            var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, draft.BusinessDate, ct);
            var bill = await UtilityBillQuery(actor.MunicipalityId, tracked: true)
                .SingleOrDefaultAsync(x => x.Id == allocation.SourceId, ct);
            if (bill is null) return Result<EcfCollectionDraftDto>.NotFound();
            var facts = await BuildFactsAsync(bill, policy, actor.MunicipalityId, draft.BusinessDate, ct);
            if (!string.Equals(allocation.SourceSnapshot, facts.SnapshotJson, StringComparison.Ordinal))
                throw Problem("ECF source facts changed. Refresh the source and review the current draft before editing this allocation.", ResultStatus.Conflict);
            current = new ResolvedDraftSource(allocation, facts.Classification, facts.Policy,
                facts.Quote.SettlementAuthority, facts.Quote.CumulativeSettledEvidence,
                facts.Quote.OutstandingAmount, facts.Quote.PayorId, facts.Quote.PayerNameSnapshot,
                facts.Snapshot.ContractId, facts.SnapshotJson, bill, null);
        }
        else if (allocation.SourceKind == CollectionSourceKind.PaymentRecord && allocation.SourcePart is null)
        {
            var facts = await new MonthlyRentCollectionSourceAdapter(db).LoadByRecordIdAsync(
                actor.MunicipalityId, allocation.SourceId, draft.BusinessDate, tracked: true, ct);
            if (facts?.Record is null) return Result<EcfCollectionDraftDto>.NotFound();
            if (facts.Quote.RequiresLegacyReconciliation)
                throw Problem("This PaymentRecord includes mixed legacy settlement evidence and requires reconciliation.", ResultStatus.Conflict);
            if (!string.Equals(allocation.SourceSnapshot, facts.SnapshotJson, StringComparison.Ordinal))
                throw Problem("Rent source facts changed. Refresh the source and review the current draft before editing this allocation.", ResultStatus.Conflict);
            current = new ResolvedDraftSource(allocation, facts.Classification, facts.Policy,
                facts.Quote.SettlementAuthority, facts.Quote.CumulativeSettledEvidence,
                facts.Quote.OutstandingAmount, facts.Quote.PayorId, facts.Quote.PayerNameSnapshot,
                facts.Snapshot.ContractId, facts.SnapshotJson, null, facts.Record);
        }
        else
        {
            throw Problem("This allocation source is not supported by the shared Composer.", ResultStatus.Conflict);
        }

        if (current.Authority == SettlementAuthority.PendingCutover)
            throw Problem("This source is quiesced for cutover reconciliation.", ResultStatus.Conflict);
        if (request.ProposedAmount + sameSourceOthers > current.Outstanding)
            throw Problem("The proposed allocation exceeds the source's remaining outstanding amount.", ResultStatus.Conflict);
        if (line.RevenueClassificationId != current.Classification.Id
            || line.RevenueClassificationPolicyId != current.Policy.Id)
            throw Problem("The source classification or policy changed. Refresh and review again.", ResultStatus.Conflict);

        var otherAllocations = allAllocations.Where(x => x.Id != allocation.Id).ToList();
        await EnsureDraftPayerContextAsync(draft, otherAllocations,
            current.PayorId, current.PayerName, current.ContractId, ct);
        var otherLineAllocations = allAllocations.Where(x => x.DraftLineId == line.Id && x.Id != allocation.Id).ToList();
        var newLineAmount = otherLineAllocations.Sum(x => x.Amount) + request.ProposedAmount;
        if (newLineAmount < 0m || newLineAmount > 9_999_999_999_999_999.99m
            || (request.ProposedAmount > 0m && newLineAmount <= 0m)
            || (request.ProposedAmount == 0m && otherLineAllocations.Count > 0 && newLineAmount <= 0m))
            throw Problem("The resulting collection line amount is outside the supported range.", ResultStatus.Invalid);

        draft.AdvanceRevision(request.ExpectedRevision, actor.Username);
        if (request.ProposedAmount == 0m)
            db.WebCollectionDraftAllocations.Remove(allocation);
        else
        {
            allocation.UpdateAmountAndSnapshot(request.ProposedAmount, current.SourceSnapshot, actor.Username);
        }
        if (request.ProposedAmount == 0m && otherLineAllocations.Count == 0)
        {
            db.WebCollectionDraftLines.Remove(line);
        }
        else
        {
            line.UpdateFinancialTerms(current.Classification.Id, current.Policy.Id,
                newLineAmount, line.CalculationSnapshot ?? current.SourceSnapshot, actor.Username);
        }
        await db.SaveChangesAsync(ct);
        return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));
    }, ct);

    public Task<Result<EcfCollectionDraftDto>> UpdateAllocationAsync(
        Guid draftId, UpdateEcfDraftAllocationRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        var draft = await FindOwnedDraftAsync(draftId, actor.MunicipalityId, actor.UserId, tracked: true, ct);
        if (draft is null) return Result<EcfCollectionDraftDto>.NotFound();
        EnsureExpectedRevision(draft, request.ExpectedRevision);
        EnsureDraftBusinessDateIsCurrent(draft);
        var line = await SingleDraftLineAsync(actor.MunicipalityId, draft.Id, ct);
        var allocation = await SingleDraftAllocationAsync(actor.MunicipalityId, line.Id, ct);
        var policy = await ResolveEcfPolicyAsync(actor.MunicipalityId, draft.BusinessDate, ct);
        var bill = await UtilityBillQuery(actor.MunicipalityId, tracked: false).SingleOrDefaultAsync(x => x.Id == line.SourceId, ct);
        if (bill is null) return Result<EcfCollectionDraftDto>.NotFound();
        var facts = await BuildFactsAsync(bill, policy, actor.MunicipalityId, draft.BusinessDate, ct);
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
        EnsureDraftBusinessDateIsCurrent(draft);
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
        EnsureDraftBusinessDateIsCurrent(draft);
        var lines = await DraftLinesAsync(actor.MunicipalityId, draft.Id, ct);
        if (lines.Count == 0)
            throw Problem("Add at least one approved source line before review.", ResultStatus.Invalid);
        var resolved = await ResolveDraftSourcesAsync(draft, lines, tracked: false, ct);
        if (!draft.AccountableDocumentId.HasValue)
            throw Problem("Select an available Official Receipt before review.", ResultStatus.Invalid);
        var document = await RequireSelectableReceiptAsync(draft.AccountableDocumentId.Value, actor, tracked: false, ct);
        if (draft.InstrumentFamily != RevenueInstrumentType.OfficialReceipt)
            throw Problem("All reviewed lines must resolve to an Official Receipt.", ResultStatus.Conflict);

        var normalizedIntent = NormalizeComposerIntent(draft, resolved, document.DocumentNumber);
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

    public Task<Result<EcfCollectionDraftDto>> ResumeDraftAsync(
        Guid draftId, EcfDraftRevisionRequest request, CancellationToken ct = default) => Run(async actor =>
    {
        var draft = await FindOwnedDraftAsync(draftId, actor.MunicipalityId, actor.UserId, tracked: true, ct);
        if (draft is null) return Result<EcfCollectionDraftDto>.NotFound();
        EnsureExpectedRevision(draft, request.ExpectedRevision);
        if (draft.BusinessDate == BusinessToday)
            return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));

        var lines = await DraftLinesAsync(actor.MunicipalityId, draft.Id, ct);
        ResolvedDraft? resolved = null;
        if (lines.Count > 0)
        {
            resolved = await ResolveDraftSourcesAsync(draft, lines, tracked: true, ct,
                businessDate: BusinessToday, allowSnapshotRefresh: true);
            foreach (var resolvedLine in resolved.Lines)
            {
                var source = resolvedLine.Sources[0];
                var lineSnapshot = resolvedLine.Line.SourceKind == CollectionSourceKind.UtilityBill
                    && resolvedLine.Line.SourcePart == CollectionSourcePart.Electricity
                    ? source.SourceSnapshot
                    : resolvedLine.Line.CalculationSnapshot ?? source.SourceSnapshot;
                resolvedLine.Line.UpdateFinancialTerms(source.Classification.Id, source.Policy.Id,
                    resolvedLine.Sources.Sum(x => x.Allocation.Amount), lineSnapshot, actor.Username,
                    source.Policy.DisplayName);
                foreach (var allocation in resolvedLine.Sources)
                    allocation.Allocation.UpdateAmountAndSnapshot(
                        allocation.Allocation.Amount, allocation.SourceSnapshot, actor.Username);
            }
        }

        var refreshedPayerName = resolved?.Sources.FirstOrDefault()?.PayerName
            ?? draft.PayerNameSnapshot;
        draft.RefreshForBusinessDate(request.ExpectedRevision, BusinessToday,
            refreshedPayerName, actor.Username);
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

        var lines = await DraftLinesAsync(actor.MunicipalityId, draft.Id, ct);
        var allocations = await DraftAllocationsAsync(actor.MunicipalityId, draft.Id, ct);
        var selectedDocument = draft.AccountableDocumentId is { } selectedId
            ? await db.AccountableDocuments.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == selectedId && x.MunicipalityId == actor.MunicipalityId, ct)
            : null;
        var normalizedIntent = NormalizeComposerIntent(draft, lines, allocations, selectedDocument?.DocumentNumber);
        var fingerprint = PostingOperation.ComputeIntentFingerprint(
            IntentVersion, normalizedIntent, WebOrigin, ActorIdentity(actor));

        var prior = await FindPostingOperationAsync(actor.MunicipalityId, request.ClientOperationId, ct);
        if (prior is not null)
            return await ResolvePriorOperationAsync(prior, fingerprint, actor, ct);

        if (draft.Status == CollectionDraftStatus.Posted && draft.CollectionId is { } postedCollectionId)
            return Result<EcfPostOutcomeDto>.Success(await BuildPostOutcomeAsync(
                actor.MunicipalityId, postedCollectionId, true, ct));
        if (draft.Status != CollectionDraftStatus.Draft)
            throw Problem("This collection draft is no longer available for posting.", ResultStatus.Conflict);
        if (draft.Revision != request.ExpectedRevision)
            throw Problem("STALE DRAFT: reload the latest revision before continuing.", ResultStatus.Conflict);
        if (!draft.IsReviewedForCurrentRevision
            || draft.ReviewedFingerprint != FingerprintFinancialContent(normalizedIntent))
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "REVIEW_REQUIRED",
                "This exact draft revision has not been reviewed. Review the current lines and allocations before posting.", ct);

        if (!draft.AccountableDocumentId.HasValue || selectedDocument is null)
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "OR_UNAVAILABLE",
                "Select a valid available Official Receipt and review again.", ct);

        ResolvedDraft resolved;
        try
        {
            resolved = await ResolveDraftSourcesAsync(draft, lines, tracked: true, ct);
        }
        catch (WorkflowProblem problem)
        {
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "SOURCE_REVIEW_REQUIRED", problem.Message, ct);
        }

        if (draft.BusinessDate != BusinessToday)
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "BUSINESS_DATE_REFRESH_REQUIRED",
                "The draft belongs to an earlier Philippine business date. Resume and refresh it, then review the new revision before posting.", ct);

        if (resolved.Sources.Any(x => x.Authority != SettlementAuthority.Canonical))
        {
            var legacy = resolved.Sources.Any(x => x.Authority == SettlementAuthority.Legacy);
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, legacy ? "SOURCE_LEGACY" : "SOURCE_CUTOVER_PENDING",
                legacy
                    ? "Every source must leave Legacy settlement authority through controlled cutover before canonical posting."
                    : "A source is quiesced for cutover reconciliation and cannot be posted.", ct);
        }

        if (draft.InstrumentFamily != RevenueInstrumentType.OfficialReceipt
            || selectedDocument.InstrumentType != RevenueInstrumentType.OfficialReceipt
            || selectedDocument.State is not (AccountableDocumentState.InOffice or AccountableDocumentState.Assigned)
            || (selectedDocument.State == AccountableDocumentState.Assigned && selectedDocument.AssignedUserId != actor.UserId))
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "OR_UNAVAILABLE",
                "The selected Official Receipt is unavailable, assigned elsewhere, or incompatible with the reviewed lines.", ct);

        AccountableDocument document;
        try
        {
            document = await RequireSelectableReceiptAsync(
                draft.AccountableDocumentId.Value, actor, tracked: true, ct);
        }
        catch (WorkflowProblem problem)
        {
            return await RejectOrResolveExistingAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "OR_UNAVAILABLE", problem.Message, ct);
        }
        var collectionLineDrafts = resolved.Lines.Select(line => new CollectionLineDraft(
            line.Sources[0].Classification, line.Sources[0].Policy, line.Line.Amount,
            line.Line.SourceKind, line.Line.SourceId, line.Line.SourcePart,
            line.Line.CalculationSnapshot,
            line.Sources.Select(source => new CollectionAllocationDraft(
                source.Allocation.SourceKind, source.Allocation.SourceId, source.Allocation.Amount,
                source.Allocation.SourcePart, source.Allocation.SourceSnapshot)).ToList())).ToList();

        Collection collection;
        try
        {
            var projections = resolved.Sources.GroupBy(x => new SourceIdentity(
                    x.Allocation.SourceKind, x.Allocation.SourceId, x.Allocation.SourcePart))
                .Select(group =>
                {
                    var source = group.First();
                    var cumulative = source.Settled + group.Sum(x => x.Allocation.Amount);
                    return (Action<DateTime>)(now =>
                    {
                        if (source.UtilityBill is { } bill)
                        {
                            if (source.Allocation.SourcePart == CollectionSourcePart.Electricity)
                                bill.ApplyCanonicalElectricityProjection(cumulative, document.DocumentNumber, now, actor.Username);
                            else if (source.Allocation.SourcePart == CollectionSourcePart.Water)
                                bill.ApplyCanonicalWaterProjection(cumulative, document.DocumentNumber, now, actor.Username);
                            else
                                throw Problem("Unsupported UtilityBill source part.", ResultStatus.Conflict);
                        }
                        else if (source.PaymentRecord is { } payment)
                            payment.ApplyCanonicalRentProjection(cumulative, now, actor.Username);
                        else
                            throw Problem("A resolved source has no canonical projection adapter.", ResultStatus.Conflict);
                    });
                }).ToList();

            collection = await new CanonicalCollectionPostingCoordinator(db).PostAsync(
                actor.MunicipalityId, request.ClientOperationId, IntentVersion, normalizedIntent,
                WebOrigin, ActorIdentity(actor), actor.Username, actor.Role, draft.BusinessDate,
                actor.Username, collectionLineDrafts, document, payorId: draft.PayorId,
                payerName: draft.PayerNameSnapshot, sourceProjections: projections,
                beforeCommit: (collectionId, _) => draft.MarkPosted(request.ExpectedRevision, collectionId, actor.Username),
                ct: ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var winner = await FindPostingOperationAsync(actor.MunicipalityId, request.ClientOperationId, ct);
            if (winner is not null)
                return await ResolvePriorOperationAsync(winner, fingerprint, actor, ct);
            return await RecordRejectionAsync(actor, request.ClientOperationId, normalizedIntent,
                draft.AccountableDocumentId, "POST_CONCURRENCY_CONFLICT",
                "A source, draft, or Official Receipt changed while posting. The complete Collection was not posted; reload and review again.", ct);
        }
        catch (DbUpdateException)
        {
            var winner = await FindPostingOperationAsync(actor.MunicipalityId, request.ClientOperationId, ct);
            if (winner is not null)
                return await ResolvePriorOperationAsync(winner, fingerprint, actor, ct);
            throw Problem("A source allocation or Official Receipt conflicts with another saved transaction. Reload and review again.", ResultStatus.Conflict);
        }

        return Result<EcfPostOutcomeDto>.Success(new EcfPostOutcomeDto(
            collection.Id, document.DocumentNumber, "Posted", collection.TotalAmount,
            collection.Lines.Count, false));
    }, ct);

    public Task<Result<IReadOnlyList<EcfCollectionActivityDto>>> GetActivityAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default) =>
        GetActivityAsync(from, to, ecfOnly: false, ct);

    public Task<Result<IReadOnlyList<EcfCollectionActivityDto>>> GetEcfActivityAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default) =>
        GetActivityAsync(from, to, ecfOnly: true, ct);

    private Task<Result<IReadOnlyList<EcfCollectionActivityDto>>> GetActivityAsync(
        DateOnly from, DateOnly to, bool ecfOnly, CancellationToken ct) => Run(async actor =>
    {
        if (from > to || to.DayNumber - from.DayNumber > 366)
            throw Problem("Choose a valid collection period of no more than 367 days.", ResultStatus.Invalid);
        var collectionsQuery = db.Collections.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.MunicipalityId
                && x.BusinessDate >= from && x.BusinessDate <= to);
        if (ecfOnly)
        {
            var ecfCollectionIds = db.CollectionLines.AsNoTracking()
                .Where(x => x.MunicipalityId == actor.MunicipalityId
                    && x.SourceKind == CollectionSourceKind.UtilityBill
                    && x.SourcePart == CollectionSourcePart.Electricity)
                .Select(x => x.CollectionId);
            collectionsQuery = collectionsQuery.Where(x => ecfCollectionIds.Contains(x.Id));
        }
        var collections = await collectionsQuery
            .ToListAsync(ct);
        var collectionMap = collections.ToDictionary(x => x.Id);
        var ids = collectionMap.Keys.ToArray();
        var includedLines = await db.CollectionLines.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.MunicipalityId && ids.Contains(x.CollectionId))
            .OrderBy(x => x.CollectionId).ThenBy(x => x.Id).ToListAsync(ct);
        var lineIds = includedLines.Select(x => x.Id).ToArray();
        var allocations = await db.CollectionAllocations.AsNoTracking()
            .Where(x => x.MunicipalityId == actor.MunicipalityId && lineIds.Contains(x.CollectionLineId))
            .OrderBy(x => x.SourceKind).ThenBy(x => x.SourceId).ToListAsync(ct);
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
                var lineAllocations = allocations.Where(x => x.CollectionLineId == line.Id).ToList();
                var allocationDetails = lineAllocations.Select(allocation =>
                {
                    var evidence = ReadSourceDisplayEvidence(allocation.SourceSnapshot);
                    var sourceName = allocation.SourceKind == CollectionSourceKind.UtilityBill
                        ? allocation.SourcePart == CollectionSourcePart.Electricity ? "UtilityBill Electricity" : "UtilityBill Water"
                        : allocation.SourceKind.ToString();
                    return new CollectionActivityAllocationDto(allocation.Amount, evidence.Year, evidence.Month,
                        evidence.Label is null ? sourceName : $"{sourceName} · {evidence.Label}");
                }).ToList();
                var firstEvidence = lineAllocations.Count == 0
                    ? (Year: (int?)null, Month: (int?)null, Label: (string?)null)
                    : ReadSourceDisplayEvidence(lineAllocations[0].SourceSnapshot);
                var allocationLabel = string.Join("; ", allocationDetails.Select(x => x.SourceLabel));
                return new EcfCollectionActivityLineDto(
                    names.GetValueOrDefault(line.RevenueClassificationPolicyId, "Collection item"), line.Amount,
                    firstEvidence.Year ?? 0, firstEvidence.Month ?? 0, allocationLabel,
                    line.CalculationSnapshot, allocationDetails,
                    SummarizeCalculationDetail(line.CalculationSnapshot));
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
        UtilityBill bill, PolicyFacts policy, Guid tenantId, DateOnly businessDate, CancellationToken ct)
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
            bill.BillingYear, bill.BillingMonth, businessDate);
        var contract = occupancy?.Contract;
        var linkedPayor = contract?.PayorId is not null ? contract.Payor : null;
        Guid? payorId = linkedPayor?.MunicipalityId == tenantId ? linkedPayor.Id : null;
        var payerName = linkedPayor?.DisplayName
            ?? (string.IsNullOrWhiteSpace(contract?.ActualOccupant) ? null : contract.ActualOccupant.Trim());
        var facility = bill.Stall.Facility!;
        var section = ResolveSection(facility, bill.Stall);
        var snapshot = new EcfSourceSnapshot(
            1, bill.Id, bill.ElectricitySourceVersion, bill.StallId, bill.Stall.StallNo,
            contract?.Id, contract?.UpdatedAt, payorId, payerName,
            facility.Id, facility.Name, section, bill.BillingYear, bill.BillingMonth,
            bill.ElecPreviousReading, bill.ElecCurrentReading, bill.ElecConsumption,
            bill.ElecRatePerKwh, bill.ElecCharge, settled, outstanding,
            policy.Classification.Id, policy.Policy.Id, policy.Policy.DisplayName,
            RevenueInstrumentType.OfficialReceipt, bill.ElectricitySettlementAuthorityState, "Metered");
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

    private async Task<WebCollectionDraft?> CurrentDraftTrackedAsync(Actor actor, CancellationToken ct) =>
        await db.WebCollectionDrafts.Where(x => x.MunicipalityId == actor.MunicipalityId
                && x.OwnerUserId == actor.UserId && x.Status == CollectionDraftStatus.Draft)
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<List<WebCollectionDraftLine>> DraftLinesAsync(Guid tenantId, Guid draftId, CancellationToken ct) =>
        await db.WebCollectionDraftLines.Where(x => x.MunicipalityId == tenantId && x.DraftId == draftId)
            .OrderBy(x => x.LineOrder).ToListAsync(ct);

    private async Task<List<WebCollectionDraftAllocation>> DraftAllocationsAsync(
        Guid tenantId, Guid draftId, CancellationToken ct)
    {
        var lineIds = await db.WebCollectionDraftLines.Where(x =>
                x.MunicipalityId == tenantId && x.DraftId == draftId)
            .Select(x => x.Id).ToListAsync(ct);
        return await db.WebCollectionDraftAllocations.Where(x =>
                x.MunicipalityId == tenantId && lineIds.Contains(x.DraftLineId))
            .OrderBy(x => x.SourceKind).ThenBy(x => x.SourceId).ToListAsync(ct);
    }

    private async Task EnsureDraftPayerContextAsync(
        WebCollectionDraft draft,
        IReadOnlyList<WebCollectionDraftAllocation> existingAllocations,
        Guid? sourcePayorId,
        string? sourcePayerName,
        Guid? sourceContractId,
        CancellationToken ct)
    {
        if (draft.PayorId != sourcePayorId || draft.PayerNameSnapshot != sourcePayerName)
            throw Problem("This source does not have the same authoritative Payor context as the current Collection.", ResultStatus.Conflict);
        if (existingAllocations.Count == 0) return;
        if (draft.PayorId.HasValue) return;
        if (!sourceContractId.HasValue)
            throw Problem("Unlinked sources can share a Collection only when they identify the same authoritative contract.", ResultStatus.Conflict);

        foreach (var allocation in existingAllocations)
        {
            var evidence = ReadPayerEvidence(allocation.SourceSnapshot);
            if (evidence.PayorId.HasValue || evidence.ContractId != sourceContractId
                || evidence.PayerName != sourcePayerName)
                throw Problem("Unlinked sources do not share the same authoritative contract payer context.", ResultStatus.Conflict);
        }
    }

    private static (Guid? PayorId, Guid? ContractId, string? PayerName) ReadPayerEvidence(string? snapshotJson)
    {
        if (string.IsNullOrWhiteSpace(snapshotJson)) return (null, null, null);
        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            var root = document.RootElement;
            return (ReadGuid(root, "payorId"), ReadGuid(root, "contractId"), ReadString(root, "payerName"));
        }
        catch (JsonException)
        {
            return (null, null, null);
        }
    }

    private static Guid? ReadGuid(JsonElement root, string propertyName) =>
        TryProperty(root, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            && value.TryGetGuid(out var id) ? id : null;

    private static string? ReadString(JsonElement root, string propertyName) =>
        TryProperty(root, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool TryProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        if (root.TryGetProperty(propertyName, out value)) return true;
        var pascal = char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        return root.TryGetProperty(pascal, out value);
    }

    private async Task<ResolvedDraft> ResolveDraftSourcesAsync(
        WebCollectionDraft draft, IReadOnlyList<WebCollectionDraftLine> lines, bool tracked, CancellationToken ct,
        DateOnly? businessDate = null, bool allowSnapshotRefresh = false)
    {
        var resolvedBusinessDate = businessDate ?? draft.BusinessDate;
        var allocations = await DraftAllocationsAsync(draft.MunicipalityId, draft.Id, ct);
        var resolvedLines = new List<ResolvedDraftLine>();
        var allSources = new List<ResolvedDraftSource>();
        foreach (var line in lines)
        {
            var lineAllocations = allocations.Where(x => x.DraftLineId == line.Id).ToList();
            if (lineAllocations.Count == 0 || lineAllocations.Sum(x => x.Amount) != line.Amount)
                throw Problem("Every collection line must equal its explicit source allocations.", ResultStatus.Conflict);

            var resolvedAllocations = new List<ResolvedDraftSource>();
            foreach (var allocation in lineAllocations)
            {
                ResolvedDraftSource source;
                if (allocation.SourceKind == CollectionSourceKind.UtilityBill
                    && allocation.SourcePart == CollectionSourcePart.Electricity)
                {
                    var policy = await ResolveEcfPolicyAsync(draft.MunicipalityId, resolvedBusinessDate, ct);
                    var bill = await UtilityBillQuery(draft.MunicipalityId, tracked)
                        .SingleOrDefaultAsync(x => x.Id == allocation.SourceId, ct);
                    if (bill is null)
                        throw Problem("The Electricity source was not found in this tenant.", ResultStatus.NotFound);
                    var facts = await BuildFactsAsync(bill, policy, draft.MunicipalityId,
                        resolvedBusinessDate, ct);
                    if (!allowSnapshotRefresh
                        && !string.Equals(allocation.SourceSnapshot, facts.SnapshotJson, StringComparison.Ordinal))
                        throw Problem("ECF source facts changed after this draft item was added. Refresh and review again.", ResultStatus.Conflict);
                    source = new ResolvedDraftSource(allocation, facts.Classification, facts.Policy,
                        facts.Quote.SettlementAuthority, facts.Quote.CumulativeSettledEvidence,
                        facts.Quote.OutstandingAmount, facts.Quote.PayorId, facts.Quote.PayerNameSnapshot,
                        facts.Snapshot.ContractId, facts.SnapshotJson, bill, null);
                }
                else if (allocation.SourceKind == CollectionSourceKind.PaymentRecord && allocation.SourcePart is null)
                {
                    var facts = await new MonthlyRentCollectionSourceAdapter(db).LoadByRecordIdAsync(
                        draft.MunicipalityId, allocation.SourceId, resolvedBusinessDate, tracked, ct);
                    if (facts?.Record is null)
                        throw Problem("The rent source or its answerable occupancy is no longer available.", ResultStatus.Conflict);
                    if (facts.Quote.RequiresLegacyReconciliation)
                        throw Problem("A mixed legacy partial PaymentRecord requires reconciliation before rent allocation.", ResultStatus.Conflict);
                    if (!allowSnapshotRefresh
                        && !string.Equals(allocation.SourceSnapshot, facts.SnapshotJson, StringComparison.Ordinal))
                        throw Problem("Rent source facts changed after this draft item was added. Refresh and review again.", ResultStatus.Conflict);
                    source = new ResolvedDraftSource(allocation, facts.Classification, facts.Policy,
                        facts.Quote.SettlementAuthority, facts.Quote.CumulativeSettledEvidence,
                        facts.Quote.OutstandingAmount, facts.Quote.PayorId, facts.Quote.PayerNameSnapshot,
                        facts.Snapshot.ContractId, facts.SnapshotJson, null, facts.Record);
                }
                else
                {
                    throw Problem("This collection draft contains a source not supported by the shared Composer.", ResultStatus.Conflict);
                }

                if (source.Authority == SettlementAuthority.PendingCutover)
                    throw Problem("A source pending cutover reconciliation cannot be reviewed or posted.", ResultStatus.Conflict);
                if (source.Policy.PermittedInstrumentType != RevenueInstrumentType.OfficialReceipt
                    || draft.InstrumentFamily != RevenueInstrumentType.OfficialReceipt)
                    throw Problem("All lines in this draft must resolve to the same Official Receipt instrument.", ResultStatus.Conflict);
                if (!allowSnapshotRefresh && (line.RevenueClassificationId != source.Classification.Id
                    || line.RevenueClassificationPolicyId != source.Policy.Id)
                   )
                    throw Problem("A line's classification or effective policy no longer matches its source.", ResultStatus.Conflict);
                if (allocation.Amount <= 0m || allocation.Amount > source.Outstanding)
                    throw Problem("An explicit allocation exceeds the source's current outstanding amount.", ResultStatus.Conflict);

                resolvedAllocations.Add(source);
                allSources.Add(source);
            }

            if (line.SourceKind is { } lineSourceKind
                && (lineAllocations.Any(x => x.SourceKind != lineSourceKind)
                    || lineAllocations.Any(x => x.SourceId != line.SourceId || x.SourcePart != line.SourcePart)))
                throw Problem("A source-anchored line must match each of its explicit allocations.", ResultStatus.Conflict);
            if (resolvedAllocations.Any(x => x.Classification.Id != resolvedAllocations[0].Classification.Id
                || x.Policy.Id != resolvedAllocations[0].Policy.Id))
                throw Problem("The source periods in this line no longer share one classification and effective policy.", ResultStatus.Conflict);
            resolvedLines.Add(new ResolvedDraftLine(line, resolvedAllocations));
        }

        foreach (var group in allSources.GroupBy(x => new SourceIdentity(x.Allocation.SourceKind,
                     x.Allocation.SourceId, x.Allocation.SourcePart)))
        {
            var source = group.First();
            if (group.Sum(x => x.Allocation.Amount) > source.Outstanding)
                throw Problem("The combined draft allocations exceed an obligation's current outstanding amount.", ResultStatus.Conflict);
            if (group.Any(x => x.Classification.Id != source.Classification.Id
                || x.Policy.Id != source.Policy.Id))
                throw Problem("One obligation cannot be posted under multiple classifications or policies in a single draft.", ResultStatus.Conflict);
        }

        if (allSources.Count == 0)
            throw Problem("A collection draft must contain at least one explicitly allocated source.", ResultStatus.Invalid);
        var first = allSources[0];
        if (allSources.Any(x => x.PayorId != first.PayorId))
            throw Problem("The draft contains obligations belonging to different authoritative Payors.", ResultStatus.Conflict);
        if (draft.PayorId != first.PayorId)
            throw Problem("The draft's Payor context no longer matches its source relationships.", ResultStatus.Conflict);
        if (draft.PayorId.HasValue)
        {
            if (!allowSnapshotRefresh
                && allSources.Any(x => !string.Equals(x.PayerName, draft.PayerNameSnapshot, StringComparison.Ordinal)))
                throw Problem("The linked Payor name changed after this draft was prepared. Refresh and review again.", ResultStatus.Conflict);
        }
        else if (allSources.Count > 1)
        {
            var contractIds = allSources.Select(x => x.ContractId).Distinct().ToList();
            if (contractIds.Count != 1 || contractIds[0] is null
                || (!allowSnapshotRefresh
                    && allSources.Any(x => !string.Equals(x.PayerName, draft.PayerNameSnapshot, StringComparison.Ordinal))))
                throw Problem("Unlinked sources may share a Collection only when they identify one authoritative contract payer context.", ResultStatus.Conflict);
        }
        else if (first.PayerName is not null
            && !allowSnapshotRefresh
            && !string.Equals(first.PayerName, draft.PayerNameSnapshot, StringComparison.Ordinal))
        {
            throw Problem("The source payer evidence changed after this draft was prepared. Refresh and review again.", ResultStatus.Conflict);
        }

        return new ResolvedDraft(resolvedLines, allSources);
    }

    private static string NormalizeComposerIntent(
        WebCollectionDraft draft, ResolvedDraft resolved, string? documentNumber) =>
        NormalizeComposerIntent(draft, resolved.Lines.Select(x => x.Line).ToList(),
            resolved.Sources.Select(x => x.Allocation).ToList(), documentNumber);

    private static string NormalizeComposerIntent(
        WebCollectionDraft draft,
        IReadOnlyList<WebCollectionDraftLine> lines,
        IReadOnlyList<WebCollectionDraftAllocation> allocations,
        string? documentNumber)
    {
        var normalizedLines = lines.Select(line =>
        {
            var lineAllocations = allocations.Where(x => x.DraftLineId == line.Id)
                .GroupBy(x => new SourceIdentity(x.SourceKind, x.SourceId, x.SourcePart))
                .Select(group => new NormalizedComposerAllocation(
                    group.Key.SourceKind, group.Key.SourceId, group.Key.SourcePart,
                    group.Sum(x => x.Amount), group.First().SourceSnapshot ?? string.Empty))
                .OrderBy(x => x.SourceKind).ThenBy(x => x.SourceId).ThenBy(x => x.SourcePart)
                .ToList();
            return new NormalizedComposerLine(line.RevenueClassificationId,
                line.RevenueClassificationPolicyId, line.Amount, line.SourceKind,
                line.SourceId, line.SourcePart, lineAllocations);
        }).OrderBy(x => x.ClassificationId).ThenBy(x => x.PolicyId)
            .ThenBy(x => x.SourceKind).ThenBy(x => x.SourceId).ThenBy(x => x.Amount).ToList();
        return JsonSerializer.Serialize(new NormalizedComposerIntent(
            IntentVersion, draft.MunicipalityId, draft.Id, draft.OwnerUserId, draft.BusinessDate,
            draft.PayorId, draft.PayerNameSnapshot, draft.InstrumentFamily,
            draft.AccountableDocumentId, documentNumber, normalizedLines), IntentJsonOptions);
    }

    private static JsonSerializerOptions CreateIntentJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonOptions);
        options.Converters.Add(new CanonicalDecimalJsonConverter());
        return options;
    }

    private sealed class CanonicalDecimalJsonConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture));
    }

    private sealed record SourceIdentity(
        CollectionSourceKind SourceKind, Guid SourceId, CollectionSourcePart? SourcePart);

    private sealed record ResolvedDraft( IReadOnlyList<ResolvedDraftLine> Lines, IReadOnlyList<ResolvedDraftSource> Sources);
    private sealed record ResolvedDraftLine(WebCollectionDraftLine Line, IReadOnlyList<ResolvedDraftSource> Sources);
    private sealed record ResolvedDraftSource(
        WebCollectionDraftAllocation Allocation,
        RevenueClassification Classification,
        RevenueClassificationPolicy Policy,
        SettlementAuthority Authority,
        decimal Settled,
        decimal Outstanding,
        Guid? PayorId,
        string? PayerName,
        Guid? ContractId,
        string SourceSnapshot,
        UtilityBill? UtilityBill,
        PaymentRecord? PaymentRecord);
    private sealed record NormalizedComposerIntent(
        int SchemaVersion, Guid MunicipalityId, Guid DraftId, Guid OwnerUserId, DateOnly BusinessDate,
        Guid? PayorId, string? PayerName, RevenueInstrumentType? Instrument,
        Guid? AccountableDocumentId, string? DocumentNumber,
        IReadOnlyList<NormalizedComposerLine> Lines);
    private sealed record NormalizedComposerLine(
        Guid ClassificationId, Guid PolicyId, decimal Amount,
        CollectionSourceKind? SourceKind, Guid? SourceId, CollectionSourcePart? SourcePart,
        IReadOnlyList<NormalizedComposerAllocation> Allocations);
    private sealed record NormalizedComposerAllocation(
        CollectionSourceKind SourceKind, Guid SourceId, CollectionSourcePart? SourcePart,
        decimal Amount, string SourceSnapshot);

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
            .Where(x => x.MunicipalityId == draft.MunicipalityId && lineIds.Contains(x.DraftLineId))
            .OrderBy(x => x.SourceKind).ThenBy(x => x.SourceId).ToListAsync(ct);
        var document = draft.AccountableDocumentId is { } documentId
            ? await db.AccountableDocuments.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == documentId && x.MunicipalityId == draft.MunicipalityId, ct)
            : null;
        var policyIds = lines.Select(x => x.RevenueClassificationPolicyId).Distinct().ToArray();
        var policyNames = await db.RevenueClassificationPolicies.AsNoTracking()
            .Where(x => x.MunicipalityId == draft.MunicipalityId && policyIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        var dtoLines = lines.Select(line =>
        {
            var lineAllocations = allocations.Where(x => x.DraftLineId == line.Id).ToList();
            var allocationDtos = lineAllocations.Select(allocation =>
            {
                var evidence = ReadSourceDisplayEvidence(allocation.SourceSnapshot);
                return new CollectionDraftAllocationDto(allocation.Id, allocation.SourceKind,
                    allocation.SourceId, allocation.SourcePart, allocation.Amount,
                    evidence.Year, evidence.Month, evidence.Label,
                    ReadSourceSettlementAuthority(allocation.SourceSnapshot));
            }).ToList();
            var kinds = lineAllocations.Select(x => x.SourceKind).Distinct().ToList();
            var sourceKind = line.SourceKind ?? (kinds.Count == 1 ? kinds[0] : null);
            var ids = lineAllocations.Select(x => x.SourceId).Distinct().ToList();
            var sourceId = line.SourceId ?? (ids.Count == 1 ? ids[0] : null);
            var ecfAllocation = lineAllocations.FirstOrDefault(x =>
                x.SourceKind == CollectionSourceKind.UtilityBill
                && x.SourcePart == CollectionSourcePart.Electricity);
            var utilityBillId = line.SourceKind == CollectionSourceKind.UtilityBill
                ? line.SourceId!.Value
                : ecfAllocation?.SourceId ?? Guid.Empty;
            var sourcePart = line.SourcePart
                ?? ecfAllocation?.SourcePart
                ?? CollectionSourcePart.Electricity;
            var version = lineAllocations.Select(x => ReadSourceVersion(x.SourceSnapshot))
                .DefaultIfEmpty(0L).Max();
            return new EcfCollectionDraftLineDto(line.Id, line.Amount,
                line.Description ?? policyNames.GetValueOrDefault(line.RevenueClassificationPolicyId, "Collection item"),
                utilityBillId, sourcePart, version,
                lineAllocations.Sum(x => x.Amount), sourceKind, sourceId, allocationDtos,
                SummarizeCalculationDetail(line.CalculationSnapshot));
        }).ToList();

        return new EcfCollectionDraftDto(draft.Id, draft.Revision, draft.BusinessDate,
            draft.Status.ToString(), draft.IsReviewedForCurrentRevision,
            draft.ReviewedAtUtc, draft.PayerNameSnapshot, draft.PayorId,
            draft.AccountableDocumentId, document?.DocumentNumber,
            dtoLines.Sum(x => x.Amount), draft.CollectionId, dtoLines,
            draft.Status == CollectionDraftStatus.Draft && draft.BusinessDate != BusinessToday);
    }

    private static long ReadSourceVersion(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return 0;
        try
        {
            using var document = JsonDocument.Parse(snapshot);
            return TryProperty(document.RootElement, "sourceVersion", out var sourceVersion)
                && sourceVersion.TryGetInt64(out var version)
                ? version
                : TryProperty(document.RootElement, "settlementVersion", out var settlementVersion)
                    && settlementVersion.TryGetInt64(out version) ? version : 0;
        }
        catch (JsonException) { return 0; }
    }

    private static (int? Year, int? Month, string? Label) ReadSourceDisplayEvidence(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return (null, null, null);
        try
        {
            using var document = JsonDocument.Parse(snapshot);
            var root = document.RootElement;
            int? year = TryProperty(root, "billingYear", out var yearValue) && yearValue.TryGetInt32(out var y) ? y : null;
            int? month = TryProperty(root, "billingMonth", out var monthValue) && monthValue.TryGetInt32(out var m) ? m : null;
            var stallNo = ReadString(root, "stallNo");
            var facility = ReadString(root, "facilityName");
            var period = year is > 0 && month is >= 1 and <= 12
                ? new DateOnly(year.Value, month.Value, 1).ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
                : null;
            var label = string.Join(" · ", new[] { period, facility, stallNo is null ? null : $"Stall {stallNo}" }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            return (year, month, string.IsNullOrWhiteSpace(label) ? null : label);
        }
        catch (JsonException) { return (null, null, null); }
    }

    private static string? SummarizeCalculationDetail(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return null;
        try
        {
            using var document = JsonDocument.Parse(snapshot);
            var root = document.RootElement;
            if (TryProperty(root, "previousReading", out var previous)
                && previous.TryGetDecimal(out var previousReading)
                && TryProperty(root, "currentReading", out var current)
                && current.TryGetDecimal(out var currentReading)
                && TryProperty(root, "consumption", out var consumption)
                && consumption.TryGetDecimal(out var consumed)
                && TryProperty(root, "ratePerKwh", out var rate)
                && rate.TryGetDecimal(out var ratePerKwh))
            {
                return string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Meter {previousReading:N2} to {currentReading:N2} · {consumed:N2} kWh at PHP {ratePerKwh:N2}/kWh");
            }
            return null;
        }
        catch (JsonException) { return null; }
    }

    private static SettlementAuthority? ReadSourceSettlementAuthority(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return null;
        try
        {
            using var document = JsonDocument.Parse(snapshot);
            return TryProperty(document.RootElement, "settlementAuthority", out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var raw)
                && Enum.IsDefined((SettlementAuthority)raw)
                    ? (SettlementAuthority)raw : null;
        }
        catch (JsonException) { return null; }
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

    private void EnsureDraftBusinessDateIsCurrent(WebCollectionDraft draft)
    {
        if (draft.BusinessDate != BusinessToday)
            throw Problem("The draft belongs to an earlier Philippine business date. Resume and refresh it before changing or reviewing it.", ResultStatus.Conflict);
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
        Guid? ContractId, DateTime? ContractUpdatedAt, Guid? PayorId, string? PayerName,
        Guid FacilityId, string FacilityName, string Section, int BillingYear, int BillingMonth,
        decimal PreviousReading, decimal CurrentReading, decimal Consumption, decimal RatePerKwh,
        decimal AssessedAmount, decimal CumulativeSettledEvidence, decimal OutstandingAmount,
        Guid ClassificationId, Guid PolicyId, string ClassificationName,
        RevenueInstrumentType Instrument, SettlementAuthority SettlementAuthority, string ChargeBasis);
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
