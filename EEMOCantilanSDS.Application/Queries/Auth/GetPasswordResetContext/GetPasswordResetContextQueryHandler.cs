using EEMOCantilanSDS.Application.Common.Interface.Time;
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

namespace EEMOCantilanSDS.Application.Queries.Auth.GetPasswordResetContext
{
    /// <summary>
    /// Read-only token→account lookup for the reset page. Mirrors the activation page's context query: the
    /// details are released ONLY to a holder of a valid, unexpired token, and nothing is mutated.
    /// </summary>
    public class GetPasswordResetContextQueryHandler(IAppDbContext context, IClock clock)
        : IRequestHandler<GetPasswordResetContextQuery, Result<TokenAccountContextDto>>
    {
        private const string GenericError = "This password reset link is invalid or has expired.";

        public async Task<Result<TokenAccountContextDto>> Handle(GetPasswordResetContextQuery request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return Result<TokenAccountContextDto>.Failure(GenericError);

            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));

            var user = await context.Users
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.PasswordResetTokenHash == hash && !u.IsDeleted, ct);

            // Re-checks the hash in fixed time and enforces the expiry.
            if (user is null || !user.IsPasswordResetTokenValid(hash, clock.UtcNow))
                return Result<TokenAccountContextDto>.Failure(GenericError);

            var municipality = await context.Municipalities
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(m => m.Id == user.MunicipalityId)
                .Select(m => new { m.Name, m.OfficeAcronym })
                .FirstOrDefaultAsync(ct);

            return Result<TokenAccountContextDto>.Success(new TokenAccountContextDto(
                user.Username ?? string.Empty,
                user.FullName,
                municipality?.Name,
                municipality?.OfficeAcronym));
        }
    }
}
