using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.CreateFirstAdmin;

public class CreateFirstAdminCommandHandler(ISetupRepository setupRepository, IUnitOfWork uow) : IRequestHandler<CreateFirstAdminCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(CreateFirstAdminCommand request, CancellationToken ct)
    {
        var isSuperAdminExists = await setupRepository.IsSuperAdminExistsAsync(ct);
        if (isSuperAdminExists)
            return Result<bool>.Conflict();

        var admin = AdminUser.Create(
            request.FullName,
            request.Username,
            request.Email,
            request.Password,
            AdminRole.SuperAdmin      
        );

        await setupRepository.AddFirstAdminAsync(admin, ct);
        await uow.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
