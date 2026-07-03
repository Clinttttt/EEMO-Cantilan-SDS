using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Admins.CreateAdmin;

public class CreateAdminCommandHandler(
    IAdminRepository adminRepo,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext)
    : IRequestHandler<CreateAdminCommand, Result<AdminDto>>
{
    public async Task<Result<AdminDto>> Handle(CreateAdminCommand request, CancellationToken cancellationToken)
    {
        var admin = AdminUser.Create(
            request.FullName.Trim(),
            request.Username.Trim(),
            request.Email.Trim(),
            request.Password,
            request.Role);

        await adminRepo.AddAsync(admin, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, cancellationToken);

        var dto = new AdminDto(
            admin.Id,
            admin.FullName!,
            admin.Username!,
            admin.Email!,
            admin.Role,
            admin.IsActive);

        return Result<AdminDto>.Success(dto);
    }
}
