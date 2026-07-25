using System;
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

        public async Task<Result<bool>> Handle(RequestPasswordResetCommand request, CancellationToken ct)
        {
            // Every exit path below returns this identical result. Do not add a distinguishing failure.
            var neutral = Result<bool>.Success(true);

            var identifier = request.UsernameOrEmail?.Trim();
            if (string.IsNullOrWhiteSpace(identifier))
                return neutral;

            // Per-LGU scoping mirrors LoginCommandHandler: when the caller came through a scoped LGU URL
            // (?lgu={code}) the lookup is restricted to that municipality, so a username shared across LGUs
            // resolves to the right tenant. With no code the lookup stays global — unchanged default
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

            var user = await query.FirstOrDefaultAsync(
                u => (u.Username != null && u.Username.ToLower() == lowered)
                     || (u.Email != null && u.Email.ToLower() == lowered), ct);

            // Silent no-ops: unknown account, disabled account, missing/unverified email. An unverified
            // address is not proof of ownership, so it is never eligible; those users are reset by their Head.
            if (user is null || !user.IsActive || !user.EmailVerified || string.IsNullOrWhiteSpace(user.Email))
                return neutral;

            var now = DateTime.UtcNow;
            if (user.PasswordResetRequestedAt is { } last
                && last.AddMinutes(RequestThrottleMinutes) > now)
                return neutral;   // already emailed a link moments ago

            var (rawToken, tokenHash) = GenerateResetToken();
            user.SetPasswordResetToken(tokenHash, now.AddMinutes(TokenLifetimeMinutes), now);
            await context.SaveChangesAsync(ct);

            // Per-LGU branding: address the user by their own municipality/office, falling back to the
            // platform name so an unresolved tenant still sends a sensible email.
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

            return neutral;
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
