using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.CreateFirstAdmin;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Security;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Auth;

/// <summary>
/// The first Head chooses their own password during first-run setup, so they are not sent to a second change-password screen.
/// The requirement stays for passwords issued by someone else; this pins only the account first-run setup creates.
/// </summary>
public class CreateFirstAdminCommandHandlerTests
{
    private static (CreateFirstAdminCommandHandler Handler, Func<AdminUser?> Created) Build(bool alreadyExists, Guid defaultTenant)
    {
        AdminUser? created = null;
        var setup = new Mock<ISetupRepository>();
        setup.Setup(s => s.IsSuperAdminExistsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(alreadyExists);
        setup.Setup(s => s.GetDefaultMunicipalityIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(defaultTenant);
        setup.Setup(s => s.AddFirstAdminAsync(It.IsAny<AdminUser>(), It.IsAny<CancellationToken>()))
            .Callback<AdminUser, CancellationToken>((a, _) => created = a).Returns(Task.CompletedTask);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns(new HashedPassword("hashed"));
        return (new CreateFirstAdminCommandHandler(setup.Object, uow.Object, hasher.Object), () => created);
    }

    private static CreateFirstAdminCommand Command() => new("System Head", "head", "head@example.gov.ph", "Str0ngPass");

    [Fact]
    public async Task TheFirstHead_ChoseTheirOwnPassword_SoIsNotForcedToChangeIt()
    {
        var tenant = Guid.NewGuid();
        var (handler, created) = Build(alreadyExists: false, tenant);

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var head = created()!;
        Assert.False(head.MustChangePassword);
        Assert.Equal(AdminRole.SuperAdmin, head.Role);
        Assert.Equal(tenant, head.MunicipalityId); // stamped to the default LGU, not the request's tenant
        Assert.True(head.IsActive);
    }

    [Fact]
    public async Task AFirstHeadCannotBeCreatedTwice_AndNothingIsWritten()
    {
        var (handler, created) = Build(alreadyExists: true, Guid.NewGuid());

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Null(created());
    }

    [Fact]
    public void TheDefaultForAnyOtherAccountIsStillAForcedChange()
    {
        // Only the first-run Head opts out explicitly. Accounts created without saying so (a Head adding an Admin with a
        // temporary password) keep the requirement.
        var other = AdminUser.Create("Staff", "staff", "s@x.ph", new HashedPassword("h"), AdminRole.Admin, Guid.NewGuid());
        Assert.True(other.MustChangePassword);
    }
}
