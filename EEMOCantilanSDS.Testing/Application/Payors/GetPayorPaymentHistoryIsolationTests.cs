using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Payors.GetPayorPaymentHistory;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>ST-11: a payor reaches a stall's history only through their own link to it.</summary>
public class GetPayorPaymentHistoryIsolationTests
{
    private static (GetPayorPaymentHistoryQueryHandler handler, Mock<IStallLedgerQueries> ledger) Build(bool linked)
    {
        var payorId = Guid.NewGuid();
        var payors = new Mock<IPayorRepository>();
        payors.Setup(p => p.LinkExistsAsync(payorId, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(linked);
        var ledger = new Mock<IStallLedgerQueries>();
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.UserId).Returns(payorId);
        return (new GetPayorPaymentHistoryQueryHandler(payors.Object, ledger.Object, user.Object), ledger);
    }

    [Fact]
    public async Task AnotherPayorsStall_IsForbidden_AndTheLedgerIsNeverRead()
    {
        var (handler, ledger) = Build(linked: false);

        var result = await handler.Handle(new GetPayorPaymentHistoryQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(EEMOCantilanSDS.Application.Common.ResultStatus.Forbidden, result.Status);
        ledger.Verify(l => l.GetPaymentHistoryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
