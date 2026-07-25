using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.VerifyEmail
{
    /// <summary>
    /// Consumes a one-time email-verification token and marks the address verified.
    /// <para>
    /// The token is matched by HASH (the raw value is never stored) and validated in fixed time. Confirming
    /// grants nothing beyond the verified flag — it cannot activate a disabled account or change a password —
    /// so this link is safe even if it sits in a mailbox for days.
    /// </para>
    /// </summary>
    public class VerifyEmailCommandHandler(IAppDbContext context)
        : IRequestHandler<VerifyEmailCommand, Result<VerifiedAccountDto>>
    {
        private const string GenericError = "This confirmation link is invalid or has expired.";

        public async Task<Result<VerifiedAccountDto>> Handle(VerifyEmailCommand request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return Result<VerifiedAccountDto>.Failure(GenericError);

            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));

            // Anonymous flow: the token is the secret and globally unique, so it determines the tenant.
            var user = await context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.EmailVerificationTokenHash == hash && !u.IsDeleted, ct);

            if (user is null || !user.IsEmailVerificationTokenValid(hash))
                return Result<VerifiedAccountDto>.Failure(GenericError);

            var alreadyVerified = user.EmailVerified;
            user.ConfirmEmail();
            await context.SaveChangesAsync(ct);

            var municipality = await context.Municipalities
                .IgnoreQueryFilters()
                .Where(m => m.Id == user.MunicipalityId)
                .Select(m => new { m.Name, m.OfficeAcronym })
                .FirstOrDefaultAsync(ct);

            return Result<VerifiedAccountDto>.Success(new VerifiedAccountDto(
                user.Username ?? string.Empty,
                municipality?.Name,
                municipality?.OfficeAcronym,
                alreadyVerified));
        }
    }
}
