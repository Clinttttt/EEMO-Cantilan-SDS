using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.RevokeAdminInvitation;

public class RevokeAdminInvitationCommandHandler(
    IAdminRepository adminRepo, IUnitOfWork uow, IEemoCacheInvalidator cacheInvalidator, ITenantContext tenantContext, ICurrentUserService currentUser)
    : IRequestHandler<RevokeAdminInvitationCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RevokeAdminInvitationCommand request, CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated || !string.Equals(currentUser.Role, "SuperAdmin", StringComparison.Ordinal))
            return Result<bool>.Forbidden();

        var admin = await adminRepo.GetByIdAsync(request.AdminId, ct);
        if (admin is null) return Result<bool>.NotFound();

        // Only an invitation nobody has accepted can be withdrawn this way. An account in use is deactivated through the
        // existing status control, never deleted from here.
        if (!admin.HasPendingInvitation)
            return Result<bool>.Failure("Only a pending invitation can be revoked.", ResultStatus.Conflict);

        admin.RevokeActivationToken();
        admin.SoftDelete(currentUser.Username ?? "Head"); // frees the username and email for a fresh invitation
        await uow.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, ct);
        return Result<bool>.Success(true);
    }
}
