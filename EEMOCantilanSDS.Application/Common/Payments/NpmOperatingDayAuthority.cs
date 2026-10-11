using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;

namespace EEMOCantilanSDS.Application.Common.Payments;

/// <summary>
/// Who may change an NPM operating day. A change dated today or later is the everyday act and stays with the existing
/// roles. A change to a day that has already passed re-reads what was owed on it, so it needs the Head and a stated reason
/// that is kept with the change in the audit trail. Money already collected is never touched by either.
/// </summary>
public static class NpmOperatingDayAuthority
{
    public const int MinimumReasonLength = 5;

    /// <summary>Null when the change is allowed; otherwise the refusal.</summary>
    public static Result<bool>? Check(ICurrentUserService user, DateOnly date, DateOnly today, string? reason)
    {
        if (!user.IsAuthenticated || user.Role is not ("SuperAdmin" or "Admin"))
            return Result<bool>.Forbidden();
        if (date >= today) return null;

        if (user.Role != "SuperAdmin")
            return Result<bool>.Failure("Only the Head can change a day that has already passed.", ResultStatus.Forbidden);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < MinimumReasonLength)
            return Result<bool>.Failure("State the reason for changing a past day; it is kept in the audit trail.", ResultStatus.Invalid);
        return null;
    }
}
