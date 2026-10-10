using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

public sealed partial class CollectionComposerWorkflow
{
    private async Task<bool> CanCollectFineAsync(FineAssessment fine, CancellationToken ct)
    {
        if (municipality.MunicipalityId == Guid.Empty || currentUser.MunicipalityId != municipality.MunicipalityId ||
            !currentUser.IsAuthenticated || currentUser.UserId is null) return false;
        if (currentUser.Role is "SuperAdmin" or "Admin") return true;
        if (currentUser.Role != "Collector" || currentUser.CollectorId is not { } id) return false;
        var collector = await db.CollectorUsers.AsNoTracking().Include(c => c.FacilityAssignments)
            .SingleOrDefaultAsync(c => c.MunicipalityId == fine.MunicipalityId && c.Id == id && c.IsActive, ct);
        if (collector is null) return false;
        if (fine.ContractId is { } contractId)
        {
            var contract = await db.Contracts.AsNoTracking().Include(c => c.Stall!).ThenInclude(s => s.Facility)
                .SingleOrDefaultAsync(c => c.MunicipalityId == fine.MunicipalityId && c.Id == contractId, ct);
            return contract?.Stall?.Facility is { } facility && collector.FacilityAssignments.Any(a => a.FacilityCode == facility.Code);
        }
        var account = await db.ObligationAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.MunicipalityId == fine.MunicipalityId && a.Id == fine.ObligationAccountId, ct);
        var operation = account is null ? null : ObligationAccount.OperationCodeFor(account.Kind);
        return operation is not null && await db.CollectorOperationAssignments.AnyAsync(a => a.MunicipalityId == fine.MunicipalityId && a.CollectorId == id && a.OperationCode == operation, ct);
    }
    private async Task<FineSettlementFacts> FineFactsAsync(Guid tenant, Guid id, DateOnly date, bool tracked, CancellationToken ct)
    {
        FineSettlementFacts? facts;
        try { facts = await new FineAssessmentCollectionSource(db).LoadAsync(tenant, id, date, tracked, ct); }
        catch (InvalidOperationException ex) { throw Problem(ex.Message, ResultStatus.Conflict); }
        if (facts is null) throw Problem("The fine assessment was not found in this tenant.", ResultStatus.NotFound);
        if (!await CanCollectFineAsync(facts.Assessment, ct)) throw Problem("Collection authority does not cover this liable source account.", ResultStatus.Forbidden);
        return facts;
    }
    private async Task<Result<T>> RunFineAsync<T>(Func<Actor, Task<Result<T>>> action)
    {
        if (currentUser.Role != "Collector") return await Run(action, default);
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } id || id == Guid.Empty || currentUser.CollectorId is null ||
            municipality.MunicipalityId == Guid.Empty || currentUser.MunicipalityId != municipality.MunicipalityId) return Result<T>.Forbidden();
        return await ExecuteAsActorAsync(new(id, municipality.MunicipalityId, currentUser.Username ?? "Collector", "Collector"), action);
    }
    private Task<Result<T>> RunFineDraftAsync<T>(Guid draftId, Func<Actor, Task<Result<T>>> action, CancellationToken ct) => RunFineAsync(async actor =>
    {
        if (actor.Role == "Collector")
        {
            var draft = await FindOwnedDraftAsync(draftId, actor.MunicipalityId, actor.UserId, false, ct);
            if (draft is null) return Result<T>.NotFound();
            var lines = await DraftLinesAsync(actor.MunicipalityId, draftId, ct);
            if (lines.Count != 1 || lines[0].SourceKind != CollectionSourceKind.FineAssessment || lines[0].SourceId is not { } assessmentId)
                return Result<T>.Forbidden();
            var fine = await db.FineAssessments.AsNoTracking().SingleOrDefaultAsync(a => a.MunicipalityId == actor.MunicipalityId && a.Id == assessmentId, ct);
            if (fine is null || !await CanCollectFineAsync(fine, ct)) return Result<T>.Forbidden();
        }
        return await action(actor);
    });
    public Task<Result<FineSettlementQuoteDto>> QuoteFineAsync(Guid id, CancellationToken ct = default) => RunFineAsync(async actor =>
        Result<FineSettlementQuoteDto>.Success((await FineFactsAsync(actor.MunicipalityId, id, BusinessToday, false, ct)).Quote));
    public Task<Result<IReadOnlyList<FineSettlementHistoryDto>>> FineHistoryAsync(Guid id, CancellationToken ct = default) => RunFineAsync(async actor =>
    {
        var fine = await db.FineAssessments.AsNoTracking().SingleOrDefaultAsync(a=>a.MunicipalityId==actor.MunicipalityId&&a.Id==id,ct);
        if(fine is null) return Result<IReadOnlyList<FineSettlementHistoryDto>>.NotFound();
        if(!await CanCollectFineAsync(fine,ct)) return Result<IReadOnlyList<FineSettlementHistoryDto>>.Forbidden();
        var rows = await (from allocation in db.CollectionAllocations.AsNoTracking()
            join line in db.CollectionLines.AsNoTracking() on new {allocation.MunicipalityId,Id=allocation.CollectionLineId} equals new {line.MunicipalityId,line.Id}
            join collection in db.Collections.AsNoTracking() on line.CollectionId equals collection.Id
            where allocation.MunicipalityId==actor.MunicipalityId && allocation.SourceKind==CollectionSourceKind.FineAssessment && allocation.SourceId==id
            orderby collection.BusinessDate descending,collection.RecordedAtUtc descending
            select new {allocation.Id,CollectionId=collection.Id,collection.ReferenceCode,collection.BusinessDate,allocation.Amount}).ToListAsync(ct);
        var ids=rows.Select(r=>r.Id).ToArray();
        var corrections=await db.CollectionCorrectionAllocations.AsNoTracking().Where(a=>a.MunicipalityId==actor.MunicipalityId&&ids.Contains(a.OriginalAllocationId))
            .Select(a=>new {a.OriginalAllocationId,a.FinancialEffectAmount}).ToListAsync(ct);
        return Result<IReadOnlyList<FineSettlementHistoryDto>>.Success(rows.Select(r=>new FineSettlementHistoryDto(r.CollectionId,r.ReferenceCode,r.BusinessDate,r.Amount,
            r.Amount+corrections.Where(a=>a.OriginalAllocationId==r.Id).Sum(a=>a.FinancialEffectAmount))).ToList());
    });
    public Task<Result<EcfCollectionDraftDto>> CurrentFineDraftAsync(Guid assessmentId, CancellationToken ct = default) => RunFineAsync(async actor =>
    {
        var facts = await FineFactsAsync(actor.MunicipalityId, assessmentId, BusinessToday, false, ct);
        var draft = await CurrentDraftTrackedAsync(actor, ct);
        if (draft is null) return Result<EcfCollectionDraftDto>.NotFound();
        var lines = await DraftLinesAsync(actor.MunicipalityId, draft.Id, ct);
        if (actor.Role == "Collector" && (lines.Count != 1 || lines[0].SourceKind != CollectionSourceKind.FineAssessment || lines[0].SourceId != assessmentId))
            throw Problem("Complete or discard the current draft before this standalone fine payment.", ResultStatus.Conflict);
        EnsureDraftBusinessDateIsCurrent(draft);
        if (draft.InstrumentFamily != RevenueInstrumentType.OfficialReceipt) throw Problem("Fine payment requires an OR-compatible draft.", ResultStatus.Conflict);
        await EnsureFineContextAsync(facts.Assessment, await DraftAllocationsAsync(actor.MunicipalityId,draft.Id,ct), actor.MunicipalityId,ct);
        if (draft.PayerNameSnapshot != facts.Assessment.Occupant) throw Problem("The draft does not preserve the original liable occupant.",ResultStatus.Conflict);
        return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft,ct));
    });
    public Task<Result<EcfCollectionDraftDto>> GetFineDraftAsync(Guid id, CancellationToken ct = default) => RunFineDraftAsync(id, async actor =>
    {
        var draft = await FindOwnedDraftAsync(id, actor.MunicipalityId, actor.UserId, false, ct);
        return draft is null ? Result<EcfCollectionDraftDto>.NotFound() : Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));
    }, ct);
    public Task<Result<EcfCollectionDraftDto>> AddFineAssessmentAsync(AddFineAssessmentDraftRequest r, CancellationToken ct = default) => RunFineAsync(async actor =>
    {
        var facts = await FineFactsAsync(actor.MunicipalityId, r.AssessmentId, BusinessToday, false, ct);
        if (!facts.Quote.CanSettle || r.Amount != facts.Quote.Remaining || r.QuoteToken != facts.Quote.QuoteToken)
            throw Problem("Obtain a current quote and pay the entire fine balance.", ResultStatus.Conflict);
        var draft = await CurrentDraftTrackedAsync(actor, ct);
        if (draft is null)
        {
            if (r.ExpectedRevision.HasValue) throw Problem("The current draft no longer exists.", ResultStatus.Conflict);
            draft = WebCollectionDraft.Create(actor.MunicipalityId, actor.UserId, BusinessToday, null, facts.Assessment.Occupant,
                RevenueInstrumentType.OfficialReceipt, null, actor.Username);
            db.WebCollectionDrafts.Add(draft);
        }
        else
        {
            var existingLines = await DraftLinesAsync(actor.MunicipalityId, draft.Id, ct);
            if (!r.ExpectedRevision.HasValue && existingLines.Count == 1 && existingLines[0].SourceKind == CollectionSourceKind.FineAssessment && existingLines[0].SourceId == r.AssessmentId)
                return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct)); // Recover a lost create/review response; final posting revalidates the quote.
            if (r.ExpectedRevision is not { } revision) throw Problem("ExpectedRevision is required for the current draft.", ResultStatus.Conflict);
            EnsureExpectedRevision(draft, revision); EnsureDraftBusinessDateIsCurrent(draft);
            if (actor.Role == "Collector") throw Problem("Collector fine payments use a standalone fine draft.", ResultStatus.Conflict);
            if (draft.InstrumentFamily != RevenueInstrumentType.OfficialReceipt) throw Problem("Fine payment requires an OR-compatible draft.", ResultStatus.Conflict);
            await EnsureFineContextAsync(facts.Assessment, await DraftAllocationsAsync(actor.MunicipalityId, draft.Id, ct), actor.MunicipalityId, ct);
            if (draft.PayerNameSnapshot != facts.Assessment.Occupant) throw Problem("The draft does not preserve the original liable occupant.", ResultStatus.Conflict);
            if (existingLines.Any(l => l.SourceKind == CollectionSourceKind.FineAssessment && l.SourceId == r.AssessmentId))
                throw Problem("This assessment is already in the draft.", ResultStatus.Conflict);
            draft.AdvanceRevision(revision, actor.Username);
        }
        var lines = await DraftLinesAsync(actor.MunicipalityId, draft.Id, ct);
        if (lines.Any(l => l.SourceKind == CollectionSourceKind.FineAssessment && l.SourceId == r.AssessmentId))
            throw Problem("This assessment is already in the draft.", ResultStatus.Conflict);
        var line = WebCollectionDraftLine.Create(actor.MunicipalityId, draft.Id, lines.Count, facts.Classification.Id, facts.Policy.Id,
            r.Amount, facts.Policy.DisplayName, CollectionSourceKind.FineAssessment, r.AssessmentId, null, facts.Snapshot, actor.Username);
        db.WebCollectionDraftLines.Add(line);
        db.WebCollectionDraftAllocations.Add(WebCollectionDraftAllocation.Create(actor.MunicipalityId, line.Id,
            CollectionSourceKind.FineAssessment, r.AssessmentId, null, r.Amount, facts.Snapshot, actor.Username));
        await db.SaveChangesAsync(ct);
        return Result<EcfCollectionDraftDto>.Success(await ToDraftDtoAsync(draft, ct));
    });
    private async Task EnsureFineContextAsync(FineAssessment fine, IReadOnlyList<WebCollectionDraftAllocation> others, Guid tenant, CancellationToken ct)
    {
        foreach (var allocation in others)
        {
            bool same;
            if (allocation.SourceKind == CollectionSourceKind.FineAssessment)
                same = await db.FineAssessments.AnyAsync(a => a.MunicipalityId == tenant && a.Id == allocation.SourceId &&
                    a.AccountKind == fine.AccountKind && a.ContractId == fine.ContractId && a.ObligationAccountId == fine.ObligationAccountId, ct);
            else if (allocation.SourceKind == CollectionSourceKind.PaymentRecord && fine.ContractId.HasValue)
                same = ReadPayerEvidence(allocation.SourceSnapshot).ContractId == fine.ContractId;
            else if (allocation.SourceKind == CollectionSourceKind.ObligationPeriod && fine.ObligationAccountId.HasValue)
                same = await db.ObligationPeriods.AnyAsync(p => p.MunicipalityId == tenant && p.Id == allocation.SourceId && p.ObligationAccountId == fine.ObligationAccountId, ct);
            else same = false;
            if (!same) throw Problem("Rental and fine lines must identify the same liable registration and an approved OR context.", ResultStatus.Conflict);
        }
    }
}
