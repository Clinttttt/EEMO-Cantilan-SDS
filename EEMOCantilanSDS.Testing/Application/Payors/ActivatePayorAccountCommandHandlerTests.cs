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
        Build(PayorActivationCode? code, PayorUser? existing, bool linkExists = false)
    {
        var repo = new Mock<IPayorRepository>();
        repo.Setup(r => r.GetActivationCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(code);
        repo.Setup(r => r.GetByContactNumberAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        repo.Setup(r => r.LinkExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(linkExists);

        var token = new Mock<ITokenService>();
        token.Setup(t => t.CreateTokenResponse(It.IsAny<BaseUser>()))
            .ReturnsAsync(new TokenResponseDto { AccessToken = "a", RefreshToken = "r" });

        var uow = new Mock<IUnitOfWork>();
        return (new ActivatePayorAccountCommandHandler(repo.Object, token.Object, uow.Object), repo, uow);
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
    public async Task ExistingContact_CorrectPassword_LinksAdditionalStall_NoNewAccount()
    {
        var existing = PayorUser.Create("Maria Dela Cruz", Contact, Password);
        var (handler, repo, _) = Build(ValidCode(), existing, linkExists: false);

        var result = await handler.Handle(Command(Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddPayorAsync(It.IsAny<PayorUser>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.AddStallLinkAsync(It.IsAny<PayorStallLink>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExistingContact_WrongPassword_IsConflict_AndDoesNotLink()
    {
        var existing = PayorUser.Create("Maria Dela Cruz", Contact, Password);
        var (handler, repo, _) = Build(ValidCode(), existing);

        var result = await handler.Handle(Command("WrongPass123!"), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        repo.Verify(r => r.AddStallLinkAsync(It.IsAny<PayorStallLink>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.AddPayorAsync(It.IsAny<PayorUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExistingContact_CorrectPassword_AlreadyLinked_DoesNotDuplicateLink()
    {
        var existing = PayorUser.Create("Maria Dela Cruz", Contact, Password);
        var (handler, repo, _) = Build(ValidCode(), existing, linkExists: true);

        var result = await handler.Handle(Command(Password), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.AddStallLinkAsync(It.IsAny<PayorStallLink>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
