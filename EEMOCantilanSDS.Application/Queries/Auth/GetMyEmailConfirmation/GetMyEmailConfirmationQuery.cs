using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Auth.GetMyEmailConfirmation
{
    /// <summary>
    /// Whether the signed-in account's email address has been confirmed, and which address it is.
    ///
    /// <para>
    /// The screen needs this to say something true: a confirmed address is what allows a self-service password reset, and
    /// an account whose address was never confirmed has no way back on its own. Offering the action unconditionally would
    /// mean offering it to accounts that have nothing to prove.
    /// </para>
    /// </summary>
    public record GetMyEmailConfirmationQuery : IRequest<Result<MyEmailConfirmationDto>>;

    /// <param name="Email">The address on the account, or null when it holds none.</param>
    /// <param name="Verified">True once the address has been proved to reach its owner.</param>
    public record MyEmailConfirmationDto(string? Email, bool Verified);
}
