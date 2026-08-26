using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing.Infrastructure.Repositories;

/// <summary>
/// The days behind a daily-billed month.
///
/// <para>
/// A market month is folded into ONE ledger row because that is how the office reconciles it: an obligation for the month
/// against the installments received. The payor, though, pays a day at a time, and the folded row left them a total they
/// could not break down, with no sight of the days a collector took in the field. The row keeps its shape; it now carries
/// the days it is made of.
/// </para>
/// </summary>
public class PaymentHistoryDayBreakdownTests : RepositoryTestBase
{
    [Fact]
    public async Task AMarketMonthCarriesTheDaysItIsMadeOf()
    {
        await using var ctx = NewContext();
        var today = PhilippineTime.Today;
        var collector = CollectorUserFixture();

        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        var contract = Contract.Create(stall.Id, "Kim Chui", "Kim Chui", today.AddMonths(-1), 3, 900m);

        var first = new DateOnly(today.Year, today.Month, 1);
        var day1 = DailyCollection.Create(stall.Id, first, dailyFee: 30m);
        day1.MarkPaid("995656", collector.Id);
        var day2 = DailyCollection.Create(stall.Id, first.AddDays(1), dailyFee: 30m);
        day2.MarkPaid("", collector.Id);            // taken in the field, receipt not yet encoded

        ctx.AddRange(npm, stall, contract, collector, day1, day2);
        await ctx.SaveChangesAsync();

        var history = await NewRepository(ctx).GetPaymentHistoryAsync(stall.Id, CancellationToken.None);
        var month = Assert.Single(history, h => h.Period == $"{today.Year:0000}-{today.Month:00}");

        Assert.Equal(60m, month.AmountPaid);
        Assert.NotNull(month.Days);
        Assert.Equal(2, month.Days!.Count);

        // Earliest first, each with its own fee, and the receipt only where there is one to state.
        Assert.Equal(first, month.Days[0].Day);
        Assert.Equal(30m, month.Days[0].Amount);
        Assert.Equal("995656", month.Days[0].ORNumber);
        Assert.Equal("Juan Dels", month.Days[0].RecordedByName);
        Assert.Null(month.Days[1].ORNumber);
    }

    [Fact]
    public async Task AMonthlyFacilityCarriesNoDays()
    {
        // The other facilities are billed by the month, and a month is all there is to show.
        await using var ctx = NewContext();
        var today = PhilippineTime.Today;

        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(tcc.Id, "B-1", 2400m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Juan Cruz", "Juan Cruz", today.AddMonths(-1), 3, 2400m);
        var payment = PaymentRecord.Create(stall.Id, today.Year, today.Month, 2400m);
        payment.RecordPayment("OR-1", Guid.NewGuid(), PaymentStatus.Paid);

        ctx.AddRange(tcc, stall, contract, payment);
        await ctx.SaveChangesAsync();

        var history = await NewRepository(ctx).GetPaymentHistoryAsync(stall.Id, CancellationToken.None);
        var month = Assert.Single(history, h => h.Period == $"{today.Year:0000}-{today.Month:00}");

        Assert.True(month.Days is null || month.Days.Count == 0);
    }

    private static Domain.Entities.Users.CollectorUser CollectorUserFixture() =>
        Domain.Entities.Users.CollectorUser.Create(
            "Juan Dels", "EEMO-2026-001", "juan.dels", null, null, TestPasswords.Hash("Secret123!"));

    private static PaymentRepository NewRepository(AppDbContext ctx) => new(ctx);
}
