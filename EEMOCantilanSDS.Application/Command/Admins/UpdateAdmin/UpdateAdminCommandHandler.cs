using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.UpdateAdmin;

public class UpdateAdminCommandHandler(
    IAdminRepository adminRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<UpdateAdminCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateAdminCommand request, CancellationToken cancellationToken)
    {
        var admin = await adminRepo.GetByIdAsync(request.AdminId, cancellationToken);
        if (admin is null) return Result<bool>.NotFound();

        // Guard: never demote the last remaining active SuperAdmin (would lock everyone out of
        // account management). This covers demoting yourself or the only other Head.
        if (admin.Role == AdminRole.SuperAdmin && request.Role != AdminRole.SuperAdmin)
        {
            var others = await adminRepo.CountOtherActiveSuperAdminsAsync(admin.Id, cancellationToken);
            if (others == 0)
                return Result<bool>.Failure("At least one active Head (SuperAdmin) must remain.");
        }

        var newUsername = request.Username.Trim();
        // Only validate uniqueness when the username actually changes (so keeping it is never
        // flagged as "taken by yourself"). IsUsernameUniqueAsync ignores the soft-delete filter.
        if (!string.Equals(newUsername, admin.Username, StringComparison.OrdinalIgnoreCase)
            && !await adminRepo.IsUsernameUniqueAsync(newUsername, cancellationToken))
        {
            return Result<bool>.Conflict();
        }

        var actor = currentUser.Username ?? "Admin";
        admin.UpdateProfile(request.FullName.Trim(), newUsername, request.Email.Trim(), actor);
        admin.ChangeRole(request.Role, actor);

        await uow.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, cancellationToken);
        return Result<bool>.Success(true);
    }
}
