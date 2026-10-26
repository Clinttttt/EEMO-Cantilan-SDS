using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Settings;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Settings.GetOperationSetup;

/// <summary>
/// Where each revenue operation stands, from backend facts only: its listing row, its revenue source and effective policy, and what each assigned
/// collector's app is told - the very same evaluation the Collector app reads, so the Head is never told "ready" about something a collector cannot
/// collect. Read-only; it grants nothing.
/// </summary>
public sealed class GetOperationSetupQueryHandler(
    IAppDbContext db, ICurrentUserService currentUser, ICurrentMunicipalityAccessor municipality, IClock clock)
    : IRequestHandler<GetOperationSetupQuery, Result<OperationSetupOverviewDto>>
{
    public async Task<Result<OperationSetupOverviewDto>> Handle(GetOperationSetupQuery request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role is not ("SuperAdmin" or "Admin")
            || currentUser.MunicipalityId is not { } tenantId || tenantId == Guid.Empty
            || municipality.MunicipalityId != Guid.Empty && municipality.MunicipalityId != tenantId)
            return Result<OperationSetupOverviewDto>.Forbidden();

        var today = clock.PhilippineToday;
        var settings = await TenantOperationSettingsStore.ReadAsync(db, tenantId, ct);

        var sources = await db.RevenueClassifications.AsNoTracking()
            .Where(c => c.MunicipalityId == tenantId)
            .Select(c => new { c.Id, c.SemanticCode, c.IsActive }).ToListAsync(ct);
        var bySource = sources.ToDictionary(c => c.SemanticCode, StringComparer.Ordinal);
        var withPolicy = (await db.RevenueClassificationPolicies.AsNoTracking()
                .Where(p => p.MunicipalityId == tenantId && p.EffectiveDate <= today)
                .Select(p => p.RevenueClassificationId).Distinct().ToListAsync(ct)).ToHashSet();

        var assignments = (await db.CollectorOperationAssignments.AsNoTracking()
                .Where(a => a.MunicipalityId == tenantId)
                .Select(a => new { a.OperationCode, a.CollectorId }).ToListAsync(ct))
            .GroupBy(a => a.OperationCode, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(a => a.CollectorId).Distinct().ToArray(), StringComparer.Ordinal);

        // One evaluation per assigned collector, shared by every operation (the Collector app's own rules).
        var evaluator = new GetCollectorOperationCapabilitiesQueryHandler(db, currentUser, municipality, clock);
        var capabilities = new Dictionary<Guid, CollectorOperationCapabilitiesDto?>();
        foreach (var collectorId in assignments.Values.SelectMany(x => x).Distinct())
            capabilities[collectorId] = await evaluator.BuildForCollectorAsync(tenantId, collectorId, ct);

        var items = new List<OperationSetupDto>();
        foreach (var item in settings.Items)
        {
            var entry = TenantOperationCatalog.Find(item.OperationCode);
            if (entry is null) continue;
            if (!item.IsListed)
            {
                items.Add(new(item.OperationCode, item.DisplayName, item.GroupKey, OperationSetupState.Available, false, false, false, 0, 0, [], item.Version));
                continue;
            }

            bySource.TryGetValue(entry.ClassificationCode, out var source);
            var sourceExists = source is not null;
            var retired = source is { IsActive: false };
            var policy = source is { IsActive: true } && withPolicy.Contains(source.Id);
            var gaps = new List<string>();
            if (!sourceExists) gaps.Add("No revenue source set up");
            else if (retired) gaps.Add("Its revenue source is retired");
            else if (!policy) gaps.Add("No effective policy (receipt type) yet");

            var collectors = assignments.GetValueOrDefault(item.OperationCode) ?? [];
            var ready = 0;
            var reasons = new List<string>();
            foreach (var collectorId in collectors)
            {
                var capability = capabilities.GetValueOrDefault(collectorId)?.Operations.FirstOrDefault(o => o.OperationCode == item.OperationCode);
                if (capability is null) continue;
                if (capability.IsCollectible && capability.Status == CollectorOperationCapabilityStatus.Ready) ready++;
                else reasons.AddRange(capability.ReasonCodes.Select(ReasonText));
            }

            var state = ready > 0 ? OperationSetupState.Ready
                : gaps.Count > 0 || collectors.Length > 0 ? OperationSetupState.SetupRequired
                : OperationSetupState.Added;
            if (state == OperationSetupState.SetupRequired) gaps.AddRange(reasons.Distinct().Where(r => !gaps.Contains(r)));
            if (state == OperationSetupState.Added) gaps.Add("No collector assigned yet");
            items.Add(new(item.OperationCode, item.DisplayName, item.GroupKey, state, sourceExists, retired, policy,
                collectors.Length, ready, state == OperationSetupState.Ready ? [] : gaps, item.Version));
        }

        return Result<OperationSetupOverviewDto>.Success(new(settings.UsesConfiguration, items));
    }

    private static string ReasonText(string code) => code switch
    {
        GetCollectorOperationCapabilitiesQueryHandler.NoMobileWriter => "Not yet collectible on Mobile",
        GetCollectorOperationCapabilitiesQueryHandler.CollectorInactive => "An assigned collector is inactive",
        GetCollectorOperationCapabilitiesQueryHandler.NoCanonicalSource => "Its source is not yet activated for collection",
        GetCollectorOperationCapabilitiesQueryHandler.PolicyNotEffective => "No effective policy for the required receipt type",
        GetCollectorOperationCapabilitiesQueryHandler.RateNotEffective => "No effective rate yet",
        GetCollectorOperationCapabilitiesQueryHandler.ServiceSetupRequired => "Service setup is not completed",
        GetCollectorOperationCapabilitiesQueryHandler.ServiceDisabled => "The service is disabled",
        GetCollectorOperationCapabilitiesQueryHandler.MobileChannelDisabled => "Mobile collection is not enabled",
        GetCollectorOperationCapabilitiesQueryHandler.NoAssignedDocument => "No receipt or ticket document is in the collector's custody",
        GetCollectorOperationCapabilitiesQueryHandler.NoAssignedCashTicket => "No Cash Ticket is in the collector's custody",
        _ => "Setup is not complete",
    };
}
