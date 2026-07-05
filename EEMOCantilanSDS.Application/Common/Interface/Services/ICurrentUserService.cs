using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;

namespace EEMOCantilanSDS.Application.Common.Interface.Services;

public interface ICurrentUserService
{
    bool IsAuthenticated { get; }
    AdminUserDto? GetCurrentUser();

    /// <summary>Authenticated user's id (from the token's NameIdentifier claim), for admins and collectors alike.</summary>
    Guid? UserId { get; }

    /// <summary>Authenticated user's username, used for audit fields.</summary>
    string? Username { get; }

    /// <summary>Authenticated user's role ("SuperAdmin", "Admin", or "Collector").</summary>
    string? Role { get; }

    /// <summary>
    /// The acting collector's id when the authenticated user is a Collector; otherwise null.
    /// Collection attribution uses this so admin-recorded entries are never mis-attributed to a collector.
    /// </summary>
    Guid? CollectorId { get; }

    /// <summary>
    /// The authenticated user's LGU/municipality code, read from the token's municipality claim.
    /// Null when there is no authenticated request (background jobs, tests) — callers fall back to
    /// the default tenant. Users are not yet municipality-scoped, so today this is the default LGU.
    /// </summary>
    string? MunicipalityCode { get; }

    /// <summary>
    /// The authenticated user's municipality id, read from the token's <c>municipality_id</c> claim.
    /// Null when there is no authenticated request (background jobs, tests, token-less flows) — callers
    /// fall back to the default municipality (Cantilan). This is the per-request tenant identity.
    /// </summary>
    Guid? MunicipalityId { get; }
}
