using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Auth.GetMyEmailConfirmation
{
    public class GetMyEmailConfirmationQueryHandler(
        IAdminRepository adminRepo,
        ICurrentUserService currentUser) : IRequestHandler<GetMyEmailConfirmationQuery, Result<MyEmailConfirmationDto>>
    {
        public async Task<Result<MyEmailConfirmationDto>> Handle(GetMyEmailConfirmationQuery request, CancellationToken ct)
        {
            if (currentUser.UserId is not { } actingId)
                return Result<MyEmailConfirmationDto>.Unauthorized();

            var admin = await adminRepo.GetByIdAsync(actingId, ct);
            if (admin is null) return Result<MyEmailConfirmationDto>.Unauthorized();

            return Result<MyEmailConfirmationDto>.Success(
                new MyEmailConfirmationDto(admin.Email, admin.EmailVerified));
        }
    }
}
