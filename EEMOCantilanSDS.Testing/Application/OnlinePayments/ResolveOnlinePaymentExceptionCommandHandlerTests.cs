using System.Reflection;
using EEMOCantilanSDS.Api.Controllers;
using EEMOCantilanSDS.Application.Command.OnlinePayments.ResolveException;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.OnlinePayments;

public class ResolveOnlinePaymentExceptionCommandHandlerTests
{
    private static OnlinePaymentTransaction Held()
    {
        var txn = OnlinePaymentTransaction.Create("EEMO-OP-HELD", Guid.NewGuid(), Guid.NewGuid(), 100m, "PayMongo");
        txn.SetPending("cs_held", "https://checkout");
        txn.MarkPaid("pay_held", "gcash", DateTime.UtcNow, "{}");
        txn.MarkSettlementReconciliationRequired("duplicate capture");
        return txn;
    }

    private static (ResolveOnlinePaymentExceptionCommandHandler handler, Mock<IUnitOfWork> uow) Build(OnlinePaymentTransaction? txn)
    {
        var repo = new Mock<IOnlinePaymentRepository>();
        if (txn is not null)
            repo.Setup(r => r.GetByIdAsync(txn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(txn);
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.Username).Returns("head.officer");
        var uow = new Mock<IUnitOfWork>();
        return (new ResolveOnlinePaymentExceptionCommandHandler(repo.Object, user.Object, uow.Object, new FixedClock(new DateTime(2026, 11, 4, 2, 0, 0, DateTimeKind.Utc))), uow);
    }

    [Fact]
    public async Task RecordsTheDecisionWithWhoAndWhen_AndKeepsWhyItWasHeld()
    {
        var txn = Held();
        var (handler, uow) = Build(txn);

        var result = await handler.Handle(new ResolveOnlinePaymentExceptionCommand(
            txn.Id, OnlinePaymentDisposition.Refunded, " ref_123 ", "Returned through PayMongo"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(txn.IsAwaitingOfficeDecision);
        Assert.Equal(OnlinePaymentDisposition.Refunded, txn.Disposition);
        Assert.Equal("ref_123", txn.DispositionReference);
        Assert.Equal("Returned through PayMongo", txn.DispositionNote);
        Assert.Equal("head.officer", txn.DispositionBy);
        Assert.Equal(new DateTime(2026, 11, 4, 2, 0, 0, DateTimeKind.Utc), txn.DispositionAtUtc);
        // History is appended to, not rewritten.
        Assert.Equal(OnlinePaymentStatus.ReconciliationRequired, txn.Status);
        Assert.Equal("duplicate capture", txn.ReconciliationReason);
        Assert.Equal("pay_held", txn.PaymentId);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ASecondDecisionIsRefused_AndTheFirstStands()
    {
        var txn = Held();
        var (handler, uow) = Build(txn);
        await handler.Handle(new ResolveOnlinePaymentExceptionCommand(txn.Id, OnlinePaymentDisposition.Applied, "OR-1"), CancellationToken.None);

        var second = await handler.Handle(new ResolveOnlinePaymentExceptionCommand(txn.Id, OnlinePaymentDisposition.Refunded, "ref_2"), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, second.Status);
        Assert.Equal(OnlinePaymentDisposition.Applied, txn.Disposition);
        Assert.Equal("OR-1", txn.DispositionReference);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnOrdinaryPaidPayment_CannotBeResolved()
    {
        var txn = OnlinePaymentTransaction.Create("EEMO-OP-PAID", Guid.NewGuid(), Guid.NewGuid(), 100m, "PayMongo");
        txn.SetPending("cs_paid", "https://checkout");
        txn.MarkPaid("pay_paid", "gcash", DateTime.UtcNow, "{}");
        var (handler, uow) = Build(txn);

        var result = await handler.Handle(new ResolveOnlinePaymentExceptionCommand(txn.Id, OnlinePaymentDisposition.Refunded, "ref"), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Null(txn.Disposition);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnotherOfficesPayment_IsNotFound()
    {
        // The repository is tenant-filtered, so another LGU's id resolves to nothing here.
        var (handler, uow) = Build(null);

        var result = await handler.Handle(new ResolveOnlinePaymentExceptionCommand(Guid.NewGuid(), OnlinePaymentDisposition.Refunded, "ref"), CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(nameof(OnlinePaymentsController.ResolveExceptionAsync), "SuperAdmin")]
    [InlineData(nameof(OnlinePaymentsController.ExceptionsAsync), "SuperAdmin,Admin")]
    [InlineData(nameof(OnlinePaymentsController.ReconcileAsync), "SuperAdmin,Admin")]
    public void OnlyTheHeadDecides_WhileBothViewAndVerify(string action, string roles)
    {
        var attribute = typeof(OnlinePaymentsController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.Equal(roles, attribute!.Roles);
    }
}
