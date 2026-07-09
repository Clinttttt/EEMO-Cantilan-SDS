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

        // Stamp the Head to the DEFAULT (Cantilan) LGU explicitly, so this never depends on the request's
        // resolved tenant (the setup call is unauthenticated).
        var defaultMunicipalityId = await setupRepository.GetDefaultMunicipalityIdAsync(ct);

        var admin = AdminUser.Create(
            request.FullName,
            request.Username,
            request.Email,
            request.Password,
            AdminRole.SuperAdmin,
            municipalityId: defaultMunicipalityId
        );

        await setupRepository.AddFirstAdminAsync(admin, ct);
        await uow.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
