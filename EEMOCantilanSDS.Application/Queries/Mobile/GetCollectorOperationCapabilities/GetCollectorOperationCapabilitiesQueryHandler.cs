using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
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

            // WCF has its own writer (WcfCollectionWorkflow.PostMobileAsync). Market Fees, Landing/Berthing, Transfer
            // Large Cattle and Vegetable/Fruit are governed configurable services (GovernedServiceWorkflow): collectible
            // only when their approved setup, instrument policy and this collector's document custody all exist.
            if (GovernedServiceCatalog.Find(code) is { } governed)
            {
                operations.Add(await EvaluateGovernedAsync(tenantId, collector.IsActive, collectorId, governed, name, today, ct));
                continue;
            }
            // Anything else has no approved Mobile writer, so an assignment cannot make it collectible.
            if (code != CollectorOperationCodes.Wcf)
            {
                operations.Add(new(code, name, true, CollectorOperationCapabilityStatus.Unsupported, false, [NoMobileWriter]));
                continue;
            }

            operations.Add(await EvaluateWcfAsync(tenantId, collector.IsActive, collectorId, code, name, today, ct));
        }

        return Result<CollectorOperationCapabilitiesDto>.Success(new(collectorId, today, operations));
    }

    public const string ServiceSetupRequired = "SERVICE_SETUP_REQUIRED";
    public const string ServiceDisabled = "SERVICE_DISABLED";
    public const string MobileChannelDisabled = "MOBILE_CHANNEL_DISABLED";
    public const string NoAssignedDocument = "NO_ASSIGNED_DOCUMENT";

    /// <summary>
    /// Mirrors the gates GovernedServiceWorkflow.PostMobileAsync enforces. Setup, channel and policy problems are
    /// NeedsPolicy; a missing document in this collector's custody is NeedsDocument. A mode-aware service is Ready
    /// when at least one of its modes can be collected.
    /// </summary>
    private async Task<CollectorOperationCapabilityDto> EvaluateGovernedAsync(
        Guid tenantId, bool collectorActive, Guid collectorId, GovernedServiceCatalog.Entry entry,
        string name, DateOnly today, CancellationToken ct)
    {
        var reasons = new List<(CollectorOperationCapabilityStatus Status, string Code)>();
        if (!collectorActive)
            reasons.Add((CollectorOperationCapabilityStatus.AssignedButInactive, CollectorInactive));

        var service = await db.GovernedServices.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.OperationCode == entry.Code, ct);
        var versions = service is null ? [] : await db.GovernedServiceSettings.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.GovernedServiceId == service.Id).ToListAsync(ct);
        var setting = GovernedServiceSetting.Resolve(versions, today);
        if (setting is null)
            reasons.Add((CollectorOperationCapabilityStatus.NeedsPolicy, ServiceSetupRequired));
        else if (!setting.IsEnabled)
            reasons.Add((CollectorOperationCapabilityStatus.NeedsPolicy, ServiceDisabled));
        else if (!setting.MobileEnabled)
            reasons.Add((CollectorOperationCapabilityStatus.NeedsPolicy, MobileChannelDisabled));

        var classificationId = await db.RevenueClassifications.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.SemanticCode == entry.ClassificationCode && x.IsActive)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        var contexts = entry.ModeAware
            ? new[] { RevenuePolicyContext.VegetableWholePayment, RevenuePolicyContext.VegetableDailyTransaction }
            : new[] { RevenuePolicyContext.Default };
        var instruments = new List<RevenueInstrumentType>();
        foreach (var context in contexts)
        {
            var instrument = classificationId is { } id
                ? await db.RevenueClassificationPolicies.AsNoTracking()
                    .Where(x => x.MunicipalityId == tenantId && x.RevenueClassificationId == id
                        && x.BusinessContext == context && x.EffectiveDate <= today)
                    .OrderByDescending(x => x.EffectiveDate).Select(x => x.PermittedInstrumentType).FirstOrDefaultAsync(ct)
                : null;
            if (instrument is { } resolved) instruments.Add(resolved);
        }
        if (instruments.Count == 0)
            reasons.Add((CollectorOperationCapabilityStatus.NeedsPolicy, PolicyNotEffective));

        // Present custody, not history: assigned to this collector under an unreturned assignment interval.
        var held = await (
                from document in db.AccountableDocuments.AsNoTracking()
                join assignment in db.AccountableFormAssignments.AsNoTracking()
                    on new { document.MunicipalityId, DocumentId = document.Id }
                    equals new { assignment.MunicipalityId, DocumentId = assignment.AccountableDocumentId }
                where document.MunicipalityId == tenantId
                    && document.State == AccountableDocumentState.Assigned
                    && document.AssignedUserId == collectorId
                    && assignment.AssignedUserId == collectorId
                    && assignment.ReturnedAtUtc == null
                select document.InstrumentType)
            .Distinct().ToListAsync(ct);
        if (!instruments.Any(held.Contains))
            reasons.Add((CollectorOperationCapabilityStatus.NeedsDocument, NoAssignedDocument));

        var status = reasons.Count == 0 ? CollectorOperationCapabilityStatus.Ready : reasons[0].Status;
        return new(entry.Code, name, true, status, status == CollectorOperationCapabilityStatus.Ready,
            reasons.Select(x => x.Code).ToArray());
    }

    /// <summary>Mirrors the gates WcfCollectionWorkflow.PostMobileAsync enforces, in the same terms.</summary>
    private async Task<CollectorOperationCapabilityDto> EvaluateWcfAsync(
        Guid tenantId, bool collectorActive, Guid collectorId,
        string code, string name, DateOnly today, CancellationToken ct)
    {
        var reasons = new List<(CollectorOperationCapabilityStatus Status, string Code)>();
        if (!collectorActive)
            reasons.Add((CollectorOperationCapabilityStatus.AssignedButInactive, CollectorInactive));
        // WCF is a utility operation (IA-053): the WCF assignment authorizes the collector, exactly as the writer checks.
        // The current Water source is an NPM-bound UtilityBill, but that is source context, not a collector gate.

        // The OPERATION's readiness, not whether a payor's Water row was prepared: WCF is collectible once the office has
        // enabled WCF Mobile collection (new sources are canonical from birth), or where a migrated canonical source exists.
        // No outstanding row is workload, never an authorization failure.
        var enabled = await db.CollectorOperationActivations.AsNoTracking().AnyAsync(x =>
                x.MunicipalityId == tenantId && x.OperationCode == CollectorOperationCodes.Wcf, ct)
            || await db.UtilityBills.AsNoTracking().AnyAsync(x =>
                x.MunicipalityId == tenantId
                && x.WaterSettlementAuthorityState == SettlementAuthority.Canonical
                && x.Stall!.Facility!.Code == FacilityCode.NPM, ct);
        if (!enabled)
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
