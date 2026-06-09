using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Monthly payment recording is shared by web admins and mobile collectors. Admins are not
/// assignment-restricted; collectors may only record against a facility they are assigned to.
/// </summary>
public class RecordPaymentCommandHandlerTests
{
    private static Stall StallInFacility(FacilityCode code)
    {
        var stall = Stall.Create(Guid.NewGuid(), "1", 2400m, ApplicableFees.BaseRental);
        // Facility is an EF navigation with a private setter; set it for the assignment check.
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(code, code.ToString(), code.ToString()));
        return stall;
    }

    private static (RecordPaymentCommandHandler handler, Mock<IPaymentRepository> paymentRepo) Build(
        Stall stall, CollectorUser? collector, string? role, Guid? collectorId)
    {
        var paymentRepo = new Mock<IPaymentRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        paymentRepo.Setup(r => r.GetPaymentRecordAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentRecordDto?)null);
        if (collector is not null)
            collectorRepo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        currentUser.SetupGet(c => c.Role).Returns(role);
        currentUser.SetupGet(c => c.CollectorId).Returns(collectorId);
        currentUser.SetupGet(c => c.Username).Returns("tester");

        return (new RecordPaymentCommandHandler(paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object), paymentRepo);
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var collector = CollectorUser.Create("Maria", "EEMO-2026-009", "maria", "maria@eemo.gov", "0917", "Secret123!");
        foreach (var code in codes)
            collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), code));
        return collector;
    }

    [Fact]
    public async Task Collector_NotAssignedToFacility_IsForbidden()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var collector = CollectorWith(FacilityCode.NCC); // assigned elsewhere
        var (handler, paymentRepo) = Build(stall, collector, "Collector", collector.Id);

        var result = await handler.Handle(
            new RecordPaymentCommand(stall.Id, 2026, 6, PaymentStatus.Paid, null, null), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
        paymentRepo.Verify(r => r.AddAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Collector_AssignedToFacility_RecordsWithOrNumber()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var collector = CollectorWith(FacilityCode.TCC);
        var (handler, paymentRepo) = Build(stall, collector, "Collector", collector.Id);

        PaymentRecord? captured = null;
        paymentRepo.Setup(r => r.AddAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentRecord, CancellationToken>((p, _) => captured = p).Returns(Task.CompletedTask);

        var result = await handler.Handle(
            new RecordPaymentCommand(stall.Id, 2026, 6, PaymentStatus.Paid, null, null, ORNumber: "00123"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal(PaymentStatus.Paid, captured!.Status);
        Assert.Equal("00123", captured.ORNumber);
        Assert.Equal(collector.Id, captured.CollectorId);
    }
}
