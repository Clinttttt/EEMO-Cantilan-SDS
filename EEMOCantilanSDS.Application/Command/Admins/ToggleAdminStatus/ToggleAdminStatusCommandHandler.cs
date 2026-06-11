using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.ToggleAdminStatus;

public class ToggleAdminStatusCommandHandler(
    IAdminRepository adminRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow) : IRequestHandler<ToggleAdminStatusCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ToggleAdminStatusCommand request, CancellationToken cancellationToken)
    {
        var admin = await adminRepo.GetByIdAsync(request.AdminId, cancellationToken);
        if (admin is null) return Result<bool>.NotFound();

        if (!request.Activate)
        {
            // Guard: you cannot deactivate your own signed-in account.
            if (currentUser.UserId == admin.Id)
                return Result<bool>.Failure("You cannot deactivate your own account.");

            // Guard: never deactivate the last remaining active SuperAdmin (the Head).
            if (admin.Role == AdminRole.SuperAdmin)
            {
                var others = await adminRepo.CountOtherActiveSuperAdminsAsync(admin.Id, cancellationToken);
                if (others == 0)
                    return Result<bool>.Failure("At least one active Head (SuperAdmin) must remain.");
            }
        }

        var actor = currentUser.Username ?? "Admin";
        if (request.Activate) admin.Activate(actor);
        else admin.Deactivate(actor);

        await uow.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
