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
        paymentRepo.Setup(r => r.IsMonthlyOrAvailableForStallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        if (collector is not null)
            collectorRepo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>())).ReturnsAsync(collector);
        currentUser.SetupGet(c => c.Role).Returns(role);
        currentUser.SetupGet(c => c.CollectorId).Returns(collectorId);
        currentUser.SetupGet(c => c.Username).Returns("tester");

        return (new RecordPaymentCommandHandler(paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant), paymentRepo);
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

    [Fact]
    public async Task NewPayment_WithDuplicateOrNumber_ReturnsConflict()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var (handler, paymentRepo) = Build(stall, null, "Admin", null);
        paymentRepo.Setup(r => r.IsMonthlyOrAvailableForStallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await handler.Handle(
            new RecordPaymentCommand(stall.Id, 2026, 6, PaymentStatus.Paid, null, null, ORNumber: "DUP-1"),
            CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        paymentRepo.Verify(r => r.AddAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExistingPayment_ResavingTheSameOrNumber_IsAllowed()
    {
        // Careful case: partial -> full re-record keeps the OR already on the SAME record.
        // A global uniqueness check would find that OR (it's this record) and must NOT reject it.
        var stall = StallInFacility(FacilityCode.TCC);
        var (handler, paymentRepo) = Build(stall, null, "Admin", null);

        var existing = PaymentRecord.Create(stall.Id, 2026, 6, 2400m, "tester");
        existing.UpdateStatus(PaymentStatus.Partial, 1000m, null, "tester", null);
        existing.SetOrNumber("00123", "tester");

        var dtoId = Guid.NewGuid();
        paymentRepo.Setup(r => r.GetPaymentRecordAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRecordDto(dtoId, PaymentStatus.Partial, "00123", 2400m, null, null, null, 1000m, 1400m));
        paymentRepo.Setup(r => r.GetByIdAsync(dtoId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        // OR "exists" globally precisely because it is on this same record.
        paymentRepo.Setup(r => r.IsMonthlyOrAvailableForStallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await handler.Handle(
            new RecordPaymentCommand(stall.Id, 2026, 6, PaymentStatus.Paid, null, null, ORNumber: "00123"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        paymentRepo.Verify(r => r.UpdateAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Collector_CannotOverwritePaymentRecordedByAnotherCollector_OnSharedFacility()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var firstCollectorId = Guid.NewGuid();
        var secondCollector = CollectorWith(FacilityCode.TCC);
        var (handler, paymentRepo) = Build(stall, secondCollector, "Collector", secondCollector.Id);

        var existing = PaymentRecord.Create(stall.Id, 2026, 6, 2400m, "collector-a");
        existing.UpdateStatus(PaymentStatus.Paid, 0m, null, "collector-a", firstCollectorId);
        existing.SetOrNumber("OR-A", "collector-a");

        var dtoId = Guid.NewGuid();
        paymentRepo.Setup(r => r.GetPaymentRecordAsync(stall.Id, 2026, 6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRecordDto(dtoId, PaymentStatus.Paid, "OR-A", 2400m, null, null, null, 2400m, 0m));
        paymentRepo.Setup(r => r.GetByIdAsync(dtoId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await handler.Handle(
            new RecordPaymentCommand(stall.Id, 2026, 6, PaymentStatus.Paid, null, null, ORNumber: "OR-B"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(firstCollectorId, existing.CollectorId);
        Assert.Equal("OR-A", existing.ORNumber);
        paymentRepo.Verify(r => r.UpdateAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Collector_CanUpdateOwnExistingPayment_OnSharedFacility()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var collector = CollectorWith(FacilityCode.TCC);
        var (handler, paymentRepo) = Build(stall, collector, "Collector", collector.Id);

        var existing = PaymentRecord.Create(stall.Id, 2026, 6, 2400m, "collector-a");
        existing.UpdateStatus(PaymentStatus.Partial, 1000m, null, "collector-a", collector.Id);
        existing.SetOrNumber("OR-A", "collector-a");

        var dtoId = Guid.NewGuid();
        paymentRepo.Setup(r => r.GetPaymentRecordAsync(stall.Id, 2026, 6, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRecordDto(dtoId, PaymentStatus.Partial, "OR-A", 2400m, null, null, null, 1000m, 1400m));
        paymentRepo.Setup(r => r.GetByIdAsync(dtoId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        paymentRepo.Setup(r => r.IsMonthlyOrAvailableForStallAsync("OR-A", It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await handler.Handle(
            new RecordPaymentCommand(stall.Id, 2026, 6, PaymentStatus.Paid, null, null, ORNumber: "OR-A"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(collector.Id, existing.CollectorId);
        Assert.Equal(PaymentStatus.Paid, existing.Status);
        paymentRepo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NpmStall_IsRejected_NoMonthlyRecordCreated()
    {
        // NPM is collected daily (₱30/day). A monthly PaymentRecord must never be created for it —
        // it would diverge from the daily ledger (a ₱500 partial ≠ 16×₱30) and double-count in reports.
        var stall = StallInFacility(FacilityCode.NPM);
        var (handler, paymentRepo) = Build(stall, null, "Admin", null);

        var result = await handler.Handle(
            new RecordPaymentCommand(stall.Id, 2026, 6, PaymentStatus.Partial, 500m, null, ORNumber: "3121212"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        paymentRepo.Verify(r => r.AddAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        paymentRepo.Verify(r => r.UpdateAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
