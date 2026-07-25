using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Common.Services
{
    /// <summary>
    /// Default <see cref="IEmailVerificationSender"/>: hashes a cryptographically-random one-time token,
    /// stores only the hash, and emails the raw token as a link branded with the account's own LGU.
    /// </summary>
    public class EmailVerificationSender(IAppDbContext context, IEmailSender emailSender) : IEmailVerificationSender
    {
        /// <summary>Verification links are long-lived — an admin may only read their mail the next day.</summary>
        private const int TokenLifetimeDays = 7;

        public async Task<bool> SendAsync(BaseUser user, bool save = true, CancellationToken ct = default)
        {
            if (user is null || string.IsNullOrWhiteSpace(user.Email))
                return false;

            var (rawToken, tokenHash) = GenerateToken();
            user.SetEmailVerificationToken(tokenHash, DateTime.UtcNow.AddDays(TokenLifetimeDays));

            if (save)
                await context.SaveChangesAsync(ct);

            // Per-LGU branding, falling back to the platform name for an unresolved tenant.
            var municipality = await context.Municipalities
                .IgnoreQueryFilters()
                .Where(m => m.Id == user.MunicipalityId)
                .Select(m => new { m.Name, m.Code, m.OfficeAcronym })
                .FirstOrDefaultAsync(ct);

            var officeName = string.IsNullOrWhiteSpace(municipality?.OfficeAcronym)
                ? "StallTrack"
                : municipality!.OfficeAcronym!;
            var lguName = string.IsNullOrWhiteSpace(municipality?.Name)
                ? "your municipality"
                : municipality!.Name;

            var link = EmailVerificationLinks.Build(rawToken, municipality?.Code);
            var body =
                $"Please confirm this email address for your {officeName} StallTrack account ({lguName}).\n\n" +
                $"Username: {user.Username}\n\n" +
                $"Confirm your email:\n{link}\n\n" +
                "Confirming proves the address reaches you, which is what lets you reset your own password " +
                "later if you ever forget it. Until then, only your office Head can restore your access.\n\n" +
                $"This link expires in {TokenLifetimeDays} days.\n\n" +
                "If you were not expecting this, you can ignore it — confirming only verifies the address " +
                "and changes nothing else about your account.\n\n" +
                $"— {officeName} StallTrack";

            // Best-effort: SendAsync never throws and no-ops when SMTP is unconfigured, so account
            // creation/update never fails because of email.
            return await emailSender.SendAsync(
                user.Email!, user.FullName, $"{officeName} StallTrack — confirm your email", body, ct);
        }

        // A url-safe, cryptographically-random one-time token; only its SHA-256 hash is stored.
        private static (string raw, string hash) GenerateToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            var raw = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            return (raw, hash);
        }
    }
}
