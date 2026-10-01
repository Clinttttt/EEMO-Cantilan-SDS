using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Head/Admin request to make one Water obligation collectible on Collector Mobile, with the attested checklist.</summary>
public sealed record WcfActivationRequest(
    Guid UtilityBillId,
    long ExpectedWaterSourceVersion,
    SettlementCutoverReconciliationEvidence Evidence);

/// <summary>
/// "Activate for Mobile collection" for one WCF Water obligation — a narrow route onto the existing source-scoped cutover
/// control plane (<see cref="SettlementCutoverWorkflow"/>): Legacy → Pending Cutover → frozen opening position → Canonical.
/// </summary>
/// <remarks>
/// It never activates in bulk, never touches Electricity or rent, and accepts only a Water part that is still an unsettled
/// direct approved assessment — so the frozen opening position is that assessment with nothing previously settled, and no
/// historical payment is re-read as canonical money. Readiness is checked with the office's attested evidence BEFORE any
/// state changes: while a real blocker remains, nothing is written. Each later step is the workflow's own command, with its
/// own serializable transaction and version checks; a retry resumes from whichever step the source reached.
/// </remarks>
public sealed class WcfActivationWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    SettlementCutoverWorkflow cutover)
{
    private const string LegacyMarker = "Mark this exact source Pending Cutover before quiescing its legacy writers.";

    private static SettlementCutoverScope ScopeOf(Guid billId) =>
        new(CollectionSourceKind.UtilityBill, billId, CollectionSourcePart.Water);

    /// <summary>The dry-run the office reviews: every blocker except the Legacy state this action itself resolves.</summary>
    public async Task<Result<SettlementCutoverReadinessDto>> GetReadinessAsync(
        Guid utilityBillId, SettlementCutoverReconciliationEvidence? evidence, CancellationToken ct = default)
    {
        if (await EligibilityProblemAsync(utilityBillId, ct) is { } problem)
            return Result<SettlementCutoverReadinessDto>.Failure(problem.Message, problem.Status);
        var readiness = await cutover.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(ScopeOf(utilityBillId), evidence), ct);
        if (!readiness.IsSuccess || readiness.Value is null) return readiness;
        var blockers = readiness.Value.BlockingReasons.Where(b => b != LegacyMarker).ToList();
        return Result<SettlementCutoverReadinessDto>.Success(readiness.Value with { BlockingReasons = blockers, Ready = blockers.Count == 0 });
    }

    public async Task<Result<SettlementCutoverOutcomeDto>> ActivateAsync(WcfActivationRequest request, CancellationToken ct = default)
    {
        if (request.Evidence is null)
            return Result<SettlementCutoverOutcomeDto>.Failure("The attested reconciliation checklist is required.", ResultStatus.Invalid);
        if (await EligibilityProblemAsync(request.UtilityBillId, ct) is { } problem)
            return Result<SettlementCutoverOutcomeDto>.Failure(problem.Message, problem.Status);
        var scope = ScopeOf(request.UtilityBillId);
        var bill = await db.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == request.UtilityBillId, ct);

        if (bill.WaterSettlementAuthorityState == SettlementAuthority.Legacy)
        {
            if (bill.WaterSourceVersion != request.ExpectedWaterSourceVersion)
                return Result<SettlementCutoverOutcomeDto>.Failure(
                    "The Water obligation changed after it was reviewed. Refresh and review it again.", ResultStatus.Conflict);
            // Nothing is written while any blocker other than the Legacy state itself remains.
            var dryRun = await GetReadinessAsync(request.UtilityBillId, request.Evidence, ct);
            if (!dryRun.IsSuccess || dryRun.Value is null)
                return Result<SettlementCutoverOutcomeDto>.Failure(dryRun.Error ?? "Readiness could not be evaluated.", dryRun.Status);
            if (!dryRun.Value.Ready)
                return Result<SettlementCutoverOutcomeDto>.Failure(
                    "Activation is blocked: " + string.Join("; ", dryRun.Value.BlockingReasons), ResultStatus.Conflict);
            var begun = await cutover.BeginPendingCutoverAsync(scope, ct);
            if (!begun.IsSuccess) return begun;
        }

        var frozen = await db.CollectionSettlementCutovers.AsNoTracking().AnyAsync(x =>
            x.SourceKind == CollectionSourceKind.UtilityBill && x.SourceId == request.UtilityBillId
            && x.SourcePart == CollectionSourcePart.Water, ct);
        if (!frozen)
        {
            var readiness = await cutover.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, request.Evidence), ct);
            if (!readiness.IsSuccess || readiness.Value is null)
                return Result<SettlementCutoverOutcomeDto>.Failure(readiness.Error ?? "Readiness could not be evaluated.", readiness.Status);
            var freeze = await cutover.FreezeOpeningPositionAsync(new SettlementCutoverFreezeRequest(
                scope, readiness.Value.SourceVersion, readiness.Value.ReadinessFingerprint, request.Evidence), ct);
            if (!freeze.IsSuccess) return freeze;
        }

        var current = await db.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == request.UtilityBillId, ct);
        return await cutover.ActivateCanonicalAsync(scope, current.WaterSourceVersion, ct);
    }

    /// <summary>
    /// Only a Head/Admin, only this tenant's Water part, and — while it is still Legacy — only an unsettled direct approved
    /// assessment with an amount. A source already in cutover may resume; a Canonical one is already collectible.
    /// </summary>
    private async Task<(string Message, ResultStatus Status)?> EligibilityProblemAsync(Guid utilityBillId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role is not ("Admin" or "SuperAdmin"))
            return ("Only the Head or an Administrator can activate a Water obligation.", ResultStatus.Forbidden);
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return ("Only the Head or an Administrator can activate a Water obligation.", ResultStatus.Forbidden);
        var bill = await db.UtilityBills.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == utilityBillId, ct);
        if (bill is null)
            return ("The Water obligation was not found.", ResultStatus.NotFound);
        if (bill.WaterSettlementAuthorityState == SettlementAuthority.Canonical)
            return ("This Water obligation is already active for Mobile collection.", ResultStatus.Conflict);
        if (bill.WaterSettlementAuthorityState == SettlementAuthority.Legacy)
        {
            if (bill.WaterCalculationBasis != UtilityCalculationBasis.DirectApproved || bill.WaterCharge <= 0m)
                return ("Only a Water obligation with a direct approved amount can be activated here.", ResultStatus.Invalid);
            if (bill.WaterStatus != PaymentStatus.Unpaid || bill.WaterAmountPaid > 0m)
                return ("Settlement has begun on this Water obligation under the legacy records; it needs the full reconciled cutover, not this action.", ResultStatus.Conflict);
        }
        return null;
    }
}
