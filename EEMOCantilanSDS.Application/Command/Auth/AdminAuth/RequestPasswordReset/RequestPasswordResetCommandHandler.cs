using System;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.RequestPasswordReset
{
    /// <summary>
    /// Issues a one-time password-reset link by email.
    /// <para>
    /// Security posture (OWASP "Forgot Password" cheat sheet):
    /// <list type="bullet">
    /// <item>Always returns the same neutral success — no account enumeration.</item>
    /// <item>Only the token HASH is stored; the raw token exists only in the email.</item>
    /// <item>Short expiry, single use, and issuing a new token invalidates the previous one.</item>
    /// <item>Requires a VERIFIED email, so a mistyped address can never be used to take over an account.</item>
    /// <item>Per-account throttle prevents mailbox flooding through the anonymous endpoint.</item>
    /// </list>
    /// </para>
    /// Scope: web admin/Head accounts only — collectors sign in on mobile and are reset by their Head.
    /// </summary>
    public class RequestPasswordResetCommandHandler(
        IAppDbContext context,
        IMunicipalityRepository municipalityRepository,
        IEmailSender emailSender)
        : IRequestHandler<RequestPasswordResetCommand, Result<bool>>
    {
        /// <summary>How long an issued reset link stays valid.</summary>
        private const int TokenLifetimeMinutes = 30;

        /// <summary>Minimum gap between reset emails for the same account (anti mail-bombing).</summary>
        private const int RequestThrottleMinutes = 2;

        /// <summary>
        /// Safety cap on how many accounts one request may serve. The same address can legitimately be
        /// registered in a few LGUs; this bounds the work (and the emails) if it is ever registered in many.
        /// </summary>
        private const int MaxAccountsPerRequest = 5;

        public async Task<Result<bool>> Handle(RequestPasswordResetCommand request, CancellationToken ct)
        {
            // Every exit path below returns this identical result. Do not add a distinguishing failure.
            var neutral = Result<bool>.Success(true);

            var identifier = request.Email?.Trim();
            if (string.IsNullOrWhiteSpace(identifier))
                return neutral;

            // Per-LGU scoping mirrors LoginCommandHandler: when the caller came through a scoped LGU URL
            // (?lgu={code}) the lookup is restricted to that municipality, so the same address reused across
            // LGUs resolves to the right tenant. With no code the lookup stays global — unchanged default
            // (Cantilan) behaviour. An unknown code returns the same neutral response (no probing).
            Guid? scopeMunicipalityId = null;
            if (!string.IsNullOrWhiteSpace(request.MunicipalityCode))
            {
                var municipality = await municipalityRepository.GetByIdentifierAsync(request.MunicipalityCode, ct);
                if (municipality is null) return neutral;
                scopeMunicipalityId = municipality.Id;
            }

            var lowered = identifier.ToLowerInvariant();

            // Anonymous flow: no tenant is attached to the request, so query filters are bypassed and the
            // tenant boundary is applied explicitly above/below instead.
            var query = context.AdminUsers.IgnoreQueryFilters().Where(u => !u.IsDeleted);
            if (scopeMunicipalityId is { } mid)
                query = query.Where(u => u.MunicipalityId == mid);

            // Matched on the registered EMAIL only — the reset link can only be delivered there.
            //
            // IMPORTANT (multi-tenant): email uniqueness is per-LGU (UNIQUE MunicipalityId+Email), so the
            // SAME address can legitimately be registered in several municipalities. When the request is not
            // scoped to one LGU we must therefore NOT pick an arbitrary match — doing so silently reset a
            // different municipality's account than the one the user meant. Every eligible match gets its
            // own single-use link instead, and each email names its LGU and username so the owner can tell
            // them apart. A scoped request (?lgu=) still resolves exactly one account.
            var candidates = await query
                .Where(u => u.Email != null && u.Email.ToLower() == lowered)
                .OrderBy(u => u.CreatedAt)
                .Take(MaxAccountsPerRequest)
                .ToListAsync(ct);

            var now = DateTime.UtcNow;

            // Silent no-ops: unknown address, disabled account, missing/unverified email. An unverified
            // address is not proof of ownership, so it is never eligible; those users are reset by their Head.
            // A per-account throttle keeps a known address from being flooded.
            var eligible = candidates
                .Where(u => u.IsActive
                            && u.EmailVerified
                            && !string.IsNullOrWhiteSpace(u.Email)
                            && (u.PasswordResetRequestedAt is not { } last
                                || last.AddMinutes(RequestThrottleMinutes) <= now))
                .ToList();

            if (eligible.Count == 0)
                return neutral;

            // Issue every token first, then persist once, so a partial failure cannot leave some accounts
            // with a token the owner never received a link for.
            var issued = new List<(BaseUser User, string RawToken)>(eligible.Count);
            foreach (var account in eligible)
            {
                var (rawToken, tokenHash) = GenerateResetToken();
                account.SetPasswordResetToken(tokenHash, now.AddMinutes(TokenLifetimeMinutes), now);
                issued.Add((account, rawToken));
            }
            await context.SaveChangesAsync(ct);

            foreach (var (account, rawToken) in issued)
                await SendResetEmailAsync(account, rawToken, ct);

            return neutral;
        }

        /// <summary>
        /// Sends one account's reset link, branded with that account's own municipality/office so a user
        /// whose address serves several LGUs can tell the links apart.
        /// </summary>
        private async Task SendResetEmailAsync(BaseUser user, string rawToken, CancellationToken ct)
        {
            // The platform's own operator belongs to no municipality, and its reset happens on the platform's own
            // console. Sending it an LGU's address would land it on a sign-in screen its account is refused by, and
            // naming a municipality at it would state something untrue. Answered from the account's own flag.
            if (user is AdminUser { IsPlatformOperator: true })
            {
                var operatorLink = PasswordResetLinks.BuildForOperator(rawToken);
                var operatorBody =
                    "A password reset was requested for your StallTrack platform operator account.\n\n" +
                    $"Username: {user.Username}\n\n" +
                    $"Set a new password using the secure link below:\n{operatorLink}\n\n" +
                    $"This link can be used once and expires in {TokenLifetimeMinutes} minutes.\n\n" +
                    "If you did not request this, you can safely ignore this email — your password stays unchanged, " +
                    "and no one can access your account through this link without your mailbox.\n\n" +
                    "— StallTrack";

                await emailSender.SendAsync(
                    user.Email!, user.FullName, "StallTrack — password reset", operatorBody, ct);
                return;
            }

            var municipalityRow = await context.Municipalities
                .IgnoreQueryFilters()
                .Where(m => m.Id == user.MunicipalityId)
                .Select(m => new { m.Name, m.Code, m.OfficeAcronym })
                .FirstOrDefaultAsync(ct);

            var officeName = string.IsNullOrWhiteSpace(municipalityRow?.OfficeAcronym)
                ? "StallTrack"
                : municipalityRow!.OfficeAcronym!;
            var lguName = string.IsNullOrWhiteSpace(municipalityRow?.Name)
                ? "your municipality"
                : municipalityRow!.Name;

            var link = PasswordResetLinks.Build(rawToken, municipalityRow?.Code);
            var body =
                $"A password reset was requested for your {officeName} StallTrack account ({lguName}).\n\n" +
                $"Username: {user.Username}\n\n" +
                $"Set a new password using the secure link below:\n{link}\n\n" +
                $"This link can be used once and expires in {TokenLifetimeMinutes} minutes.\n\n" +
                "If you did not request this, you can safely ignore this email — your password stays " +
                "unchanged, and no one can access your account through this link without your mailbox.\n\n" +
                $"— {officeName} StallTrack";

            // Best-effort: SendAsync never throws and no-ops when SMTP is unconfigured, so an email outage
            // cannot turn into a failed request that would reveal whether the account exists.
            await emailSender.SendAsync(user.Email!, user.FullName, $"{officeName} StallTrack — password reset", body, ct);
        }

        // A url-safe, cryptographically-random one-time token; only its SHA-256 hash is stored.
        // Mirrors the activation-token generator.
        private static (string raw, string hash) GenerateResetToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            var raw = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            return (raw, hash);
        }
    }
}
