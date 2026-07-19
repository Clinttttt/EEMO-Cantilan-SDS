using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The Online Payments treasury overview sums only RECEIVED online payments (Paid + OR-Completed), counts
/// them, reports the dominant method, and lists them with the tenant facility name resolved. Figures are
/// drawn from our own records so they reconcile to the treasury report (the PayMongo balance is not used).
/// </summary>
public class OnlinePaymentDashboardTests : RepositoryTestBase
{
    [Fact]
    public async Task Dashboard_SumsReceivedOnline_CountsThem_AndListsRecentWithFacilityName()
    {
        var context = NewContext();
        var today = PhilippineTime.Today;
        var day = Math.Min(15, DateTime.DaysInMonth(today.Year, today.Month));
        var (midMonthUtc, _) = PhilippineTime.DayUtcRange(new DateOnly(today.Year, today.Month, day));

        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(tcc.Id, "101", 2_400m, ApplicableFees.BaseRental);
        var record = PaymentRecord.Create(stall.Id, today.Year, today.Month, 2_400m);

        // Paid (money received, awaiting OR).
        var paid = OnlinePaymentTransaction.Create("EEMO-OP-1", Guid.NewGuid(), record.Id, 2_400m, "PayMongo");
        paid.SetPending("cs_1", "https://checkout/1");
        paid.MarkPaid("pay_1", "gcash", midMonthUtc, "{}");

        // Completed (received + OR encoded).
        var completed = OnlinePaymentTransaction.Create("EEMO-OP-2", Guid.NewGuid(), record.Id, 1_000m, "PayMongo");
        completed.SetPending("cs_2", "https://checkout/2");
        completed.MarkPaid("pay_2", "gcash", midMonthUtc, "{}");
        completed.CompleteWithOr("OR-2026-1", "admin");

        // Failed attempt — must NOT count toward collected money.
        var failed = OnlinePaymentTransaction.Create("EEMO-OP-3", Guid.NewGuid(), record.Id, 5_000m, "PayMongo");
        failed.SetPending("cs_3", "https://checkout/3");
        failed.MarkFailed("{}");

        context.AddRange(tcc, stall, record, paid, completed, failed);
        await context.SaveChangesAsync();

        var repo = new OnlinePaymentRepository(context);
        var dto = await repo.GetDashboardAsync(today.Year, today.Month, 25, CancellationToken.None);

        Assert.Equal(3_400m, dto.CollectedThisMonth);      // paid + completed only
        Assert.Equal(3_400m, dto.CollectedThisYear);
        Assert.Equal(2, dto.SettledCountThisYear);         // failed excluded
        Assert.Equal("gcash", dto.TopMethod);
        Assert.Equal(2, dto.Recent.Count);
        Assert.All(dto.Recent, r => Assert.Equal("Tampak Commercial Center", r.Facility));
        Assert.Contains(dto.Recent, r => r.Status == "Completed" && r.ORNumber == "OR-2026-1");
        Assert.Contains(dto.Recent, r => r.Status == "Awaiting OR");
    }
}
