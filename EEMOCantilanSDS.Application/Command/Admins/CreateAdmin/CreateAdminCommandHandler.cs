using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
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
    ITenantContext tenantContext,
    IEmailVerificationSender verificationSender,
    IPasswordHasher passwordHasher)
    : IRequestHandler<CreateAdminCommand, Result<AdminDto>>
{
    public async Task<Result<AdminDto>> Handle(CreateAdminCommand request, CancellationToken cancellationToken)
    {
        var admin = AdminUser.Create(
            request.FullName.Trim(),
            request.Username.Trim(),
            request.Email.Trim(),
            passwordHasher.Hash(request.Password),
            request.Role);

        await adminRepo.AddAsync(admin, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, cancellationToken);

        // Ask the new admin to confirm their address. Confirming is what later allows them to reset their
        // own password; until then only the Head can restore their access. Best-effort by design — the
        // account is already created, so an email problem must never fail this operation.
        await verificationSender.SendAsync(admin, save: true, cancellationToken);

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
