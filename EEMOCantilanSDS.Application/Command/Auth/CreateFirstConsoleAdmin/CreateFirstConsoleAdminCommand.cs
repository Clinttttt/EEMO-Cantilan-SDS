using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.CreateFirstConsoleAdmin
{
    /// <summary>
    /// First-run bootstrap of the platform/console operator (manages onboarding across all LGUs). Allowed
    /// ONLY when no platform operator exists yet — self-disables afterwards, so it can never be a public
    /// back door to system-owner access. Distinct from a municipality's Head.
    /// </summary>
    public record CreateFirstConsoleAdminCommand(
        string FullName,
        string Username,
        string Email,
        string Password) : IRequest<Result<bool>>;
}
