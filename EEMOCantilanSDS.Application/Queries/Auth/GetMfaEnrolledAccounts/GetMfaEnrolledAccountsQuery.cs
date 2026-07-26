using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Authorization;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Queries.Auth.GetMfaEnrolledAccounts
{
    /// <summary>
    /// Lists every account with two-factor enabled, across ALL municipalities, for the platform operator's
    /// recovery tool. Cross-tenant by necessity — the operator's job is to rescue a Head of any LGU.
    /// </summary>
    public record GetMfaEnrolledAccountsQuery : IRequest<Result<IReadOnlyList<MfaEnrolledAccountDto>>>;

    /// <summary>
    /// Platform-operator only (same guard as onboarding/activation). Exposes identity and state only — never
    /// a secret, a recovery code, or a challenge.
    /// </summary>
    public class GetMfaEnrolledAccountsQueryHandler(IAppDbContext context, ICurrentUserService currentUser)
        : IRequestHandler<GetMfaEnrolledAccountsQuery, Result<IReadOnlyList<MfaEnrolledAccountDto>>>
    {
        public async Task<Result<IReadOnlyList<MfaEnrolledAccountDto>>> Handle(
            GetMfaEnrolledAccountsQuery request, CancellationToken ct)
        {
            if (!await PlatformOperatorGuard.IsCurrentAsync(context, currentUser, ct))
                return Result<IReadOnlyList<MfaEnrolledAccountDto>>.Forbidden();

            // Join to the municipality registry so the operator can tell two LGUs' accounts apart.
            var accounts = await context.AdminUsers
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(u => u.MfaEnabled && !u.IsDeleted)
                .Select(u => new
                {
                    u.Id,
                    u.Username,
                    u.FullName,
                    u.Email,
                    u.Role,
                    u.MfaEnrolledAt,
                    u.MfaRecoveryCodeHashes,
                    Municipality = context.Municipalities
                        .IgnoreQueryFilters()
                        .Where(m => m.Id == u.MunicipalityId)
                        .Select(m => new { m.Name, m.OfficeAcronym })
                        .FirstOrDefault()
                })
                .ToListAsync(ct);

            var result = accounts
                .Select(a => new MfaEnrolledAccountDto(
                    a.Id,
                    a.Username ?? string.Empty,
                    a.FullName,
                    a.Email,
                    a.Municipality?.Name,
                    a.Municipality?.OfficeAcronym,
                    IsHead: a.Role == AdminRole.SuperAdmin,
                    a.MfaEnrolledAt,
                    // Counted here rather than in SQL: the codes are stored as one delimited column.
                    RecoveryCodesRemaining: string.IsNullOrEmpty(a.MfaRecoveryCodeHashes)
                        ? 0
                        : a.MfaRecoveryCodeHashes.Split(';', StringSplitOptions.RemoveEmptyEntries).Length))
                .OrderByDescending(a => a.IsHead)
                .ThenBy(a => a.Municipality)
                .ThenBy(a => a.Username)
                .ToList();

            return Result<IReadOnlyList<MfaEnrolledAccountDto>>.Success(result);
        }
    }
}
