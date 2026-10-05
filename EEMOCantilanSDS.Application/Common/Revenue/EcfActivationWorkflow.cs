using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>Head/Admin request to make one Electricity (ECF) obligation collectible through the canonical composer, with the attested checklist.</summary>
public sealed record EcfActivationRequest(
    Guid UtilityBillId,
    long ExpectedElectricitySourceVersion,
    SettlementCutoverReconciliationEvidence Evidence);

public sealed record EcfActivationReadinessRequest(Guid UtilityBillId, SettlementCutoverReconciliationEvidence? Evidence);

/// <summary>
/// Legacy ECF migration for one historical Electricity source: the narrow, Head-controlled route onto the existing source-scoped
/// cutover control plane (<see cref="SettlementCutoverWorkflow"/>) - Legacy, then Pending Cutover, then frozen opening position,
/// then Canonical. The same shape as <see cref="WcfActivationWorkflow"/>, for the Electricity part.
/// </summary>
/// <remarks>
/// It never migrates in bulk and never touches Water or rent, and it never writes authority state itself: every step is the cutover
/// workflow's own command with its own serializable transaction and version check. Readiness is evaluated with the office's attested
/// evidence BEFORE any state changes, so while a real blocker remains nothing is written; a retry resumes from whichever step the
/// source reached. The cutover freezes the bill's real legacy settled amount as the opening position, so no historical payment is
/// re-read as canonical money and nothing is collected twice.
/// </remarks>
public sealed class EcfActivationWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    SettlementCutoverWorkflow cutover)
{
    private const string LegacyMarker = "Mark this exact source Pending Cutover before quiescing its legacy writers.";

    private static SettlementCutoverScope ScopeOf(Guid billId) =>
        new(CollectionSourceKind.UtilityBill, billId, CollectionSourcePart.Electricity);

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

    public async Task<Result<SettlementCutoverOutcomeDto>> ActivateAsync(EcfActivationRequest request, CancellationToken ct = default)
    {
        if (request.Evidence is null)
            return Result<SettlementCutoverOutcomeDto>.Failure("The attested reconciliation checklist is required.", ResultStatus.Invalid);
        if (await EligibilityProblemAsync(request.UtilityBillId, ct) is { } problem)
            return Result<SettlementCutoverOutcomeDto>.Failure(problem.Message, problem.Status);
        var scope = ScopeOf(request.UtilityBillId);
        var bill = await db.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == request.UtilityBillId, ct);

        if (bill.ElectricitySettlementAuthorityState == SettlementAuthority.Legacy)
        {
            if (bill.ElectricitySourceVersion != request.ExpectedElectricitySourceVersion)
                return Result<SettlementCutoverOutcomeDto>.Failure(
                    "The Electricity obligation changed after it was reviewed. Refresh and review it again.", ResultStatus.Conflict);
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
            && x.SourcePart == CollectionSourcePart.Electricity, ct);
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
        return await cutover.ActivateCanonicalAsync(scope, current.ElectricitySourceVersion, ct);
    }

    /// <summary>
    /// Only a Head/Admin, only this tenant's Electricity part, and only one with an assessment to migrate. A source already in
    /// cutover may resume; a Canonical one is already collectible.
    /// </summary>
    private async Task<(string Message, ResultStatus Status)?> EligibilityProblemAsync(Guid utilityBillId, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role is not ("Admin" or "SuperAdmin"))
            return ("Only the Head or an Administrator can activate an Electricity obligation.", ResultStatus.Forbidden);
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return ("Only the Head or an Administrator can activate an Electricity obligation.", ResultStatus.Forbidden);
        var bill = await db.UtilityBills.AsNoTracking().SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == utilityBillId, ct);
        if (bill is null)
            return ("The Electricity obligation was not found.", ResultStatus.NotFound);
        if (bill.ElectricitySettlementAuthorityState == SettlementAuthority.Canonical)
            return ("This Electricity obligation is already active for canonical collection.", ResultStatus.Conflict);
        if (bill.ElectricitySettlementAuthorityState == SettlementAuthority.Legacy && bill.ElecCharge <= 0m)
            return ("This Electricity record has no assessment to migrate.", ResultStatus.Invalid);
        return null;
    }
}
