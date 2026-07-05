using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class RecordDailyCollectionCommandHandlerTests
{
    private static Stall StallInFacility(FacilityCode code)
    {
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(code, code.ToString(), code.ToString()));
        return stall;
    }

    private static CollectorUser CollectorWith(params FacilityCode[] codes)
    {
        var collector = CollectorUser.Create("Maria", "EEMO-2026-010", "maria", "maria@eemo.gov", "0917", "Secret123!");
        foreach (var code in codes)
            collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, Guid.NewGuid(), code));
        return collector;
    }

    // Regression: previously the handler hardcoded collectorId: Guid.Empty / createdBy: "System",
    // so collector daily-collection stats were structurally always zero.
    [Fact]
    public async Task Handle_AttributesCollectionToAuthenticatedUser()
    {
        var collectorId = Guid.NewGuid();
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        currentUser.SetupGet(c => c.CollectorId).Returns(collectorId);
        currentUser.SetupGet(c => c.Username).Returns("collector1");
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        DailyCollection? captured = null;
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured = dc)
            .Returns(Task.CompletedTask);

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, DateOnly.FromDateTime(DateTime.UtcNow), IsPaid: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.True(captured!.IsPaid);
        Assert.Equal(collectorId, captured.CollectorId);
        Assert.Equal("collector1", captured.CreatedBy);
    }

    [Fact]
    public async Task Handle_PersistsOrNumberForPaidDailyCollection()
    {
        var collectorId = Guid.NewGuid();
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        currentUser.SetupGet(c => c.CollectorId).Returns(collectorId);
        currentUser.SetupGet(c => c.Username).Returns("collector1");
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        DailyCollection? captured = null;
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured = dc)
            .Returns(Task.CompletedTask);

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 6), true, ORNumber: "000001"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.True(captured!.IsPaid);
        Assert.Equal("000001", captured.ORNumber);
    }

    [Fact]
    public async Task Handle_CreatesUnpaidMarkerForSameDayRefresh()
    {
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        currentUser.SetupGet(c => c.Username).Returns("collector1");
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        DailyCollection? captured = null;
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured = dc)
            .Returns(Task.CompletedTask);

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 6), false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.False(captured!.IsPaid);
        Assert.Equal(new DateOnly(2026, 6, 6), captured.CollectionDate);
    }

    [Fact]
    public async Task Handle_NewPaidCollection_WithDuplicateOrNumber_ReturnsConflict()
    {
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        currentUser.SetupGet(c => c.Username).Returns("collector1");

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 6), true, ORNumber: "DUP-1"),
            CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
        dailyRepo.Verify(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingPaidCollection_ResavingSameOrNumber_IsAllowed()
    {
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);
        var existing = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 6), "prev");
        existing.MarkPaid(orNumber: "X-1", collectorId: null, fishKilos: null, updatedBy: "prev");

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        // OR "exists" globally only because it is on this same day's record.
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        currentUser.SetupGet(c => c.Username).Returns("collector1");

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 6), true, ORNumber: "X-1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(existing.IsPaid);
        Assert.Equal("X-1", existing.ORNumber);
    }

    [Fact]
    public async Task Collector_CannotOverwriteDailyCollectionRecordedByAnotherCollector_OnSharedFacility()
    {
        var firstCollectorId = Guid.NewGuid();
        var secondCollector = CollectorWith(FacilityCode.NPM);
        var stall = StallInFacility(FacilityCode.NPM);
        var collectionDate = new DateOnly(2026, 6, 6);
        var existing = DailyCollection.Create(stall.Id, collectionDate, "collector-a");
        existing.MarkPaid("OR-A", firstCollectorId, null, "collector-a");

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        stallRepo.Setup(r => r.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(stall.Id, collectionDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        collectorRepo.Setup(r => r.GetByIdAsync(secondCollector.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(secondCollector);
        currentUser.SetupGet(c => c.Role).Returns("Collector");
        currentUser.SetupGet(c => c.CollectorId).Returns(secondCollector.Id);
        currentUser.SetupGet(c => c.Username).Returns("collector-b");

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, collectionDate, IsPaid: true, ORNumber: "OR-B"),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(firstCollectorId, existing.CollectorId);
        Assert.Equal("OR-A", existing.ORNumber);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Collector_CanUpdateOwnDailyCollection_OnSharedFacility()
    {
        var collector = CollectorWith(FacilityCode.NPM);
        var stall = StallInFacility(FacilityCode.NPM);
        var collectionDate = new DateOnly(2026, 6, 6);
        var existing = DailyCollection.Create(stall.Id, collectionDate, "collector-a");
        existing.MarkPaid("OR-A", collector.Id, null, "collector-a");

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync("OR-A", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        stallRepo.Setup(r => r.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(stall.Id, collectionDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        collectorRepo.Setup(r => r.GetByIdAsync(collector.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(collector);
        currentUser.SetupGet(c => c.Role).Returns("Collector");
        currentUser.SetupGet(c => c.CollectorId).Returns(collector.Id);
        currentUser.SetupGet(c => c.Username).Returns("collector-a");
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, collectionDate, IsPaid: true, FishKilos: 2m, ORNumber: "OR-A"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(collector.Id, existing.CollectorId);
        Assert.Equal("OR-A", existing.ORNumber);
        Assert.Equal(2m, existing.FishKilos);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MarksNewDayAbsent_ZeroOwed_NotPaid()
    {
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyCollection?)null);
        currentUser.SetupGet(c => c.Username).Returns("collector1");
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        DailyCollection? captured = null;
        dailyRepo.Setup(r => r.AddAsync(It.IsAny<DailyCollection>(), It.IsAny<CancellationToken>()))
            .Callback<DailyCollection, CancellationToken>((dc, _) => captured = dc)
            .Returns(Task.CompletedTask);

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 6), IsPaid: false, IsAbsent: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.True(captured!.IsAbsent);
        Assert.False(captured.IsPaid);
        Assert.Equal(0m, captured.TotalCollected);
    }

    [Fact]
    public async Task Handle_MarkingAbsent_ClearsPriorPaidState()
    {
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);
        var existing = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 6), "prev");
        existing.MarkPaid(orNumber: "X-1", collectorId: Guid.NewGuid(), fishKilos: 3m, updatedBy: "prev");

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        currentUser.SetupGet(c => c.Username).Returns("collector1");
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 6), IsPaid: false, IsAbsent: true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(existing.IsAbsent);
        Assert.False(existing.IsPaid);
        Assert.Null(existing.ORNumber);
        Assert.Null(existing.FishKilos);
    }

    [Fact]
    public async Task Handle_AbsentThenNotCollected_ClearsAbsent_SoDayIsOwedAgain()
    {
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);
        var existing = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 6), "prev");
        existing.MarkAbsent("prev");   // currently excused → ₱0 owed for the day

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        currentUser.SetupGet(c => c.Username).Returns("collector1");
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        // Collector switches the absent day to "Not Collected".
        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 6), IsPaid: false, IsAbsent: false),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(existing.IsAbsent);   // excused cleared → the day is a normal collectable day again
        Assert.False(existing.IsPaid);     // and it is unpaid → the ₱30 is owed again
    }

    [Fact]
    public async Task Handle_AbsentThenCollected_MarksPaid_ClearsAbsent()
    {
        var stall = Stall.Create(Guid.NewGuid(), "A-1", 900m, ApplicableFees.DailyRental);
        var existing = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 6), "prev");
        existing.MarkAbsent("prev");

        var dailyRepo = new Mock<IDailyCollectionRepository>();
        var stallRepo = new Mock<IStallRepository>();
        var collectorRepo = new Mock<ICollectorRepository>();
        var currentUser = new Mock<ICurrentUserService>();
        var uow = new Mock<IUnitOfWork>();
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.IsORNumberUniqueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        stallRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        currentUser.SetupGet(c => c.Username).Returns("collector1");
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new RecordDailyCollectionCommandHandler(
            dailyRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, currentUser.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.FeeRateResolver, CacheTestDoubles.Tenant);

        var result = await handler.Handle(
            new RecordDailyCollectionCommand(stall.Id, new DateOnly(2026, 6, 6), IsPaid: true, ORNumber: "OR-9"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(existing.IsPaid);
        Assert.False(existing.IsAbsent);   // excused cleared → ₱30 collected
        Assert.Equal("OR-9", existing.ORNumber);
    }

    [Fact]
    public void Validator_RejectsPaidAndAbsentTogether()
    {
        var validator = new RecordDailyCollectionCommandValidator();
        var result = validator.Validate(
            new RecordDailyCollectionCommand(Guid.NewGuid(), PhilippineTime.Today, IsPaid: true, IsAbsent: true));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validator_AllowsAbsentForFutureDate_ScheduledExcused()
    {
        // Future "Absent" is now permitted — it records an admin-approved scheduled excused
        // absence (e.g. a planned closure). It is ₱0 owed and never counts as unpaid/missed.
        var validator = new RecordDailyCollectionCommandValidator();
        var future = PhilippineTime.Today.AddDays(3);
        var result = validator.Validate(
            new RecordDailyCollectionCommand(Guid.NewGuid(), future, IsPaid: false, IsAbsent: true));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_AllowsAbsentForToday()
    {
        var validator = new RecordDailyCollectionCommandValidator();
        var result = validator.Validate(
            new RecordDailyCollectionCommand(Guid.NewGuid(), PhilippineTime.Today, IsPaid: false, IsAbsent: true));
        Assert.True(result.IsValid);
    }
}
