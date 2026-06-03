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
}
