using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.UpdateAdmin;

public class UpdateAdminCommandHandler(
    IAdminRepository adminRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow) : IRequestHandler<UpdateAdminCommand, Result<bool>>
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

        var actor = currentUser.Username ?? "Admin";
        admin.UpdateProfile(request.FullName.Trim(), request.Email.Trim(), actor);
        admin.ChangeRole(request.Role, actor);

        await uow.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
