using EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Activate;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Entities.Users;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Payors;

public class ActivatePayorAccountCommandHandlerTests
{
    private const string Contact = "09171234567";
    private const string Password = "Secret123!";
    private static readonly Guid StallId = Guid.NewGuid();

    private static PayorActivationCode ValidCode() =>
        PayorActivationCode.Create("ABCD-EFGH", Contact, StallId, DateTime.UtcNow.AddDays(1));

    private static (ActivatePayorAccountCommandHandler handler, Mock<IPayorRepository> repo, Mock<IUnitOfWork> uow)
        Build(PayorActivationCode? code, PayorUser? existing)
    {
        var repo = new Mock<IPayorRepository>();
        repo.Setup(r => r.GetActivationCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(code);
        repo.Setup(r => r.GetByContactNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var token = new Mock<ITokenService>();
        token.Setup(t => t.CreateTokenResponse(It.IsAny<BaseUser>()))
            .ReturnsAsync(new TokenResponseDto { AccessToken = "a", RefreshToken = "r" });

        var uow = new Mock<IUnitOfWork>();
        var muniRepo = new Mock<IMunicipalityRepository>();
        var tenantScope = new EEMOCantilanSDS.Infrastructure.Tenancy.RequestTenantScope();
        return (new ActivatePayorAccountCommandHandler(repo.Object, muniRepo.Object, tenantScope, token.Object, uow.Object), repo, uow);
    }

    private static ActivatePayorAccountCommand Command(string password = Password) =>
        new("ABCD-EFGH", Contact, "Maria Dela Cruz", password);

    [Fact]
    public async Task InvalidCode_IsBadRequest()
    {
        var (handler, repo, _) = Build(code: null, existing: null);

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        repo.Verify(r => r.AddPayorAsync(It.IsAny<PayorUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NewContact_CreatesAccount_AndLinksStall()
    {
        var (handler, repo, _) = Build(ValidCode(), existing: null);

        var result = await handler.Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddPayorAsync(It.IsAny<PayorUser>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.AddStallLinkAsync(It.IsAny<PayorStallLink>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExistingContact_IsConflict_NeverMergesOrCreates()
    {
        // The number already belongs to a payor. Activation must NOT link the code's stall onto that
        // account (that merge was the bug) and must NOT create a duplicate — it directs them to sign in.
        var existing = PayorUser.Create("Diego Villafuerte", Contact, Password);
        var (handler, repo, _) = Build(ValidCode(), existing);

        var result = await handler.Handle(Command(Password), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        repo.Verify(r => r.AddStallLinkAsync(It.IsAny<PayorStallLink>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.AddPayorAsync(It.IsAny<PayorUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
