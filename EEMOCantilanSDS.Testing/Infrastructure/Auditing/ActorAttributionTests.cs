using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Command.TaboanMarket.MarkVendorPaid;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class ActorAttributionTests
{
    private static Mock<ICurrentUserService> Actor(Guid? collectorId, string username)
    {
        var m = new Mock<ICurrentUserService>();
        m.SetupGet(c => c.CollectorId).Returns(collectorId);
        m.SetupGet(c => c.Username).Returns(username);
        return m;
    }

    private static async Task<PaymentRecord> RecordPaymentAndCapture(Mock<ICurrentUserService> actor)
    {
        var stall = Stall.Create(Guid.NewGuid(), "1", 900m, ApplicableFees.DailyRental);
        var paymentRepo = new Mock<IPaymentRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var uow = new Mock<IUnitOfWork>();

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        paymentRepo.Setup(r => r.GetPaymentRecordAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentRecordDto?)null);
        PaymentRecord? captured = null;
        paymentRepo.Setup(r => r.AddAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentRecord, CancellationToken>((p, _) => captured = p).Returns(Task.CompletedTask);

        var handler = new RecordPaymentCommandHandler(paymentRepo.Object, stallRepo.Object, collectorRepo.Object, actor.Object, uow.Object);
        await handler.Handle(new RecordPaymentCommand(stall.Id, 2026, 1, PaymentStatus.Paid, null, null), CancellationToken.None);
        return captured!;
    }

    [Fact]
    public async Task RecordPayment_ByCollector_AttributesCollectorId()
    {
        var collectorId = Guid.NewGuid();
        var captured = await RecordPaymentAndCapture(Actor(collectorId, "collector1"));
        Assert.Equal(collectorId, captured.CollectorId);
    }

    [Fact]
    public async Task RecordPayment_ByAdmin_LeavesCollectorIdNull()
    {
        var captured = await RecordPaymentAndCapture(Actor(null, "head"));
        Assert.Null(captured.CollectorId); // admin-recorded: never an admin id in CollectorId
    }

    private static async Task<TpmAttendance> MarkVendorPaidAndCapture(Mock<ICurrentUserService> actor)
    {
        var attendance = TpmAttendance.Create(Guid.NewGuid(), new DateOnly(2026, 1, 2)); // a Friday
        var tpmRepo = new Mock<ITpmRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var uow = new Mock<IUnitOfWork>();
        tpmRepo.Setup(r => r.GetAttendanceByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(attendance);

        var handler = new MarkVendorPaidCommandHandler(tpmRepo.Object, collectorRepo.Object, actor.Object, uow.Object);
        await handler.Handle(new MarkVendorPaidCommand(Guid.NewGuid(), IsPaid: true), CancellationToken.None);
        return attendance;
    }

    [Fact]
    public async Task MarkVendorPaid_ByCollector_AttributesCollectorId()
    {
        var collectorId = Guid.NewGuid();
        var attendance = await MarkVendorPaidAndCapture(Actor(collectorId, "collector1"));
        Assert.True(attendance.IsPaid);
        Assert.Equal(collectorId, attendance.CollectorId);
    }

    [Fact]
    public async Task MarkVendorPaid_ByAdmin_LeavesCollectorIdNull()
    {
        var attendance = await MarkVendorPaidAndCapture(Actor(null, "head"));
        Assert.True(attendance.IsPaid);
        Assert.Null(attendance.CollectorId);
    }
}
