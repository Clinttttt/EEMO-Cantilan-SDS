using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.Auth.VerifyMyPassword;

public class VerifyMyPasswordQueryHandler(
    IAdminRepository adminRepo,
    ICurrentUserService currentUser) : IRequestHandler<VerifyMyPasswordQuery, Result<bool>>
{
    public async Task<Result<bool>> Handle(VerifyMyPasswordQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actingId)
            return Result<bool>.Unauthorized();

        var actor = await adminRepo.GetByIdAsync(actingId, cancellationToken);
        var matched = actor is not null
            && !string.IsNullOrWhiteSpace(request.Password)
            && actor.VerifyPassword(request.Password);

        // Success carries the match result; a wrong password is a valid "false", not an error.
        return Result<bool>.Success(matched);
    }
}
