using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.Mfa;

/// <summary>
/// Records that the signed-in account has been shown the two-factor reminder, so it is not shown again.
/// Two-factor itself is unchanged by this: the account may still enrol later from Settings at any time.
/// </summary>
public record AcknowledgeMfaReminderCommand : IRequest<Result<bool>>;

public class AcknowledgeMfaReminderCommandHandler(
    IAdminRepository adminRepo,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser)
    : IRequestHandler<AcknowledgeMfaReminderCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(AcknowledgeMfaReminderCommand request, CancellationToken ct)
    {
        if (currentUser.UserId is not { } id)
            return Result<bool>.Unauthorized();

        var user = await adminRepo.GetByIdAsync(id, ct);
        if (user is null)
            return Result<bool>.NotFound();

        // Idempotent: dismissing twice (two tabs, a refresh mid-dismiss) is not an error.
        user.MarkMfaReminderShown();
        await unitOfWork.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
