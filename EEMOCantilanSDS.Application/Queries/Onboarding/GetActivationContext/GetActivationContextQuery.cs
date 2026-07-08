using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Onboarding.GetActivationContext
{
    /// <summary>
    /// Resolves the display context for a Head's one-time activation link so the set-password page can show
    /// who the account belongs to (full name, username, office) — mirroring the Cantilan first-run Head setup.
    /// Anonymous: the token is the credential. Returns a generic failure for any invalid/expired token so it
    /// never reveals whether a token exists.
    /// </summary>
    public record GetActivationContextQuery(string Token) : IRequest<Result<ActivationContextDto>>;

    /// <summary>Identity shown on the activation page for a valid, unused token.</summary>
    public record ActivationContextDto(string FullName, string Username, string Municipality, string? OfficeName, string? OfficeAcronym);
}
