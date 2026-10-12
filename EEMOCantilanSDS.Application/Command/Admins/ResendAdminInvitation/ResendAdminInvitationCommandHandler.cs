using EEMOCantilanSDS.Application.Command.Admins.InviteAdmin;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.ResendAdminInvitation;

public class ResendAdminInvitationCommandHandler(
    IAdminRepository adminRepo, IUnitOfWork uow, IEmailSender emailSender, IClock clock, ICurrentUserService currentUser)
    : IRequestHandler<ResendAdminInvitationCommand, Result<AdminInvitationDto>>
{
    public async Task<Result<AdminInvitationDto>> Handle(ResendAdminInvitationCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !string.Equals(currentUser.Role, "SuperAdmin", StringComparison.Ordinal))
            return Result<AdminInvitationDto>.Forbidden();

        // Tenant-scoped lookup: another municipality's account is simply not found.
        var admin = await adminRepo.GetByIdAsync(request.AdminId, ct);
        if (admin is null) return Result<AdminInvitationDto>.NotFound();
        if (!admin.HasPendingInvitation)
            return Result<AdminInvitationDto>.Failure("This account has no pending invitation.", ResultStatus.Conflict);

        // A new token replaces the old hash, so the link already sent can no longer be used.
        var (raw, hash) = ActivationTokens.Generate();
        var expires = clock.UtcNow.Add(ActivationTokens.Lifetime);
        admin.SetActivationToken(hash, expires);
        await uow.SaveChangesAsync(ct);

        return Result<AdminInvitationDto>.Success(await InvitationMail.SendAsync(admin, raw, expires, emailSender, ct));
    }
}
