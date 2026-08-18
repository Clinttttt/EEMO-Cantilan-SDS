using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.Login;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Security;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Who may sign in to the ADMIN CONSOLE, the screen that onboards and activates LGUs.
///
/// <para>
/// It admits the dedicated platform operator and nobody else. It used to admit anyone who could sign in to any LGU portal,
/// because the console posts to the shared administrator login - and the office Head of the DEFAULT municipality could get
/// all the way in, since the platform-operator policy also counts "SuperAdmin of the default LGU". A Head runs an LGU;
/// they are not the platform's onboarding operator.
/// </para>
///
/// <para>
/// The policy itself is untouched, because it guards other things (whole-database backups in the portal). This is the
/// narrower question of who the console lets through its front door, and it asks for the dedicated flag.
/// </para>
/// </summary>
public class ConsoleLoginOperatorOnlyTests
{
    private const string Password = "Secret123!";

    private static AdminUser Admin(AdminRole role, bool isPlatformOperator) =>
        AdminUser.Create("Someone", "someone", "someone@lgu.gov", TestPasswords.Hash(Password), role,
            municipalityId: Guid.NewGuid(), isActive: true, isPlatformOperator: isPlatformOperator,
            mustChangePassword: false);

    private static LoginCommandHandler Build(AdminUser user)
    {
        var repo = new Mock<IAuthRepository>();
        var muni = new Mock<IMunicipalityRepository>();
        var token = new Mock<ITokenService>();
        var uow = new Mock<IUnitOfWork>();

        repo.Setup(r => r.GetAdminByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        repo.Setup(r => r.GetAdminByUsernameAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        token.Setup(t => t.CreateTokenResponse(It.IsAny<AdminUser>()))
             .ReturnsAsync(new TokenResponseDto { AccessToken = "at", RefreshToken = "rt" });

        return new LoginCommandHandler(repo.Object, muni.Object, token.Object, uow.Object,
            new IdentityPasswordHasher(), new FixedClock(DateTime.UtcNow));
    }

    private static LoginCommand Console(string password = Password) =>
        new("someone", password, null, RequirePlatformOperator: true);

    [Fact]
    public async Task TheDedicatedOperatorIsLetIn()
    {
        var result = await Build(Admin(AdminRole.SuperAdmin, isPlatformOperator: true))
            .Handle(Console(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("at", result.Value!.AccessToken);
    }

    [Fact]
    public async Task ASuperAdminHeadWhoIsNotAnOperatorIsREFUSED()
    {
        // The report: the office Head signed in on the console because the platform-operator policy counts a default-LGU
        // SuperAdmin. Correct credentials, correct role, wrong door.
        var result = await Build(Admin(AdminRole.SuperAdmin, isPlatformOperator: false))
            .Handle(Console(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Contains("not a platform operator", result.Error);
    }

    [Fact]
    public async Task AnOrdinaryLGUAdminIsREFUSED()
    {
        var result = await Build(Admin(AdminRole.Admin, isPlatformOperator: false))
            .Handle(Console(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task TheREFUSALComesAfterThePasswordSoItRevealsNothing()
    {
        // A wrong password on a non-operator account must look like every other wrong password: a bare 401, never the
        // "not a platform operator" notice, which would confirm the account exists.
        var result = await Build(Admin(AdminRole.SuperAdmin, isPlatformOperator: false))
            .Handle(Console("WrongPassword1!"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.DoesNotContain("platform operator", result.Error ?? string.Empty);
    }

    [Fact]
    public async Task TheORDINARYPortalLoginIsUnchangedForTheSameAccount()
    {
        // The requirement belongs to the console's own endpoint. An LGU administrator signing in to their own portal is
        // not affected by any of this - which is the whole reason it is a flag on the command and not a change to the
        // shared login.
        var result = await Build(Admin(AdminRole.SuperAdmin, isPlatformOperator: false))
            .Handle(new LoginCommand("someone", Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("at", result.Value!.AccessToken);
    }
}
