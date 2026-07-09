using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Onboarding.GetActivationContext
{
    public class GetActivationContextQueryHandler(IAppDbContext context)
        : IRequestHandler<GetActivationContextQuery, Result<ActivationContextDto>>
    {
        private const string GenericError = "This activation link is invalid or has expired.";

        public async Task<Result<ActivationContextDto>> Handle(GetActivationContextQuery request, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Token))
                return Result<ActivationContextDto>.Failure(GenericError);

            var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(request.Token)));

            // Anonymous flow: look the token up across all municipalities (the request carries no tenant).
            var user = await context.Users
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.ActivationTokenHash == hash, ct);

            if (user is null || !user.IsActivationTokenValid(hash))
                return Result<ActivationContextDto>.Failure(GenericError);

            var muni = await context.Municipalities
                .IgnoreQueryFilters()
                .Where(m => m.Id == user.MunicipalityId)
                .Select(m => new { m.Name, m.OfficeName, m.OfficeAcronym })
                .FirstOrDefaultAsync(ct);

            return Result<ActivationContextDto>.Success(new ActivationContextDto(
                user.FullName ?? string.Empty,
                user.Username ?? string.Empty,
                muni?.Name ?? string.Empty,
                muni?.OfficeName,
                muni?.OfficeAcronym));
        }
    }
}
