using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities;

/// <summary>
/// Derives, from server facts only, whether each assigned non-facility operation is collectible now. It never grants
/// authority: the posting workflow still revalidates everything. An operation without an approved Mobile writer is
/// reported Unsupported however it is assigned; nothing here invents a generic collection form.
/// </summary>
public sealed class GetCollectorOperationCapabilitiesQueryHandler(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock clock)
    : IRequestHandler<GetCollectorOperationCapabilitiesQuery, Result<CollectorOperationCapabilitiesDto>>
{
    public const string NoMobileWriter = "NO_MOBILE_WRITER";
    public const string CollectorInactive = "COLLECTOR_INACTIVE";
    public const string NpmFacilityRequired = "NPM_FACILITY_REQUIRED";
    public const string NoCanonicalSource = "NO_CANONICAL_SOURCE";
    public const string PolicyNotEffective = "POLICY_NOT_EFFECTIVE";
    public const string NoAssignedCashTicket = "NO_ASSIGNED_CASH_TICKET";

    public async Task<Result<CollectorOperationCapabilitiesDto>> Handle(
        GetCollectorOperationCapabilitiesQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector"
            || currentUser.CollectorId is not { } collectorId || collectorId == Guid.Empty)
            return Result<CollectorOperationCapabilitiesDto>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<CollectorOperationCapabilitiesDto>.Forbidden();

        var collector = await db.CollectorUsers.AsNoTracking()
            .Include(x => x.FacilityAssignments)
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == collectorId, ct);
        if (collector is null)
            return Result<CollectorOperationCapabilitiesDto>.NotFound();

        var today = clock.PhilippineToday;
        var assigned = (await db.CollectorOperationAssignments.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && x.CollectorId == collectorId)
                .Select(x => x.OperationCode)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var operations = new List<CollectorOperationCapabilityDto>();
        foreach (var (code, name) in CollectorOperationAssignmentWorkflow.Catalog)
        {
            if (!assigned.Contains(code))
            {
                operations.Add(new(code, name, false, CollectorOperationCapabilityStatus.NotAssigned, false, []));
                continue;
            }

            // WCF is the only non-facility operation with an approved Collector Mobile writer on this baseline
            // (WcfCollectionWorkflow.PostMobileAsync). Market Fees, Vegetable/Fruit, Landing/Berthing and Transfer
            // Large Cattle have no source model or writer yet, so an assignment cannot make them collectible.
            if (code != CollectorOperationCodes.Wcf)
            {
                operations.Add(new(code, name, true, CollectorOperationCapabilityStatus.Unsupported, false, [NoMobileWriter]));
                continue;
            }

            operations.Add(await EvaluateWcfAsync(tenantId, collector.IsActive,
                collector.FacilityAssignments.Any(x => x.FacilityCode == FacilityCode.NPM), collectorId, code, name, today, ct));
        }

        return Result<CollectorOperationCapabilitiesDto>.Success(new(collectorId, today, operations));
    }

    /// <summary>Mirrors the gates WcfCollectionWorkflow.PostMobileAsync enforces, in the same terms.</summary>
    private async Task<CollectorOperationCapabilityDto> EvaluateWcfAsync(
        Guid tenantId, bool collectorActive, bool hasNpmFacility, Guid collectorId,
        string code, string name, DateOnly today, CancellationToken ct)
    {
        var reasons = new List<(CollectorOperationCapabilityStatus Status, string Code)>();
        if (!collectorActive)
            reasons.Add((CollectorOperationCapabilityStatus.AssignedButInactive, CollectorInactive));
        // The current Water source is the NPM-bound UtilityBill, so its writer also requires NPM facility authorization.
        if (!hasNpmFacility)
            reasons.Add((CollectorOperationCapabilityStatus.AssignedButInactive, NpmFacilityRequired));

        var canonicalWater = await db.UtilityBills.AsNoTracking().AnyAsync(x =>
            x.MunicipalityId == tenantId
            && x.WaterSettlementAuthorityState == SettlementAuthority.Canonical
            && x.Stall!.Facility!.Code == FacilityCode.NPM, ct);
        if (!canonicalWater)
            reasons.Add((CollectorOperationCapabilityStatus.PendingCutover, NoCanonicalSource));

        var classificationId = await db.RevenueClassifications.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.SemanticCode == RevenueClassificationCodes.Wcf && x.IsActive)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(ct);
        var instrument = classificationId is { } id
            ? await db.RevenueClassificationPolicies.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && x.RevenueClassificationId == id
                    && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= today)
                .OrderByDescending(x => x.EffectiveDate)
                .Select(x => x.PermittedInstrumentType)
                .FirstOrDefaultAsync(ct)
            : null;
        if (instrument != RevenueInstrumentType.CashTicket)
            reasons.Add((CollectorOperationCapabilityStatus.NeedsPolicy, PolicyNotEffective));

        // Present custody, not history: the ticket must be Assigned to this collector under an unreturned assignment.
        var holdsTicket = await (
                from document in db.AccountableDocuments.AsNoTracking()
                join assignment in db.AccountableFormAssignments.AsNoTracking()
                    on new { document.MunicipalityId, DocumentId = document.Id }
                    equals new { assignment.MunicipalityId, DocumentId = assignment.AccountableDocumentId }
                where document.MunicipalityId == tenantId
                    && document.InstrumentType == RevenueInstrumentType.CashTicket
                    && document.State == AccountableDocumentState.Assigned
                    && document.AssignedUserId == collectorId
                    && assignment.AssignedUserId == collectorId
                    && assignment.ReturnedAtUtc == null
                select document.Id)
            .AnyAsync(ct);
        if (!holdsTicket)
            reasons.Add((CollectorOperationCapabilityStatus.NeedsDocument, NoAssignedCashTicket));

        var status = reasons.Count == 0 ? CollectorOperationCapabilityStatus.Ready : reasons[0].Status;
        return new(code, name, true, status, status == CollectorOperationCapabilityStatus.Ready,
            reasons.Select(x => x.Code).ToArray());
    }
}
