using System;
using System.Threading;
using System.Threading.Tasks;
using EEMOCantilanSDS.Application.Command.Auth.AdminAuth.SendMyEmailVerification;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Auth.GetMyEmailConfirmation;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;
using Xunit;

namespace EEMOCantilanSDS.Testing.Application.Auth;

/// <summary>
/// An account confirming its own email address.
///
/// <para>
/// Why this exists at all: a self-service password reset is only ever sent to a CONFIRMED address, and the platform
/// operator's was never confirmed. The Head-triggered path cannot help it — a municipality's roster deliberately excludes
/// platform operators, and there is nobody above the operator to act for it either. So the operator was the one account
/// on the platform that could never obtain a reset link. It confirms its own address here.
/// </para>
///
/// <para>
/// The subject is always the caller, taken from the token. There is no id to pass, so this can never be pointed at
/// another account.
/// </para>
/// </summary>
public class MyEmailConfirmationHandlerTests
{
    private static readonly Guid CallerId = Guid.NewGuid();

    private static AdminUser Operator(string? email = "operator@stalltrack.site", bool verified = false)
    {
        var account = AdminUser.Create("Platform Operator", "console.admin", email!, TestPasswords.Hash("Passw0rd1"),
            AdminRole.SuperAdmin, Guid.NewGuid(), isActive: true, isPlatformOperator: true, mustChangePassword: false);
        if (verified) account.MarkEmailVerified();
        return account;
    }

    private static (SendMyEmailVerificationCommandHandler handler, Mock<IEmailVerificationSender> sender, Mock<IUnitOfWork> uow)
        Build(AdminUser? caller, Guid? actingId, bool sendSucceeds = true)
    {
        var repo = new Mock<IAdminRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(caller);

        var current = new Mock<ICurrentUserService>();
        current.SetupGet(c => c.UserId).Returns(actingId);

        var sender = new Mock<IEmailVerificationSender>();
        sender.Setup(s => s.SendAsync(It.IsAny<BaseUser>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(sendSucceeds);

        var uow = new Mock<IUnitOfWork>();

        return (new SendMyEmailVerificationCommandHandler(repo.Object, current.Object, sender.Object, uow.Object), sender, uow);
    }

    [Fact]
    public async Task TheCallersOwnAddressIsSentAConfirmation()
    {
        var account = Operator();
        var (handler, sender, uow) = Build(account, CallerId);

        var result = await handler.Handle(new SendMyEmailVerificationCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        sender.Verify(s => s.SendAsync(account, false, It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnAlreadyConfirmedAddressIsSaidSo_NotSentAgain()
    {
        // A second link would retire the one already in the mailbox, and a confirmed address has nothing to prove.
        var (handler, sender, uow) = Build(Operator(verified: true), CallerId);

        var result = await handler.Handle(new SendMyEmailVerificationCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("already confirmed", result.Error!, StringComparison.OrdinalIgnoreCase);
        sender.Verify(s => s.SendAsync(It.IsAny<BaseUser>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnAccountWithNoAddressIsRefused()
    {
        // The entity refuses to be created without an address, so it is cleared afterwards to reach the state a
        // long-standing account can be in.
        var account = Operator();
        account.UpdateProfile(account.FullName!, account.Username!, string.Empty, "Test");
        var (handler, sender, _) = Build(account, CallerId);

        var result = await handler.Handle(new SendMyEmailVerificationCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        sender.Verify(s => s.SendAsync(It.IsAny<BaseUser>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithNoSessionNothingIsSent()
    {
        var (handler, sender, _) = Build(Operator(), actingId: null);

        var result = await handler.Handle(new SendMyEmailVerificationCommand(), CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        sender.Verify(s => s.SendAsync(It.IsAny<BaseUser>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AFailedSendLeavesNoTokenBehind()
    {
        // The token is stamped on the entity before the email is attempted, so a failure must not be committed: the
        // account would otherwise carry a pending token for a link nobody ever received.
        var (handler, _, uow) = Build(Operator(), CallerId, sendSucceeds: false);

        var result = await handler.Handle(new SendMyEmailVerificationCommand(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TheStatusStatesTheAddressAndWhetherItIsConfirmed()
    {
        var repo = new Mock<IAdminRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Operator());
        var current = new Mock<ICurrentUserService>();
        current.SetupGet(c => c.UserId).Returns(CallerId);

        var result = await new GetMyEmailConfirmationQueryHandler(repo.Object, current.Object)
            .Handle(new GetMyEmailConfirmationQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("operator@stalltrack.site", result.Value!.Email);
        Assert.False(result.Value.Verified);
    }
}
