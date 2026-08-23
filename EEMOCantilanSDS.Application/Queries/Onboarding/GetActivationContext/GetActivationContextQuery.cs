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
    /// <param name="Code">
    /// The municipality's own code, so the "Continue to sign in" button can hand the login page the LGU it belongs to.
    /// Without it that button led to a bare /login, which falls back to the default LGU's seal and office - an office
    /// finished activating Madrid and was greeted by Cantilan.
    /// </param>
    /// <param name="SealPath">
    /// The office's OWN seal, so the page an office sets its first password on carries its own identification. It used
    /// to carry StallTrack's mark alone, on the grounds that a one-time token does not name an LGU - but it does, by
    /// way of the account it belongs to. Null when the office has no seal on file: the slot then waits, and is never
    /// filled with another municipality's mark. An embedded seal is rewritten to the seal endpoint's address by the
    /// controller, which is the only place that knows the host the caller reached.
    /// </param>
    public record ActivationContextDto(
        string FullName,
        string Username,
        string Municipality,
        string? OfficeName,
        string? OfficeAcronym,
        string? Code = null,
        string? SealPath = null);
}
