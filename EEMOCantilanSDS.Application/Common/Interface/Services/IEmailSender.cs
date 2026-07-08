using System.Threading;
using System.Threading.Tasks;

namespace EEMOCantilanSDS.Application.Common.Interface.Services
{
    /// <summary>Sends transactional emails (onboarding links, notices). No-ops safely when unconfigured.</summary>
    public interface IEmailSender
    {
        /// <summary>Sends a plain-text email. Returns true when actually sent; false when unconfigured or on failure (best-effort).</summary>
        Task<bool> SendAsync(string toEmail, string? toName, string subject, string body, CancellationToken ct = default);
    }
}
