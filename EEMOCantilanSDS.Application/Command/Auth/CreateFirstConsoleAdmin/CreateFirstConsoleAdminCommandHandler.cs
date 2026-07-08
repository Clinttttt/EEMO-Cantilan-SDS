using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Application.Command.Auth.CreateFirstConsoleAdmin
{
    public class CreateFirstConsoleAdminCommandHandler(IAppDbContext context)
        : IRequestHandler<CreateFirstConsoleAdminCommand, Result<bool>>
    {
        public async Task<Result<bool>> Handle(CreateFirstConsoleAdminCommand request, CancellationToken ct)
        {
            // Only one first-run: refuse once any platform operator exists.
            var operatorExists = await context.AdminUsers
                .IgnoreQueryFilters()
                .AnyAsync(u => u.IsPlatformOperator && !u.IsDeleted, ct);
            if (operatorExists)
                return Result<bool>.Conflict();

            var defaultMunicipalityId = await context.Municipalities
                .IgnoreQueryFilters()
                .Where(m => m.IsDefault)
                .Select(m => (System.Guid?)m.Id)
                .FirstOrDefaultAsync(ct);
            if (defaultMunicipalityId is null)
                return Result<bool>.Failure("The platform is not initialized yet.");

            var username = request.Username.Trim();
            var usernameTaken = await context.Users
                .IgnoreQueryFilters()
                .AnyAsync(u => u.MunicipalityId == defaultMunicipalityId && u.Username == username && !u.IsDeleted, ct);
            if (usernameTaken)
                return Result<bool>.Failure($"Username '{username}' is already taken.");

            // Dedicated console operator: platform-operator flag set, own password (no forced change),
            // provisioned under the default municipality for tenant context.
            var operatorAdmin = AdminUser.Create(
                request.FullName.Trim(),
                username,
                request.Email.Trim(),
                request.Password,
                AdminRole.SuperAdmin,
                defaultMunicipalityId.Value,
                isActive: true,
                isPlatformOperator: true,
                mustChangePassword: false);

            context.AdminUsers.Add(operatorAdmin);
            await context.SaveChangesAsync(ct);

            return Result<bool>.Success(true);
        }
    }
}
