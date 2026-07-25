using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Auth;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Auth.GetMfaStatus
{
    /// <summary>Two-factor state of the signed-in user, for their own security panel.</summary>
    public record GetMfaStatusQuery : IRequest<Result<MfaStatusDto>>;

    /// <summary>
    /// Reports whether two-factor is on, half-enrolled, and how many recovery codes remain. Deliberately
    /// exposes no secret material — only state the owner already knows.
    /// </summary>
    public class GetMfaStatusQueryHandler(IAdminRepository adminRepo, ICurrentUserService currentUser)
        : IRequestHandler<GetMfaStatusQuery, Result<MfaStatusDto>>
    {
        public async Task<Result<MfaStatusDto>> Handle(GetMfaStatusQuery request, CancellationToken ct)
        {
            if (currentUser.UserId is not { } id)
                return Result<MfaStatusDto>.Unauthorized();

            var user = await adminRepo.GetByIdAsync(id, ct);
            if (user is null)
                return Result<MfaStatusDto>.NotFound();

            return Result<MfaStatusDto>.Success(new MfaStatusDto(
                Enabled: user.MfaEnabled,
                PendingEnrollment: user.HasPendingMfaEnrollment,
                EnrolledAt: user.MfaEnrolledAt,
                RecoveryCodesRemaining: user.MfaRecoveryCodesRemaining));
        }
    }
}
