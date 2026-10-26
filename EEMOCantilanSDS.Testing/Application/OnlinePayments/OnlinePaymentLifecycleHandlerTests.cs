using EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;
using EEMOCantilanSDS.Application.Command.OnlinePayments.IssueOrNumber;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Application.Dtos.Payors;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.OnlinePayments;

public class InitiateOnlinePaymentCommandHandlerTests
{
    private static Stall StallInFacility(FacilityCode code, decimal monthlyRate = 2400m,
        DateOnly? contractStart = null, int contractYears = 20, Guid municipalityId = default)
    {
        var facility = Facility.Create(code, code.ToString(), code.ToString(), municipalityId: municipalityId);
        var stall = Stall.Create(facility.Id, "4", monthlyRate, ApplicableFees.BaseRental,
            createdBy: "test", municipalityId: municipalityId);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, facility);
        // A contract so the requested period is covered (wide by default; narrow it for the out-of-term test).
        stall.Contracts.Add(Contract.Create(
            stall.Id, "Occupant", "Occupant", contractStart ?? new DateOnly(2020, 1, 1), contractYears, monthlyRate));
        return stall;
    }

    private static InitiateOnlinePaymentCommandHandler Build(
        Stall stall, PaymentRecord? existingRecord, Guid? payorId, bool linked,
        Mock<IOnlinePaymentRepository>? onlineRepoOut = null,
        Mock<IPaymentRepository>? paymentRepoOut = null,
        Result<CheckoutSessionResult>? gatewayResult = null,
        Mock<IPaymentGateway>? gatewayOut = null)
    {
        var onlineRepo = onlineRepoOut ?? new Mock<IOnlinePaymentRepository>();
        onlineRepo.Setup(r => r.ReferenceExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var paymentRepo = paymentRepoOut ?? new Mock<IPaymentRepository>();
        if (existingRecord is not null)
        {
            paymentRepo.Setup(r => r.GetPaymentRecordAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PaymentRecordDto(existingRecord.Id, existingRecord.Status, existingRecord.ORNumber,
                    existingRecord.BaseRentalAmount, existingRecord.ElecAmount, existingRecord.WaterAmount,
                    existingRecord.FishFeeAmount, existingRecord.AmountPaid, existingRecord.BalanceDue));
            paymentRepo.Setup(r => r.GetByIdAsync(existingRecord.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existingRecord);
        }
        else
        {
            paymentRepo.Setup(r => r.GetPaymentRecordAsync(stall.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((PaymentRecordDto?)null);
        }

        var stallRepo = new Mock<IStallRepository>();
        stallRepo.Setup(r => r.GetByIdAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);

        var payorRepo = new Mock<IPayorRepository>();
        payorRepo.Setup(r => r.LinkExistsAsync(It.IsAny<Guid>(), stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(linked);

        var gateway = gatewayOut ?? new Mock<IPaymentGateway>();
        gateway.SetupGet(g => g.Provider).Returns("PayMongo");
        gateway.Setup(g => g.CreateCheckoutSessionAsync(It.IsAny<CreateCheckoutSessionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(gatewayResult ?? Result<CheckoutSessionResult>.Success(new CheckoutSessionResult("https://pay", "cs_test", "PayMongo")));

        var urlBuilder = new Mock<IOnlinePaymentUrlBuilder>();
        urlBuilder.Setup(b => b.BuildSuccessUrl(It.IsAny<string>())).Returns("https://ok");
        urlBuilder.Setup(b => b.BuildCancelUrl(It.IsAny<string>())).Returns("https://cancel");

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(payorId);

        var uow = new Mock<IUnitOfWork>();

        return new InitiateOnlinePaymentCommandHandler(
            onlineRepo.Object, paymentRepo.Object, stallRepo.Object, payorRepo.Object,
            gateway.Object, urlBuilder.Object, currentUser.Object, uow.Object, new FixedClock(DateTime.UtcNow));
    }

    [Fact]
    public async Task NotLinkedToPayor_IsForbidden()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var handler = Build(stall, existingRecord: null, Guid.NewGuid(), linked: false);

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Theory]
    [InlineData(SettlementAuthority.PendingCutover)]
    [InlineData(SettlementAuthority.Canonical)]
    public async Task MonthlyRecord_DoesNotStartOnlineCheckoutAfterSettlementAuthorityChanges(
        SettlementAuthority authority)
    {
        var tenantId = Guid.NewGuid();
        var stall = StallInFacility(FacilityCode.TCC, municipalityId: tenantId);
        var record = PaymentRecord.Create(stall.Id, 2026, 6, 2400m, "test");
        if (authority == SettlementAuthority.PendingCutover)
            record.MarkSettlementPendingCutover();
        else
        {
            record.MarkSettlementPendingCutover();
            var boundary = DateTime.UtcNow;
            var cutover = CollectionSettlementCutover.Freeze(tenantId,
                CollectionSourceKind.PaymentRecord, record.Id, null, record.SettlementVersion,
                boundary, record.BaseRentalAmount, 0m, record.BaseRentalAmount,
                "{\"legacyWritersQuiesced\":true}", Guid.NewGuid(), boundary);
            record.ActivateCanonicalSettlement(cutover);
        }

        var gateway = new Mock<IPaymentGateway>();
        var onlineRepo = new Mock<IOnlinePaymentRepository>();
        var handler = Build(stall, record, Guid.NewGuid(), linked: true,
            onlineRepoOut: onlineRepo, gatewayOut: gateway);

        var result = await handler.Handle(
            new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        gateway.Verify(g => g.CreateCheckoutSessionAsync(It.IsAny<CreateCheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        onlineRepo.Verify(r => r.AddAsync(It.IsAny<OnlinePaymentTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // NEW online checkout is monthly Stall Rent only. A daily-billed Public Market is refused for EVERY kind - including the
    // default Monthly kind (the whole-month aggregate / a fish-section stall sending the default) - with a stable code, before
    // pricing, the gateway or persistence is touched.
    [Theory]
    [InlineData(PayorPayableKind.Monthly, null, null)]   // the default kind: how a whole-month or fish-section request arrives
    [InlineData(PayorPayableKind.NpmDaily, null, null)]  // whole-month rest-of-month rent
    [InlineData(PayorPayableKind.NpmUtility, null, null)]
    [InlineData(PayorPayableKind.NpmFish, 15, 54)]
    [InlineData(PayorPayableKind.NpmDaily, null, 3)]   // Days = a part-month daily fragment
    public async Task DailyBilledNpm_IsRefusedForEveryKind_WithAStableCode(PayorPayableKind kind, int? day, int? daysOrKilos)
    {
        var stall = StallInFacility(FacilityCode.NPM);
        var gateway = new Mock<IPaymentGateway>();
        var onlineRepo = new Mock<IOnlinePaymentRepository>();
        var handler = Build(stall, existingRecord: null, Guid.NewGuid(), linked: true, onlineRepoOut: onlineRepo, gatewayOut: gateway);

        var command = kind == PayorPayableKind.NpmFish
            ? new InitiateOnlinePaymentCommand(stall.Id, 2026, 6, kind, day, daysOrKilos)
            : new InitiateOnlinePaymentCommand(stall.Id, 2026, 6, kind, Days: daysOrKilos);
        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.True(result.ValidationErrors!.ContainsKey(InitiateOnlinePaymentCommandHandler.OutOfScopeCode));
        gateway.Verify(g => g.CreateCheckoutSessionAsync(It.IsAny<CreateCheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        onlineRepo.Verify(r => r.AddAsync(It.IsAny<OnlinePaymentTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // A non-NPM facility cannot be pushed into a non-rent kind either, and a monthly-rent Public Market keeps its checkout.
    [Theory]
    [InlineData(PayorPayableKind.NpmUtility)]
    [InlineData(PayorPayableKind.NpmFish)]
    [InlineData(PayorPayableKind.NpmDaily)]
    public async Task MonthlyFacility_IsRefusedForANonRentKind(PayorPayableKind kind)
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var handler = Build(stall, existingRecord: null, Guid.NewGuid(), linked: true);

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6, kind), CancellationToken.None);

        Assert.True(result.ValidationErrors!.ContainsKey(InitiateOnlinePaymentCommandHandler.OutOfScopeCode));
    }

    [Fact]
    public async Task SpaceRentalFacility_IsRefused()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(stall,
            Facility.Create(FacilityCode.Custom1, "Hall", "Hall", customCategory: CustomFacilityCategory.SpaceRental));
        var handler = Build(stall, existingRecord: null, Guid.NewGuid(), linked: true);

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.True(result.ValidationErrors!.ContainsKey(InitiateOnlinePaymentCommandHandler.OutOfScopeCode));
    }

    [Fact]
    public async Task MonthlyRentPublicMarket_StaysPayableOnline()
    {
        var stall = StallInFacility(FacilityCode.NPM);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(stall,
            Facility.Create(FacilityCode.NPM, "NPM", "NPM", archetype: BillingArchetype.MonthlyRental));
        var handler = Build(stall, existingRecord: null, Guid.NewGuid(), linked: true);

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task NpmFishDaysList_IsRefusedAsOutOfScope()
    {
        var stall = StallInFacility(FacilityCode.NPM);
        var handler = Build(stall, existingRecord: null, Guid.NewGuid(), linked: true);

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6, PayorPayableKind.NpmFish,
            FishDays: new[] { new FishDayDeclarationInput(14, 1m), new FishDayDeclarationInput(15, 2m) }), CancellationToken.None);

        Assert.True(result.ValidationErrors!.ContainsKey(InitiateOnlinePaymentCommandHandler.OutOfScopeCode));
    }

    [Fact]
    public async Task StallOfAnotherTenant_IsNotFound_AndNothingIsCharged()
    {
        // The stall repository is tenant-filtered, so a stall outside the caller's tenant resolves to null even if a link row existed.
        var stall = StallInFacility(FacilityCode.TCC);
        var gateway = new Mock<IPaymentGateway>();
        var stallRepo = new Mock<IStallRepository>();   // returns null for any id
        var other = new InitiateOnlinePaymentCommandHandler(
            new Mock<IOnlinePaymentRepository>().Object, new Mock<IPaymentRepository>().Object, stallRepo.Object,
            Mock.Of<IPayorRepository>(p => p.LinkExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()) == Task.FromResult(true)),
            gateway.Object, new Mock<IOnlinePaymentUrlBuilder>().Object,
            Mock.Of<ICurrentUserService>(c => c.UserId == Guid.NewGuid()),
            new Mock<IUnitOfWork>().Object, new FixedClock(DateTime.UtcNow));

        var result = await other.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        gateway.Verify(g => g.CreateCheckoutSessionAsync(It.IsAny<CreateCheckoutSessionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MonthlyRecord_FirstBilledOnline_UsesTheContractRateOfThatMonth_NotTheStallsCurrentRate()
    {
        // The office's own payment path bills a month at the rate of the term that answered for it. Online created the
        // record from the stall's current rate, so a rate set later for another lessee restated an older month.
        var stall = StallInFacility(FacilityCode.TCC, monthlyRate: 2400m);
        stall.UpdateRates(3000m, updatedBy: "test");

        PaymentRecord? added = null;
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.AddAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()))
            .Callback<PaymentRecord, CancellationToken>((r, _) => added = r).Returns(Task.CompletedTask);
        var handler = Build(stall, existingRecord: null, Guid.NewGuid(), linked: true, paymentRepoOut: paymentRepo);

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2400m, added!.BaseRentalAmount);
    }

    [Fact]
    public async Task PeriodOutsideContractTerm_IsRejected()
    {
        // #2 regression — the stall's contract covers only 2019; a 2026 period is outside its term, so a
        // payor must not be able to open a checkout (and create an obligation row) for it.
        var stall = StallInFacility(FacilityCode.TCC, contractStart: new DateOnly(2019, 1, 1), contractYears: 1);
        var handler = Build(stall, existingRecord: null, Guid.NewGuid(), linked: true);

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task NewCurrentMonthObligation_CreatesRecord_OpensCheckout_AndMarksPending()
    {
        var stall = StallInFacility(FacilityCode.TCC, 2400m);

        OnlinePaymentTransaction? capturedTxn = null;
        var onlineRepo = new Mock<IOnlinePaymentRepository>();
        onlineRepo.Setup(r => r.ReferenceExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        onlineRepo.Setup(r => r.AddAsync(It.IsAny<OnlinePaymentTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<OnlinePaymentTransaction, CancellationToken>((t, _) => capturedTxn = t).Returns(Task.CompletedTask);

        var paymentRepo = new Mock<IPaymentRepository>();

        var handler = Build(stall, existingRecord: null, Guid.NewGuid(), linked: true, onlineRepo, paymentRepo);

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://pay", result.Value!.CheckoutUrl);
        Assert.NotNull(capturedTxn);
        Assert.Equal(OnlinePaymentStatus.Pending, capturedTxn!.Status);
        Assert.Equal("cs_test", capturedTxn.GatewayReference);
        Assert.Equal(2400m, capturedTxn.Amount);
        // The current-month record did not exist and was created + persisted.
        paymentRepo.Verify(r => r.AddAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AlreadyPaidExistingRecord_Conflict()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var paid = PaymentRecord.Create(stall.Id, 2026, 6, 2400m);
        paid.UpdateStatus(PaymentStatus.Paid);

        var onlineRepo = new Mock<IOnlinePaymentRepository>();
        onlineRepo.Setup(r => r.ReferenceExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var handler = Build(stall, existingRecord: paid, Guid.NewGuid(), linked: true, onlineRepo);

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        onlineRepo.Verify(r => r.AddAsync(It.IsAny<OnlinePaymentTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExistingUnfinishedCheckout_ResumesSameSession_WithoutNewGatewaySession()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var record = PaymentRecord.Create(stall.Id, 2026, 6, 2400m); // unpaid, balance remains

        var resumable = OnlinePaymentTransaction.Create("EEMO-OP-20260613-RESUME01", Guid.NewGuid(), record.Id, 2400m, "PayMongo");
        resumable.SetPending("cs_existing", "https://resume.test/checkout");

        var onlineRepo = new Mock<IOnlinePaymentRepository>();
        onlineRepo.Setup(r => r.ReferenceExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        onlineRepo.Setup(r => r.GetResumableTransactionForRecordAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(resumable);

        var handler = Build(stall, existingRecord: record, Guid.NewGuid(), linked: true, onlineRepo);

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://resume.test/checkout", result.Value!.CheckoutUrl);
        // Resuming must NOT open a new gateway session / persist a new transaction.
        onlineRepo.Verify(r => r.AddAsync(It.IsAny<OnlinePaymentTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GatewayFailure_Returns502_AndDoesNotPersist()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var onlineRepo = new Mock<IOnlinePaymentRepository>();
        onlineRepo.Setup(r => r.ReferenceExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var paymentRepo = new Mock<IPaymentRepository>();

        var handler = Build(stall, existingRecord: null, Guid.NewGuid(), linked: true, onlineRepo, paymentRepo,
            gatewayResult: Result<CheckoutSessionResult>.Failure("gateway down", 502));

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.Equal(ResultStatus.UpstreamFailed, result.Status);
        onlineRepo.Verify(r => r.AddAsync(It.IsAny<OnlinePaymentTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
        paymentRepo.Verify(r => r.AddAsync(It.IsAny<PaymentRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class IssueOnlinePaymentOrNumberCommandHandlerTests
{
    private static (OnlinePaymentTransaction txn, PaymentRecord record) PaidPair()
    {
        var record = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, 2400m);
        record.MarkPaidOnline("Paid online via gcash · ref EEMO-OP-X");
        var txn = OnlinePaymentTransaction.Create("EEMO-OP-20260613-ABCD1234", Guid.NewGuid(), record.Id, 2400m, "PayMongo");
        txn.SetPending("cs_test", "https://checkout.test/cs");
        txn.MarkPaid("pay_1", "gcash", DateTime.UtcNow, "{}");
        return (txn, record);
    }

    private static (IssueOnlinePaymentOrNumberCommandHandler handler, Mock<IUnitOfWork> uow, Mock<IPayorRealtimeNotifier> notifier) Build(
        OnlinePaymentTransaction txn, PaymentRecord record,
        string? role = null, Guid? collectorId = null, Stall? stall = null, CollectorUser? collector = null)
    {
        var onlineRepo = new Mock<IOnlinePaymentRepository>();
        onlineRepo.Setup(r => r.GetByIdAsync(txn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(txn);

        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var stallRepo = new Mock<IStallRepository>();
        if (stall is not null)
            stallRepo.Setup(r => r.GetByIdAsync(record.StallId, It.IsAny<CancellationToken>())).ReturnsAsync(stall);

        var collectorRepo = new Mock<ICollectorRepository>();
        if (collector is not null && collectorId is { } cid)
            collectorRepo.Setup(r => r.GetByIdAsync(cid, It.IsAny<CancellationToken>())).ReturnsAsync(collector);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("admin");
        currentUser.SetupGet(c => c.Role).Returns(role);
        currentUser.SetupGet(c => c.CollectorId).Returns(collectorId);

        var notifier = new Mock<IPayorRealtimeNotifier>();

        var uow = new Mock<IUnitOfWork>();
        return (new IssueOnlinePaymentOrNumberCommandHandler(
            onlineRepo.Object, paymentRepo.Object, stallRepo.Object, collectorRepo.Object, new Mock<IDailyCollectionRepository>().Object, new Mock<IUtilityBillRepository>().Object, new Mock<IOrNumberRegistry>().Object, currentUser.Object, notifier.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant), uow, notifier);
    }

    [Fact]
    public async Task StaffEncodeOr_CompletesTransaction_AndMirrorsOntoRecord()
    {
        var (txn, record) = PaidPair();
        var (handler, uow, notifier) = Build(txn, record);

        var result = await handler.Handle(new IssueOnlinePaymentOrNumberCommand(txn.Id, "OR-2026-0601"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Completed, txn.Status);
        Assert.Equal("OR-2026-0601", txn.ORNumber);
        Assert.Equal("OR-2026-0601", record.ORNumber);   // mirrored onto the ledger record
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // The paying payor is alerted that their receipt is now official (with the OR).
        notifier.Verify(n => n.NotifyOrIssuedAsync(
            txn.PayorUserId,
            It.Is<PayorOrIssuedNotification>(p => p.OrNumber == "OR-2026-0601" && p.StallId == record.StallId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EncodeOr_OnNonPaidTransaction_IsConflict()
    {
        var record = PaymentRecord.Create(Guid.NewGuid(), 2026, 6, 2400m);
        var txn = OnlinePaymentTransaction.Create("EEMO-OP-20260613-ABCD1234", Guid.NewGuid(), record.Id, 2400m, "PayMongo");
        txn.SetPending("cs_test", "https://checkout.test/cs"); // still Pending, not Paid
        var (handler, uow, notifier) = Build(txn, record);

        var result = await handler.Handle(new IssueOnlinePaymentOrNumberCommand(txn.Id, "OR-1"), CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Equal(OnlinePaymentStatus.Pending, txn.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        notifier.Verify(n => n.NotifyOrIssuedAsync(It.IsAny<Guid>(), It.IsAny<PayorOrIssuedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Collector_NotAssignedToFacility_IsForbidden()
    {
        // #5 regression — a collector may only receipt an online payment for a facility they're assigned to.
        var (txn, record) = PaidPair();
        var stall = Stall.Create(Guid.NewGuid(), "4", 2400m, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(FacilityCode.TCC, "TCC", "TCC"));
        var collectorId = Guid.NewGuid();
        var collector = CollectorUser.Create("Col", "EMP-1", "col", "c@x.gov", "0917", TestPasswords.Hash("Passw0rd!")); // no assignments

        var (handler, uow, _) = Build(txn, record, role: "Collector", collectorId: collectorId, stall: stall, collector: collector);

        var result = await handler.Handle(new IssueOnlinePaymentOrNumberCommand(txn.Id, "OR-X"), CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.NotEqual(OnlinePaymentStatus.Completed, txn.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task NpmFishDay_StaffEncodesOr_StampsThatDay_AndCompletes()
    {
        var stall = Stall.Create(Guid.NewGuid(), "7", 900m, ApplicableFees.DailyRental, section: MarketSection.FishSection);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(FacilityCode.NPM, "NPM", "NPM"));
        var txn = OnlinePaymentTransaction.CreateForNpmFishDay("EEMO-OP-FISH", Guid.NewGuid(), stall.Id, 2026, 6, 15, 54m, 84m, "PayMongo");
        txn.SetPending("cs_fish", "https://checkout");
        txn.MarkPaid("pay_f", "qrph", DateTime.UtcNow, "{}");

        // That day was settled online: paid, blank OR.
        var dc = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 15));
        dc.MarkPaid(orNumber: string.Empty, collectorId: null, fishKilos: 54m);

        var onlineRepo = new Mock<IOnlinePaymentRepository>();
        onlineRepo.Setup(r => r.GetByIdAsync(txn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(txn);
        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.IsDailyCollectionOrAvailableForStallAsync("OR-FISH-1", stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var dailyRepo = new Mock<IDailyCollectionRepository>();
        dailyRepo.Setup(r => r.GetByStallAndDateAsync(stall.Id, new DateOnly(2026, 6, 15), It.IsAny<CancellationToken>())).ReturnsAsync(dc);
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("admin");
        currentUser.SetupGet(c => c.Role).Returns((string?)null);
        var notifier = new Mock<IPayorRealtimeNotifier>();
        var uow = new Mock<IUnitOfWork>();

        var handler = new IssueOnlinePaymentOrNumberCommandHandler(
            onlineRepo.Object, paymentRepo.Object, new Mock<IStallRepository>().Object, new Mock<ICollectorRepository>().Object,
            dailyRepo.Object, new Mock<IUtilityBillRepository>().Object, new Mock<IOrNumberRegistry>().Object, currentUser.Object, notifier.Object, uow.Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        var result = await handler.Handle(new IssueOnlinePaymentOrNumberCommand(txn.Id, "OR-FISH-1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OnlinePaymentStatus.Completed, txn.Status);
        Assert.Equal("OR-FISH-1", dc.ORNumber);          // stamped on that one settled day
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifier.Verify(n => n.NotifyOrIssuedAsync(
            txn.PayorUserId,
            It.Is<PayorOrIssuedNotification>(p => p.OrNumber == "OR-FISH-1" && p.StallId == stall.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
