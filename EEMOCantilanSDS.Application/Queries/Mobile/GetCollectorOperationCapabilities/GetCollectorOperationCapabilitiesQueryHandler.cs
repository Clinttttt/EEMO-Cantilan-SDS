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
    public const string RateNotEffective = "RATE_NOT_EFFECTIVE";

    public async Task<Result<CollectorOperationCapabilitiesDto>> Handle(
        GetCollectorOperationCapabilitiesQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role != "Collector"
            || currentUser.CollectorId is not { } collectorId || collectorId == Guid.Empty)
            return Result<CollectorOperationCapabilitiesDto>.Forbidden();
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId)
            return Result<CollectorOperationCapabilitiesDto>.Forbidden();

        var built = await BuildForCollectorAsync(tenantId, collectorId, ct);
        return built is null
            ? Result<CollectorOperationCapabilitiesDto>.NotFound()
            : Result<CollectorOperationCapabilitiesDto>.Success(built);
    }

    /// <summary>
    /// The capability facts for one collector of a tenant, from server state only (no caller identity). The collector's own endpoint above and the Head's
    /// Revenue Sources readiness both read it, so what the Head is told an operation can do is exactly what that collector's app is told. Null when the
    /// collector is not in this tenant.
    /// </summary>
    public async Task<CollectorOperationCapabilitiesDto?> BuildForCollectorAsync(Guid tenantId, Guid collectorId, CancellationToken ct)
    {
        var collector = await db.CollectorUsers.AsNoTracking()
            .Include(x => x.FacilityAssignments)
            .SingleOrDefaultAsync(x => x.MunicipalityId == tenantId && x.Id == collectorId, ct);
        if (collector is null)
            return null;

        var today = clock.PhilippineToday;
        var assigned = (await db.CollectorOperationAssignments.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && x.CollectorId == collectorId)
                .Select(x => x.OperationCode)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        // The municipality's own name for each operation, where it has set one. The Collector app prints the name it is sent, so an
        // installed app shows it with no update; every code, status and reason below is unchanged. An office that has not adopted
        // tenant configuration gets the standard names exactly as before.
        var tenantNames = await Common.Onboarding.TenantOperationSettingsStore.DisplayNamesAsync(db, tenantId, ct);

        var operations = new List<CollectorOperationCapabilityDto>();
        foreach (var (code, standardName) in CollectorOperationAssignmentWorkflow.Catalog)
        {
            var name = tenantNames.TryGetValue(code, out var tenantName) ? tenantName : standardName;
            if (!assigned.Contains(code))
            {
                operations.Add(new(code, name, false, CollectorOperationCapabilityStatus.NotAssigned, false, []));
                continue;
            }

            // WCF has its own writer (WcfCollectionWorkflow.PostMobileAsync). Market Fees, Landing/Berthing, Transfer
            // Large Cattle and Vegetable/Fruit are governed configurable services (GovernedServiceWorkflow): collectible
            // only when their approved setup, instrument policy and this collector's document custody all exist.
            if (code is CollectorOperationCodes.Terminal or CollectorOperationCodes.FishMeatVendorFee or CollectorOperationCodes.WeightAndMeasure)
            {
                var codes = code == CollectorOperationCodes.Terminal
                    ? (await TerminalSectionSettingsStore.ReadAsync(db, tenantId, ct)).Where(x => x.IsEnabled).Select(x => OfficeCollectionWorkflow.SectionCode(x.Section)).ToArray()
                    : new[] { code == CollectorOperationCodes.FishMeatVendorFee ? RevenueClassificationCodes.FishMeatVendorFee : RevenueClassificationCodes.WeightAndMeasure };
                var expected = code == CollectorOperationCodes.Terminal ? RevenueInstrumentType.CashTicket : RevenueInstrumentType.OfficialReceipt;
                var policies = await (from p in db.RevenueClassificationPolicies.AsNoTracking()
                    join c in db.RevenueClassifications.AsNoTracking() on p.RevenueClassificationId equals c.Id
                    where c.MunicipalityId == tenantId && c.IsActive && codes.Contains(c.SemanticCode)
                        && p.BusinessContext == RevenuePolicyContext.Default && p.EffectiveDate <= today
                    select p).ToListAsync(ct);
                var policyReady = collector.IsActive && today >= OfficeCollectionWorkflow.EffectiveFrom && policies
                    .GroupBy(x => x.RevenueClassificationId).Any(g => g.OrderByDescending(x => x.EffectiveDate).First().PermittedInstrumentType == expected);
                // Policy alone is not weighing availability. The source remains server-authoritative.
                var rateReady = code != CollectorOperationCodes.WeightAndMeasure
                    || await WeighingRateSchedule.HasAvailableTypeAsync(db, tenantId, today, ct);
                var ready = policyReady && rateReady;
                operations.Add(new(code, name, true, ready ? CollectorOperationCapabilityStatus.Ready : CollectorOperationCapabilityStatus.NeedsPolicy,
                    ready, ready ? [] : [!collector.IsActive ? CollectorInactive : !policyReady ? PolicyNotEffective : RateNotEffective]));
                continue;
            }
            if (CollectorOperationCodes.IsSpaceObligation(code))
            {
                var classificationCode = code == CollectorOperationCodes.KanmanggaySpaceRental
                    ? RevenueClassificationCodes.KanmanggaySpaceRental : RevenueClassificationCodes.FiestaArawLotRental;
                var classificationId = await db.RevenueClassifications.AsNoTracking().Where(x =>
                    x.MunicipalityId == tenantId && x.IsActive && x.SemanticCode == classificationCode)
                    .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
                var instrument = await db.RevenueClassificationPolicies.AsNoTracking().Where(x =>
                    x.MunicipalityId == tenantId && x.RevenueClassificationId == classificationId &&
                    x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= today)
                    .OrderByDescending(x => x.EffectiveDate).Select(x => x.PermittedInstrumentType).FirstOrDefaultAsync(ct);
                var ready = collector.IsActive && instrument == RevenueInstrumentType.OfficialReceipt;
                operations.Add(new(code, name, true, ready ? CollectorOperationCapabilityStatus.Ready : CollectorOperationCapabilityStatus.NeedsPolicy,
                    ready, ready ? [] : [collector.IsActive ? PolicyNotEffective : CollectorInactive]));
                continue;
            }
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

        return new CollectorOperationCapabilitiesDto(collectorId, today, operations.Select(x => x with {
            Family = x.OperationCode == CollectorOperationCodes.Terminal ? CollectionFamily.Terminal
                : x.OperationCode == CollectorOperationCodes.Slaughterhouse ? CollectionFamily.Slaughterhouse
                : CollectorOperationCodes.IsSpaceObligation(x.OperationCode) || x.OperationCode == CollectorOperationCodes.VegetableFruitSpaceRental ? CollectionFamily.Space
                : x.OperationCode == CollectorOperationCodes.NpmDaily ? CollectionFamily.Rent : CollectionFamily.Market
        }).ToArray());
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
        // Monthly space settlement is independent of the governed Daily amount rule.
        if (entry.Code == CollectorOperationCodes.VegetableFruitSpaceRental && collectorActive && classificationId is { } monthlyId)
        {
            var monthlyInstrument = await db.RevenueClassificationPolicies.AsNoTracking().Where(x =>
                x.MunicipalityId == tenantId && x.RevenueClassificationId == monthlyId &&
                x.BusinessContext == RevenuePolicyContext.VegetableWholePayment && x.EffectiveDate <= today)
                .OrderByDescending(x => x.EffectiveDate).Select(x => x.PermittedInstrumentType).FirstOrDefaultAsync(ct);
            if (monthlyInstrument == RevenueInstrumentType.OfficialReceipt)
                return new(entry.Code, name, true, CollectorOperationCapabilityStatus.Ready, true, []);
        }
        var contexts = entry.ModeAware
            ? new[] { RevenuePolicyContext.VegetableDailyTransaction }
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

        var status = reasons.Count == 0 ? CollectorOperationCapabilityStatus.Ready : reasons[0].Status;
        return new(code, name, true, status, status == CollectorOperationCapabilityStatus.Ready,
            reasons.Select(x => x.Code).ToArray());
    }
}
