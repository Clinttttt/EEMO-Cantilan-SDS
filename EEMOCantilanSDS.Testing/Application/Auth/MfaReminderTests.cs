using EEMOCantilanSDS.Application.Command.Auth.Mfa;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Auth.GetMfaStatus;
using EEMOCantilanSDS.Domain.Entities.Users;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Two-factor is offered, not compulsory: the portal reminds an account once and then leaves the choice alone.
/// These cover the "once" — the flag the reminder is driven by, and the acknowledgement that clears it.
/// </summary>
public class MfaReminderTests
{
    private static AdminUser NewHead() =>
        AdminUser.Create("Head Admin", "head", "head@eemo.gov", TestPasswords.Hash("Secret123!"), AdminRole.SuperAdmin);

    private static (AcknowledgeMfaReminderCommandHandler ack, GetMfaStatusQueryHandler status, Mock<IUnitOfWork> uow)
        Build(AdminUser user)
    {
        var repo = new Mock<IAdminRepository>();
        var uow = new Mock<IUnitOfWork>();
        var currentUser = new Mock<ICurrentUserService>();

        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(user);
        currentUser.SetupGet(c => c.UserId).Returns(user.Id);

        return (new AcknowledgeMfaReminderCommandHandler(repo.Object, uow.Object, currentUser.Object),
                new GetMfaStatusQueryHandler(repo.Object, currentUser.Object),
                uow);
    }

    [Fact]
    public async Task AnAccountWithoutTwoFactor_IsRemindedOnce()
    {
        var head = NewHead();
        var (ack, status, uow) = Build(head);

        var before = await status.Handle(new GetMfaStatusQuery(), CancellationToken.None);
        Assert.True(before.Value!.ReminderPending);

        await ack.Handle(new AcknowledgeMfaReminderCommand(), CancellationToken.None);

        var after = await status.Handle(new GetMfaStatusQuery(), CancellationToken.None);
        Assert.False(after.Value!.ReminderPending);
        Assert.NotNull(head.MfaReminderShownAt);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcknowledgingTwice_KeepsTheFirstTimestamp()
    {
        // Two tabs, or a refresh mid-dismiss, must not be an error and must not move the record.
        var head = NewHead();
        var (ack, _, _) = Build(head);

        await ack.Handle(new AcknowledgeMfaReminderCommand(), CancellationToken.None);
        var first = head.MfaReminderShownAt;

        var second = await ack.Handle(new AcknowledgeMfaReminderCommand(), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(first, head.MfaReminderShownAt);
    }
}
