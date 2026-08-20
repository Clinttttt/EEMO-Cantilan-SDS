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
    /// Lists accounts with two-factor enabled for the recovery tool. A Head sees the accounts of their OWN
    /// municipality — the staff they administer. A DEDICATED platform operator (the console) sees every
    /// municipality, because rescuing an office whose only Head is locked out is its job. One LGU's portal
    /// therefore never displays another's usernames or work e-mail addresses.
    /// </summary>
    public record GetMfaEnrolledAccountsQuery : IRequest<Result<IReadOnlyList<MfaEnrolledAccountDto>>>;

    /// <summary>
    /// Heads and the dedicated platform operator. Exposes identity and state only — never a secret, a
    /// recovery code, or a challenge.
    /// </summary>
    public class GetMfaEnrolledAccountsQueryHandler(IAppDbContext context, ICurrentUserService currentUser)
        : IRequestHandler<GetMfaEnrolledAccountsQuery, Result<IReadOnlyList<MfaEnrolledAccountDto>>>
    {
        public async Task<Result<IReadOnlyList<MfaEnrolledAccountDto>>> Handle(
            GetMfaEnrolledAccountsQuery request, CancellationToken ct)
        {
            // Cross-tenant listing is a PLATFORM-OPERATOR power, not a Head's. A dedicated operator account
            // (the IsPlatformOperator flag, used by the console) keeps the full view because rescuing any
            // LGU's Head is its job. Every other Head — a municipal officer first — is scoped to their OWN
            // municipality: another LGU's Head usernames and work emails have no business appearing inside
            // one municipality's portal.
            var seesEveryMunicipality = await PlatformOperatorGuard.IsDedicatedOperatorAsync(context, currentUser, ct);
            var isHead = string.Equals(currentUser.Role, PlatformOperatorPolicy.SuperAdminRole, StringComparison.OrdinalIgnoreCase);
            if (!seesEveryMunicipality && !isHead)
                return Result<IReadOnlyList<MfaEnrolledAccountDto>>.Forbidden();

            var enrolled = context.AdminUsers
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(u => u.MfaEnabled && !u.IsDeleted);

            if (!seesEveryMunicipality)
            {
                if (currentUser.MunicipalityId is not Guid ownMunicipalityId)
                    return Result<IReadOnlyList<MfaEnrolledAccountDto>>.Forbidden();

                enrolled = enrolled.Where(u => u.MunicipalityId == ownMunicipalityId);
            }

            // Join to the municipality registry so the operator can tell two LGUs' accounts apart.
            var accounts = await enrolled
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
