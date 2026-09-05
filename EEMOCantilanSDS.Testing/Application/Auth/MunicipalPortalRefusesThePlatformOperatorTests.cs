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
/// The platform operator cannot sign in to a municipality's own portal.
/// </summary>
/// <remarks>
/// The mirror of <see cref="ConsoleLoginOperatorOnlyTests"/>, and it closes a real hole rather than a theoretical one.
///
/// <para>The operator account is created under the DEFAULT municipality "for tenant context" - see
/// <c>CreateFirstConsoleAdminCommandHandler</c> - so its <c>MunicipalityId</c> equals that LGU's. The login handler's tenant
/// boundary asks only whether the account belongs to the requested municipality, so for the default LGU it PASSED, and the
/// municipal endpoint does not require the operator flag. The operator could therefore sign in to that office's console and read
/// its collections, vendors and reports. Other municipalities were never reachable, their ids not matching - so the exposure was
/// exactly one office, and it was the office that never agreed to it.</para>
///
/// <para>The operator's business is onboarding, activation and the platform's own tools, every one of which lives behind
/// console-login. Nothing it does needs a municipal session.</para>
/// </remarks>
public class MunicipalPortalRefusesThePlatformOperatorTests
{
    private const string Password = "Str0ng!Passw0rd";

    /// <summary>The default municipality, which the operator account is stamped with.</summary>
    private static readonly Guid DefaultMunicipality = Guid.NewGuid();

    private static AdminUser Admin(bool isPlatformOperator, Guid? municipalityId = null) =>
        AdminUser.Create("Someone", "someone", "someone@lgu.gov", TestPasswords.Hash(Password), AdminRole.SuperAdmin,
            municipalityId: municipalityId ?? DefaultMunicipality, isActive: true,
            isPlatformOperator: isPlatformOperator, mustChangePassword: false);

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

    /// <summary>A municipal sign-in: the portal endpoint, which does not ask for the operator flag.</summary>
    private static LoginCommand Portal() => new("someone", Password, null, RequirePlatformOperator: false);

    [Fact]
    public async Task TheOperatorIsRefusedAMunicipalPortalSession()
    {
        var result = await Build(Admin(isPlatformOperator: true)).Handle(Portal(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    /// <summary>
    /// The office's own Head is untouched, which is the half that must not break.
    /// </summary>
    [Fact]
    public async Task TheOfficesOwnHeadStillSignsIn()
    {
        var result = await Build(Admin(isPlatformOperator: false)).Handle(Portal(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("at", result.Value!.AccessToken);
    }

    /// <summary>
    /// Refused for being the operator, not for a bad password - so the refusal cannot be used to find the account.
    /// </summary>
    /// <remarks>
    /// The check sits AFTER the password verification, like the two boundaries beside it. A wrong password on the operator
    /// account must therefore look exactly like a wrong password on any other: Unauthorized, not Forbidden. Otherwise the
    /// difference between the two answers tells a stranger which username the operator holds.
    /// </remarks>
    [Fact]
    public async Task AWrongPasswordOnTheOperatorAccountLooksLikeAnyOtherWrongPassword()
    {
        var result = await Build(Admin(isPlatformOperator: true))
            .Handle(new LoginCommand("someone", "not-the-password", null, RequirePlatformOperator: false),
                CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    /// <summary>The console still admits the operator: this change closes one door and leaves the other open.</summary>
    [Fact]
    public async Task TheConsoleStillAdmitsTheOperator()
    {
        var result = await Build(Admin(isPlatformOperator: true))
            .Handle(new LoginCommand("someone", Password, null, RequirePlatformOperator: true),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
