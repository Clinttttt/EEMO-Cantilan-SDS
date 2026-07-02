using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;

namespace EEMOCantilanSDS.Infrastructure.Tenancy;

/// <summary>
/// Resolves the active LGU from the authenticated user's municipality claim (via
/// <see cref="ICurrentUserService"/>), falling back to the default tenant when there is no
/// authenticated request — background jobs, seeding, tests, and (today) every user, since users are
/// not yet municipality-scoped.
///
/// TEMPORARY fallback: once a second LGU is onboarded (Phase 3+), a missing/invalid claim on an
/// authenticated request should become a warning/error rather than silently defaulting to Cantilan,
/// so a malformed token can never land in Cantilan's context. Until then, behaviour is identical to
/// the previous <see cref="StaticTenantContext"/> (always the default tenant), so caches and reports
/// are unchanged.
/// </summary>
public sealed class ClaimTenantContext(ICurrentUserService currentUser) : ITenantContext
{
    public string TenantCode
    {
        get
        {
            var code = currentUser.MunicipalityCode;
            return string.IsNullOrWhiteSpace(code) ? TenantConstants.DefaultTenantCode : code.Trim();
        }
    }
}
