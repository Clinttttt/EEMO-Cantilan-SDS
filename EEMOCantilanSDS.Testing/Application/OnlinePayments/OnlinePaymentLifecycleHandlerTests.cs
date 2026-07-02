using EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;
using EEMOCantilanSDS.Application.Command.OnlinePayments.IssueOrNumber;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.OnlinePayments;

public class InitiateOnlinePaymentCommandHandlerTests
{
    private static Stall StallInFacility(FacilityCode code, decimal monthlyRate = 2400m)
    {
        var stall = Stall.Create(Guid.NewGuid(), "4", monthlyRate, ApplicableFees.BaseRental);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!
            .SetValue(stall, Facility.Create(code, code.ToString(), code.ToString()));
        return stall;
    }

    private static InitiateOnlinePaymentCommandHandler Build(
        Stall stall, PaymentRecord? existingRecord, Guid? payorId, bool linked,
        Mock<IOnlinePaymentRepository>? onlineRepoOut = null,
        Mock<IPaymentRepository>? paymentRepoOut = null,
        Result<CheckoutSessionResult>? gatewayResult = null)
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

        var gateway = new Mock<IPaymentGateway>();
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
            gateway.Object, urlBuilder.Object, currentUser.Object, uow.Object);
    }

    [Fact]
    public async Task NotLinkedToPayor_IsForbidden()
    {
        var stall = StallInFacility(FacilityCode.TCC);
        var handler = Build(stall, existingRecord: null, Guid.NewGuid(), linked: false);

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.Equal(403, result.StatusCode);
    }

    [Fact]
    public async Task NpmFacility_IsBlocked()
    {
        var stall = StallInFacility(FacilityCode.NPM);
        var handler = Build(stall, existingRecord: null, Guid.NewGuid(), linked: true);

        var result = await handler.Handle(new InitiateOnlinePaymentCommand(stall.Id, 2026, 6), CancellationToken.None);

        Assert.Equal(409, result.StatusCode);
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

        Assert.Equal(409, result.StatusCode);
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

        Assert.Equal(502, result.StatusCode);
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
        OnlinePaymentTransaction txn, PaymentRecord record)
    {
        var onlineRepo = new Mock<IOnlinePaymentRepository>();
        onlineRepo.Setup(r => r.GetByIdAsync(txn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(txn);

        var paymentRepo = new Mock<IPaymentRepository>();
        paymentRepo.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.Username).Returns("admin");

        var notifier = new Mock<IPayorRealtimeNotifier>();

        var uow = new Mock<IUnitOfWork>();
        return (new IssueOnlinePaymentOrNumberCommandHandler(
            onlineRepo.Object, paymentRepo.Object, currentUser.Object, notifier.Object, uow.Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant), uow, notifier);
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

        Assert.Equal(409, result.StatusCode);
        Assert.Equal(OnlinePaymentStatus.Pending, txn.Status);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        notifier.Verify(n => n.NotifyOrIssuedAsync(It.IsAny<Guid>(), It.IsAny<PayorOrIssuedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
