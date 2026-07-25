using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Domain.Entities.Users;

namespace EEMOCantilanSDS.Application.Common.Interface.Services
{
    /// <summary>
    /// Issues and emails a one-time email-verification link for a user account.
    /// <para>
    /// A verified address is what makes self-service password recovery possible, so this runs whenever an
    /// account is created with an address and whenever that address changes — and can be triggered again by
    /// the office Head. Best-effort: a send failure never fails the surrounding operation.
    /// </para>
    /// </summary>
    public interface IEmailVerificationSender
    {
        /// <summary>
        /// Stamps a fresh verification token on the user and emails the link. The caller is responsible for
        /// persisting (this only mutates the tracked entity) — pass <paramref name="save"/> to persist here.
        /// Returns false when there is nothing to do (no address) or the email could not be sent.
        /// </summary>
        Task<bool> SendAsync(BaseUser user, bool save = true, CancellationToken ct = default);
    }
}
