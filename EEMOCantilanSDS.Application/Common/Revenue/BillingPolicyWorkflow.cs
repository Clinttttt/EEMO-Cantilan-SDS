using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Revenue;

/// <summary>The office's billing rules (IA-071, IA-072). Head and Admin read them; only the Head records a new version.</summary>
// shortcut: one policy per municipality, not per facility, because no office has asked to bill two facilities differently. Key versions by facility when one does.
public sealed class BillingPolicyWorkflow(IAppDbContext db, ICurrentUserService user, ICurrentMunicipalityAccessor municipality, IClock clock)
{
    private Guid Tenant => municipality.MunicipalityId;

    private Result<T>? Guard<T>(bool head = false)
    {
        if (!user.IsAuthenticated || user.UserId is null || user.UserId == Guid.Empty) return Result<T>.Unauthorized();
        if (Tenant == Guid.Empty || user.MunicipalityId != Tenant || user.Role is not ("Admin" or "SuperAdmin") || head && user.Role != "SuperAdmin")
            return Result<T>.Forbidden();
        return null;
    }

    /// <summary>The rules in force today for the pinned office; the StallTrack defaults when it has set none.</summary>
    public async Task<BillingPolicy> CurrentAsync(CancellationToken ct = default) =>
        BillingPolicyVersion.InForce(await VersionsAsync(ct), clock.PhilippineToday)?.Rules ?? BillingPolicy.StallTrackDefaults;

    private async Task<List<BillingPolicyVersion>> VersionsAsync(CancellationToken ct) =>
        await db.BillingPolicyVersions.AsNoTracking().Where(x => x.MunicipalityId == Tenant).ToListAsync(ct);

    public async Task<Result<BillingPolicyPageDto>> ReadAsync(CancellationToken ct = default)
    {
        if (Guard<BillingPolicyPageDto>() is { } denied) return denied;
        return Result<BillingPolicyPageDto>.Success(Page(await VersionsAsync(ct)));
    }

    public async Task<Result<BillingPolicyPageDto>> RecordAsync(RecordBillingPolicyRequest request, CancellationToken ct = default)
    {
        if (Guard<BillingPolicyPageDto>(head: true) is { } denied) return denied;
        try
        {
            db.BillingPolicyVersions.Add(BillingPolicyVersion.Create(Tenant, request.EffectiveFrom, clock.PhilippineToday,
                request.Rules, request.GoverningReference, user.UserId!.Value, clock.UtcNow));
            await db.SaveChangesAsync(ct);
        }
        catch (ArgumentException ex)
        {
            return Result<BillingPolicyPageDto>.Failure(ex.Message, ResultStatus.Invalid);
        }
        return Result<BillingPolicyPageDto>.Success(Page(await VersionsAsync(ct)));
    }

    private BillingPolicyPageDto Page(List<BillingPolicyVersion> versions)
    {
        var today = clock.PhilippineToday;
        var inForce = BillingPolicyVersion.InForce(versions, today);
        return new(inForce?.Rules ?? BillingPolicy.StallTrackDefaults, inForce is null ? null : Dto(inForce),
            versions.OrderByDescending(v => v.RecordedAtUtc).Select(Dto).ToList(), today);
    }

    private static BillingPolicyVersionDto Dto(BillingPolicyVersion v) => new(v.Id, v.EffectiveFrom, v.Rules, v.GoverningReference, v.RecordedAtUtc);
}
