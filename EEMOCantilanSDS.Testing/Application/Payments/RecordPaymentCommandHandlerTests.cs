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

        return (new RecordPaymentCommandHandler(paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow)), paymentRepo);
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var collector = CollectorUser.Create("Maria", "EEMO-2026-009", "maria", "maria@eemo.gov", "0917", TestPasswords.Hash("Secret123!"));
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

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        paymentRepo.Verify(r => r.AddAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Legacy_payload_cannot_mutate_a_month_after_cutover_begins_or_stamp_a_new_operation()
    {
        var stall = StallInFacility(FacilityCode.NCC); var record = PaymentRecord.Create(stall.Id, 2026, 10, 1200);
        record.MarkSettlementPendingCutover();
        var (handler, repository) = Build(stall, null, "Admin", null);
        repository.Setup(x => x.GetPaymentRecordAsync(stall.Id, 2026, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRecordDto(record.Id, PaymentStatus.Unpaid, null, 1200, null, null, null, 0, 1200));
        repository.Setup(x => x.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var result = await handler.Handle(new(stall.Id, 2026, 10, PaymentStatus.Paid, null, null, "546564", Guid.NewGuid()), default);
        Assert.Equal(ResultStatus.Conflict, result.Status); Assert.Contains("RECONCILIATION_REQUIRED", result.Error);
        Assert.Equal(PaymentStatus.Unpaid, record.Status); Assert.Null(record.ORNumber); Assert.Null(record.ClientOperationId);
        repository.Verify(x => x.UpdateAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Legacy_payload_cannot_mutate_an_already_canonical_month_or_fabricate_a_receipt()
    {
        var stall = StallInFacility(FacilityCode.NCC); var record = PaymentRecord.Create(stall.Id, 2026, 10, 1200);
        record.MarkSettlementPendingCutover();
        var boundary = DateTime.UtcNow;
        record.ActivateCanonicalSettlement(EEMOCantilanSDS.Domain.Entities.Revenue.CollectionSettlementCutover.Freeze(Guid.NewGuid(),
            EEMOCantilanSDS.Domain.Enums.CollectionSourceKind.PaymentRecord, record.Id, null, record.SettlementVersion,
            boundary, 1200, 0, 1200, "{\"legacyWritersQuiesced\":true}", Guid.NewGuid(), boundary));
        var (handler, repository) = Build(stall, null, "Admin", null);
        repository.Setup(x => x.GetPaymentRecordAsync(stall.Id, 2026, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRecordDto(record.Id, PaymentStatus.Unpaid, null, 1200, null, null, null, 0, 1200, SettlementAuthority.Canonical));
        repository.Setup(x => x.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var result = await handler.Handle(new(stall.Id, 2026, 10, PaymentStatus.Partial, 600, null, "546564", Guid.NewGuid()), default);

        Assert.Equal(ResultStatus.Conflict, result.Status); Assert.Contains("RECONCILIATION_REQUIRED", result.Error);
        Assert.Equal((PaymentStatus.Unpaid, 0m), (record.Status, record.PartialAmount)); Assert.Null(record.ORNumber); Assert.Null(record.ClientOperationId);
        repository.Verify(x => x.UpdateAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
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

        Assert.Equal(ResultStatus.Conflict, result.Status);
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
    public async Task Collector_can_record_clean_unpaid_month_even_if_historical_row_retained_another_collector_id()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var previous = Guid.NewGuid();
        var assigned = CollectorWith(FacilityCode.TCC);
        var (handler, payments) = Build(stall, assigned, "Collector", assigned.Id);
        var month = PaymentRecord.Create(stall.Id, 2026, 10, 2400m, "historical");
        // Simulates a legacy row left Unpaid with an old collector reference.
        month.UpdateStatus(PaymentStatus.Paid, collectorId: previous);
        typeof(PaymentRecord).GetProperty(nameof(PaymentRecord.Status))!.SetValue(month, PaymentStatus.Unpaid);
        typeof(PaymentRecord).GetProperty(nameof(PaymentRecord.PaidAt))!.SetValue(month, null);
        Assert.Equal(previous, month.CollectorId);
        Assert.Equal(0m, month.AmountPaid);
        var id = month.Id;
        payments.Setup(x => x.GetPaymentRecordAsync(stall.Id, 2026, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRecordDto(id, PaymentStatus.Unpaid, null, 2400m, null, null, null, 0, 2400m));
        payments.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(month);

        var result = await handler.Handle(new(stall.Id, 2026, 10, PaymentStatus.Paid, null, null), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(assigned.Id, month.CollectorId);
        Assert.Equal(PaymentStatus.Paid, month.Status);
        payments.Verify(x => x.UpdateAsync(month, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Collector_cannot_take_over_unpaid_row_when_receipt_evidence_remains()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var oldCollector = Guid.NewGuid();
        var assigned = CollectorWith(FacilityCode.TCC);
        var (handler, payments) = Build(stall, assigned, "Collector", assigned.Id);
        var record = PaymentRecord.Create(stall.Id, 2026, 10, 2400m);
        record.UpdateStatus(PaymentStatus.Paid, collectorId: oldCollector);
        record.SetOrNumber("OR-EXISTING");
        // An inconsistent old row: UI says unpaid, but verifiable receipt evidence exists.
        typeof(PaymentRecord).GetProperty(nameof(PaymentRecord.Status))!.SetValue(record, PaymentStatus.Unpaid);
        typeof(PaymentRecord).GetProperty(nameof(PaymentRecord.PaidAt))!.SetValue(record, null);
        payments.Setup(x => x.GetPaymentRecordAsync(stall.Id, 2026, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentRecordDto(record.Id, PaymentStatus.Unpaid, "OR-EXISTING", 2400m, null, null, null, 0, 2400m));
        payments.Setup(x => x.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var result = await handler.Handle(new(stall.Id, 2026, 10, PaymentStatus.Paid, null, null), default);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(oldCollector, record.CollectorId);
        Assert.Equal("OR-EXISTING", record.ORNumber);
        payments.Verify(x => x.UpdateAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
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
        Assert.Equal(ResultStatus.Conflict, result.Status);
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
        Assert.Equal(ResultStatus.Invalid, result.Status);
        paymentRepo.Verify(r => r.AddAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        paymentRepo.Verify(r => r.UpdateAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
