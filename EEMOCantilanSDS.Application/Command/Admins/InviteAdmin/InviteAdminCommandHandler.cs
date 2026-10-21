using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Onboarding;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.InviteAdmin;

public class InviteAdminCommandHandler(
    IAdminRepository adminRepo,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext,
    IPasswordHasher passwordHasher,
    IEmailSender emailSender,
    IClock clock,
    ICurrentUserService currentUser)
    : IRequestHandler<InviteAdminCommand, Result<AdminInvitationDto>>
{
    public async Task<Result<AdminInvitationDto>> Handle(InviteAdminCommand request, CancellationToken ct)
    {
        // Defence in depth beside the controller's [Authorize]: only the municipality's Head issues invitations.
        if (!currentUser.IsAuthenticated || !string.Equals(currentUser.Role, "SuperAdmin", StringComparison.Ordinal))
            return Result<AdminInvitationDto>.Forbidden();

        // Inactive, holding a random password nobody knows. MustChangePassword stays false: the password the owner sets through the
        // link is THEIR OWN choice (the requirement is for passwords someone else issued, and none is issued here).
        var admin = AdminUser.Create(
            request.FullName.Trim(), request.Username.Trim(), request.Email.Trim(),
            passwordHasher.Hash(ActivationTokens.RandomPlaceholderPassword()), request.Role,
            isActive: false, mustChangePassword: false);

        var (raw, hash) = ActivationTokens.Generate();
        var expires = clock.UtcNow.Add(ActivationTokens.Lifetime);
        admin.SetActivationToken(hash, expires);

        await adminRepo.AddAsync(admin, ct);
        await uow.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);

        return Result<AdminInvitationDto>.Success(await InvitationMail.SendAsync(admin, raw, expires, emailSender, ct));
    }
}
