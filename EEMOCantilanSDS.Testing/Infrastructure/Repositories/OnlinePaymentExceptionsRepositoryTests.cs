using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories;

/// <summary>The office's work list: what it holds, what it leaves out, and the order someone should act on it.</summary>
public class OnlinePaymentExceptionsRepositoryTests : RepositoryTestBase
{
    private readonly PayorUser _payor = PayorUser.Create("Angelica Reyes", "09170000000", TestPasswords.Hash("Passw0rd!"));

    private OnlinePaymentTransaction Txn(Guid recordId, string reference)
    {
        var txn = OnlinePaymentTransaction.Create(reference, _payor.Id, recordId, 100m, "PayMongo");
        txn.SetPending("cs_" + reference, "https://checkout");
        return txn;
    }

    private static void Age(OnlinePaymentTransaction txn, TimeSpan by) =>
        typeof(OnlinePaymentTransaction).GetProperty(nameof(OnlinePaymentTransaction.CreatedAt))!.SetValue(txn, DateTime.UtcNow - by);

    [Fact]
    public async Task ListsWhatNeedsSomeone_InTheOrderToActOnIt_AndLeavesOutWhatDoesNot()
    {
        await using var ctx = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Town Center", "TCC");
        var stall = Stall.Create(facility.Id, "7", 2400m, ApplicableFees.BaseRental);
        var record = PaymentRecord.Create(stall.Id, 2026, 6, 100m, "test");
        var retried = PaymentRecord.Create(stall.Id, 2026, 7, 100m, "test");

        var held = Txn(record.Id, "HELD");
        held.MarkPaid("pay_h", "gcash", DateTime.UtcNow, "{}");
        held.MarkSettlementReconciliationRequired("The gateway captured 90.00.", capturedAmount: 90m);

        var decided = Txn(record.Id, "DECIDED");
        decided.MarkPaid("pay_d", "gcash", DateTime.UtcNow, "{}");
        decided.MarkSettlementReconciliationRequired("duplicate");
        decided.RecordOfficeDecision(OnlinePaymentDisposition.Refunded, "ref_1", null, "head", DateTime.UtcNow);

        var decidedLongAgo = Txn(record.Id, "OLDDECISION");
        decidedLongAgo.MarkPaid("pay_o", "gcash", DateTime.UtcNow, "{}");
        decidedLongAgo.MarkSettlementReconciliationRequired("duplicate");
        decidedLongAgo.RecordOfficeDecision(OnlinePaymentDisposition.Refunded, "ref_0", null, "head", DateTime.UtcNow.AddDays(-45));

        var stale = Txn(record.Id, "STALE");
        Age(stale, TimeSpan.FromHours(26));
        var fresh = Txn(record.Id, "FRESH");
        // A payor who left a checkout open is not work for the office. The reconciler closes it after a day.
        var leftOpen = Txn(record.Id, "LEFTOPEN");
        Age(leftOpen, TimeSpan.FromHours(3));

        var failed = Txn(record.Id, "FAILED");
        failed.MarkFailed("{}");
        var oldFailure = Txn(record.Id, "OLDFAILED");
        oldFailure.MarkFailed("{}");
        Age(oldFailure, TimeSpan.FromDays(45));

        // Failed, then paid on a later attempt: nobody needs to look at the failure any more.
        var failedThenPaid = Txn(retried.Id, "RETRIED");
        failedThenPaid.MarkFailed("{}");
        var paid = Txn(retried.Id, "PAID");
        paid.MarkPaid("pay_p", "gcash", DateTime.UtcNow, "{}");

        ctx.AddRange(facility, stall, record, retried, held, decided, decidedLongAgo, stale, fresh, leftOpen, failed, oldFailure, failedThenPaid, paid, _payor);
        await ctx.SaveChangesAsync();

        var rows = await new OnlinePaymentRepository(ctx).GetExceptionsAsync(DateTime.UtcNow);

        Assert.Equal(["HELD", "STALE", "FAILED", "DECIDED"], rows.Select(r => r.Reference));
        Assert.Equal(
            [OnlinePaymentExceptionKind.NeedsDecision, OnlinePaymentExceptionKind.Unconfirmed, OnlinePaymentExceptionKind.Failed, OnlinePaymentExceptionKind.Resolved],
            rows.Select(r => r.Kind));

        var first = rows[0];
        Assert.Equal(90m, first.CapturedAmount);
        Assert.Equal(100m, first.Amount);
        Assert.Equal("The gateway captured 90.00.", first.Reason);
        Assert.Equal("Town Center", first.Facility);
        Assert.Equal("7", first.StallNo);
        Assert.Equal("2026-06", first.Period);
        Assert.Equal("Angelica Reyes", first.PayorName);
        Assert.Equal("ref_1", rows[3].DispositionReference);
    }

    [Fact]
    public async Task AHeldCaptureWhoseRecordIsGone_IsStillListed()
    {
        await using var ctx = NewContext();
        var orphan = Txn(Guid.NewGuid(), "ORPHAN");
        orphan.MarkPaid("pay_x", "gcash", DateTime.UtcNow, "{}");
        orphan.MarkSettlementReconciliationRequired("The linked monthly record was not found.");
        ctx.Add(orphan);
        await ctx.SaveChangesAsync();

        var row = Assert.Single(await new OnlinePaymentRepository(ctx).GetExceptionsAsync(DateTime.UtcNow));

        Assert.Equal("ORPHAN", row.Reference);
        Assert.Equal("—", row.Facility);
        Assert.Equal("—", row.Period);
        Assert.Equal("—", row.PayorName);
    }

    [Fact]
    public async Task ADecisionReleasesThePeriodAndTheMerchantGuard()
    {
        await using var ctx = NewContext();
        var recordId = Guid.NewGuid();
        var held = Txn(recordId, "HELD");
        held.MarkPaid("pay_h", "gcash", DateTime.UtcNow, "{}");
        held.MarkSettlementReconciliationRequired("duplicate");
        ctx.Add(held);
        await ctx.SaveChangesAsync();
        var repo = new OnlinePaymentRepository(ctx);

        Assert.True(await repo.HasUnresolvedCaptureForRecordAsync(recordId));
        Assert.True(await repo.HasCapturedReconciliationAsync(held.MunicipalityId));

        held.RecordOfficeDecision(OnlinePaymentDisposition.Applied, "OR-1", null, "head", DateTime.UtcNow);
        await ctx.SaveChangesAsync();

        Assert.False(await repo.HasUnresolvedCaptureForRecordAsync(recordId));
        Assert.False(await repo.HasCapturedReconciliationAsync(held.MunicipalityId));
    }
}
