using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>
/// WCF Mobile collection, enabled once per tenant (2026-10-01). Readiness is derived by the server from what StallTrack already
/// knows — the WCF policy, the collectors assigned WCF, and any WCF collection awaiting office
/// review — so the Head is never asked to attest or type a fact the system holds. "One tap" is automatic validation, not a
/// bypass: Enable re-evaluates every check inside its own transaction and activates nothing while a blocker remains.
/// Activation is prospective and idempotent; historical Water records are not changed (they keep their explicit migration).
/// </summary>
public sealed class WcfMobileCollectionWorkflow(
    IAppDbContext db,
    ICurrentUserService currentUser,
    ICurrentMunicipalityAccessor municipality,
    IClock? clock = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DateOnly BusinessToday => clock?.PhilippineToday ?? PhilippineTime.Today;
    private DateTime UtcNow => clock?.UtcNow ?? DateTime.UtcNow;

    public async Task<Result<WcfMobileStatusDto>> GetStatusAsync(CancellationToken ct = default)
    {
        if (OfficeActor() is not { } office) return Result<WcfMobileStatusDto>.Forbidden();
        return Result<WcfMobileStatusDto>.Success(await EvaluateAsync(office.TenantId, ct));
    }

    public async Task<Result<WcfMobileStatusDto>> EnableAsync(CancellationToken ct = default)
    {
        if (OfficeActor() is not { } office) return Result<WcfMobileStatusDto>.Forbidden();
        await using var transaction = await db.BeginSerializableTransactionAsync(ct);
        // Re-evaluated at commit time: what the page showed is never the safety mechanism.
        var status = await EvaluateAsync(office.TenantId, ct);
        if (status.Active) return Result<WcfMobileStatusDto>.Success(status);   // idempotent: no second boundary
        if (status.Blockers.Count > 0)
            return Result<WcfMobileStatusDto>.Failure(
                "WCF Mobile collection cannot be enabled yet: " + string.Join(" ", status.Blockers.Select(b => b.Detail)),
                ResultStatus.Conflict);

        var now = UtcNow;
        var activation = CollectorOperationActivation.Activate(office.TenantId, CollectorOperationCodes.Wcf,
            BusinessToday, office.UserId, office.Username, now);
        db.CollectorOperationActivations.Add(activation);
        db.AuditLogs.Add(AuditLog.Create(office.UserId.ToString("N"), office.Username, office.Role,
            "WcfMobileCollectionEnabled", nameof(CollectorOperationActivation), activation.Id,
            oldValues: JsonSerializer.Serialize(new { Active = false }, JsonOptions),
            newValues: JsonSerializer.Serialize(new
            {
                Active = true,
                OperationCode = CollectorOperationCodes.Wcf,
                EffectiveFrom = activation.EffectiveFrom.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                Instrument = "CashTicket",
                AmountBasis = "DirectAmount",
                Channel = "CollectorMobile"
            }, JsonOptions),
            notes: "Prospective boundary: new WCF activity is canonical from this date; historical Water records are unchanged.",
            municipalityId: office.TenantId));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent Enable won the unique (tenant, operation) row: return the single activation it created.
            db.ChangeTracker.Clear();
            return Result<WcfMobileStatusDto>.Success(await EvaluateAsync(office.TenantId, ct));
        }
        return Result<WcfMobileStatusDto>.Success(await EvaluateAsync(office.TenantId, ct));
    }

    private sealed record Office(Guid TenantId, Guid UserId, string Username, string Role);

    private Office? OfficeActor()
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId || userId == Guid.Empty
            || currentUser.Role is not ("Admin" or "SuperAdmin"))
            return null;
        var tenantId = municipality.MunicipalityId;
        if (tenantId == Guid.Empty || currentUser.MunicipalityId is { } claimed && claimed != tenantId) return null;
        return new Office(tenantId, userId, currentUser.Username ?? "Office", currentUser.Role);
    }

    private async Task<WcfMobileStatusDto> EvaluateAsync(Guid tenantId, CancellationToken ct)
    {
        var activation = await db.CollectorOperationActivations.AsNoTracking().SingleOrDefaultAsync(x =>
            x.MunicipalityId == tenantId && x.OperationCode == CollectorOperationCodes.Wcf, ct);
        var checks = new List<WcfReadinessItemDto>();
        var blockers = new List<WcfReadinessItemDto>();
        void Check(string code, string title, bool ok, string okDetail, string failDetail, string? action = null, string? href = null)
        {
            var item = new WcfReadinessItemDto(code, title, ok ? okDetail : failDetail, ok, ok ? null : action, ok ? null : href);
            checks.Add(item);
            if (!ok) blockers.Add(item);
        }

        // Policy: WCF is a Cash Ticket classification (system rule; nothing for the Head to configure).
        var classificationId = await db.RevenueClassifications.AsNoTracking()
            .Where(x => x.MunicipalityId == tenantId && x.SemanticCode == RevenueClassificationCodes.Wcf && x.IsActive)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        var instrument = classificationId is { } id
            ? await db.RevenueClassificationPolicies.AsNoTracking()
                .Where(x => x.MunicipalityId == tenantId && x.RevenueClassificationId == id
                    && x.BusinessContext == RevenuePolicyContext.Default && x.EffectiveDate <= BusinessToday)
                .OrderByDescending(x => x.EffectiveDate).Select(x => x.PermittedInstrumentType).FirstOrDefaultAsync(ct)
            : null;
        Check("POLICY", "Water policy", instrument == RevenueInstrumentType.CashTicket,
            "Cash Ticket · direct amount", "The Water Consumption Fee policy in effect is not Cash Ticket.",
            "Open Revenue Setup", "/settings/revenue");

        // Collectors assigned WCF. Cash Ticket stock is never a readiness gate (IA-062).
        var assigned = await (
            from assignment in db.CollectorOperationAssignments.AsNoTracking()
            join collector in db.CollectorUsers.AsNoTracking() on assignment.CollectorId equals collector.Id
            where assignment.MunicipalityId == tenantId && assignment.OperationCode == CollectorOperationCodes.Wcf
            select new { collector.Id, collector.FullName, collector.IsActive }).ToListAsync(ct);
        var activeIds = assigned.Where(x => x.IsActive).Select(x => x.Id).ToArray();

        Check("COLLECTOR", "Collector assignment", activeIds.Length > 0,
            activeIds.Length == 1 ? "1 active collector assigned" : $"{activeIds.Length} active collectors assigned",
            assigned.Count == 0 ? "No collector is assigned Water Consumption Fee." : "Every collector assigned Water Consumption Fee is inactive.",
            "Assign collector", "/collectors");

        // WCF collections already waiting for office review must be resolved first.
        var pending = await db.PostingOperations.AsNoTracking().CountAsync(x =>
            x.MunicipalityId == tenantId && (x.Origin == "MobileWcf" || x.Origin == "WebWcf")
            && x.Status == PostingOperationStatus.ReconciliationRequired, ct);
        Check("POSTING", "Posting queue", pending == 0, "No WCF collection awaits review",
            $"{pending} WCF collection{(pending == 1 ? "" : "s")} {(pending == 1 ? "needs" : "need")} office review.",
            "Review WCF exceptions", "/operations/water-consumption-fees");

        // Historical Water records with legacy settlement stay on the legacy path — information, never a blocker.
        var legacy = await db.UtilityBills.AsNoTracking().CountAsync(x => x.MunicipalityId == tenantId
            && x.WaterSettlementAuthorityState != SettlementAuthority.Canonical
            && (x.WaterStatus != PaymentStatus.Unpaid || x.WaterSettlementAuthorityState == SettlementAuthority.PendingCutover), ct);

        var collectors = assigned.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase).Select(x => new WcfCollectorReadinessDto(
            x.Id, x.FullName, x.IsActive, 0, 0, x.IsActive)).ToList();

        return new WcfMobileStatusDto(activation is not null, activation?.EffectiveFrom, activation?.ActivatedAtUtc,
            activation?.ActivatedBy, activation is null && blockers.Count == 0,
            activation is null ? blockers : [], checks, collectors, legacy);
    }
}
