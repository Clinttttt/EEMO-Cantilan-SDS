using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// What the Record-payment dialog offers on a stall that has been re-let.
///
/// <para>The defect these pin down: the dialog resolved "the stall's active contract", so opening it from a former
/// lessee's ₱32,910 balance listed the SITTING lessee's current month at ₱60 — a figure that bore no relation to the
/// row it was opened from, and which, if recorded, would have settled the sitting lessee's days under the former
/// lessee's name. Naming the term makes each lessee's arrears their own.</para>
/// </summary>
public class OutstandingMonthsPerOccupancyTests : RepositoryTestBase
{
    private const decimal Daily = FeeRates.NpmDailyFee;

    private static Contract Term(Guid stallId, string occupant, DateOnly from, int years, decimal rate = 1_000m) =>
        Contract.Create(stallId, occupant, occupant, from, years, rate);

    /// <summary>A monthly-rental stall handed from one lessee to the next, both leaving months unpaid.</summary>
    private static async Task<(Stall stall, Contract outgoing, Contract incoming)> ReLetStallAsync(DbContext context)
    {
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "3", 2_500m, ApplicableFees.BaseRental);

        var today = PhilippineTime.Today;
        var handover = new DateOnly(today.Year, today.Month, 1).AddMonths(-2);

        // The outgoing lessee ran for two years at ₱1,000 and was handed over; the new one pays ₱2,500.
        var outgoing = Term(stall.Id, "Wilma K. Tecson", handover.AddYears(-2), 3, 1_000m);
        outgoing.Terminate("Head", handover.AddDays(-1));
        var incoming = Term(stall.Id, "Teofila Reyes", handover, 3, 2_500m);

        context.AddRange(facility, stall, outgoing, incoming);
        await context.SaveChangesAsync();

        return (stall, outgoing, incoming);
    }

    [Fact]
    public async Task NamingTheEndedTerm_StatesThatLesseesOwnMonths()
    {
        var context = NewContext();
        var (stall, outgoing, incoming) = await ReLetStallAsync(context);
        var repo = new PaymentRepository(context);

        var theirs = await repo.GetOutstandingMonthsAsync(stall.Id, outgoing.Id, null, CancellationToken.None);
        var sitting = await repo.GetOutstandingMonthsAsync(stall.Id, incoming.Id, null, CancellationToken.None);

        Assert.NotEmpty(theirs);
        Assert.NotEmpty(sitting);

        // Not one month in common: the windows meet, they never overlap.
        Assert.Empty(theirs.Select(m => m.Period).Intersect(sitting.Select(m => m.Period)));

        // Each is billed at the rent THEY agreed to, not at whatever the stall's rate happens to be now.
        Assert.All(theirs, m => Assert.Equal(1_000m, m.BalanceDue));
        Assert.All(sitting, m => Assert.Equal(2_500m, m.BalanceDue));
    }

    [Fact]
    public async Task NamingNoTerm_StillMeansTheSittingLessee()
    {
        // Every collection screen that says only "this stall" must keep behaving exactly as before.
        var context = NewContext();
        var (stall, _, incoming) = await ReLetStallAsync(context);
        var repo = new PaymentRepository(context);

        var byDefault = await repo.GetOutstandingMonthsAsync(stall.Id, null, null, CancellationToken.None);
        var named = await repo.GetOutstandingMonthsAsync(stall.Id, incoming.Id, null, CancellationToken.None);

        Assert.Equal(named.Select(m => m.Period), byDefault.Select(m => m.Period));
    }

    [Fact]
    public async Task ALapsedTermIsStillTheStallsAccount_WhenNoTermIsNamed()
    {
        // A "Contract expired" row: the term ran out and nobody replaced the lessee. That account is what the
        // office collects on, so the default must not fall silent just because the term is no longer current.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "9", 1_000m, ApplicableFees.BaseRental);
        var lapsed = Term(stall.Id, "Ana Reyes", PhilippineTime.Today.AddYears(-2), 1, 1_000m);

        context.AddRange(facility, stall, lapsed);
        await context.SaveChangesAsync();

        var months = await new PaymentRepository(context).GetOutstandingMonthsAsync(stall.Id, null, null, CancellationToken.None);

        Assert.NotEmpty(months);
        // Charges stop when the term lapsed — the lessee owes nothing for the year that followed.
        Assert.All(months, m => Assert.True(string.Compare(m.Period, $"{lapsed.ExpiryDate.Year:0000}-{lapsed.ExpiryDate.Month:00}", StringComparison.Ordinal) <= 0));
    }

    [Fact]
    public async Task OnADailyStall_AHandoverMonthIsSplitByDay_NotDoubleCharged()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.BaseRental, section: MarketSection.VegetableArea, dailyRate: Daily);

        // Handover on the 8th of last month: days 1–7 are the outgoing lessee's, 8th onward the incoming one's.
        var lastMonth = new DateOnly(PhilippineTime.Today.Year, PhilippineTime.Today.Month, 1).AddMonths(-1);
        var handover = lastMonth.AddDays(7);

        var outgoing = Term(stall.Id, "Merlita A. Abuso", lastMonth.AddYears(-1), 3, 900m);
        outgoing.Terminate("Head", handover.AddDays(-1));
        var incoming = Term(stall.Id, "Rosalinda G. Huelma", handover, 3, 900m);

        context.AddRange(facility, stall, outgoing, incoming);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var theirs = await repo.GetOutstandingMonthsAsync(stall.Id, outgoing.Id, null, CancellationToken.None);
        var sitting = await repo.GetOutstandingMonthsAsync(stall.Id, incoming.Id, null, CancellationToken.None);

        var period = $"{lastMonth.Year:0000}-{lastMonth.Month:00}";
        var theirShare = theirs.Single(m => m.Period == period);
        var sittingShare = sitting.Single(m => m.Period == period);

        // Seven days for the outgoing lessee; the rest of the month for the incoming one. Between them they owe the
        // month exactly once — the old reading charged the whole month to whoever the stall's contract pointed at.
        Assert.Equal(7 * Daily, theirShare.BalanceDue);
        Assert.Equal((DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month) - 7) * Daily, sittingShare.BalanceDue);
    }

    [Fact]
    public async Task ThePeriodBeingViewed_DecidesWhoseArrearsAreStated()
    {
        // Reported from the 2025 history: a December 2025 row showed ₱930, and the payment dialog offered July and
        // August 2026 totalling ₱90 — the months of the lessee sitting there NOW. Naming the period being looked at
        // makes the answer the lessee who actually held the stall then.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "1", 2_500m, ApplicableFees.BaseRental);

        var handover = new DateOnly(2026, 6, 8);
        var outgoing = Term(stall.Id, "Merlita A. Abuso", new DateOnly(2023, 6, 1), 3, 1_000m);
        outgoing.Terminate("Head", handover.AddDays(-1));
        var incoming = Term(stall.Id, "New Occupant", handover, 3, 2_500m);

        context.AddRange(facility, stall, outgoing, incoming);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);

        var forDecember2025 = await repo.GetOutstandingMonthsAsync(
            stall.Id, null, new DateOnly(2025, 12, 1), CancellationToken.None);

        // December 2025 is in the list, at the departed lessee's own rate, and none of the sitting lessee's months are.
        var december = Assert.Single(forDecember2025, m => m.Period == "2025-12");
        Assert.Equal(1_000m, december.BalanceDue);
        Assert.DoesNotContain(forDecember2025, m => string.Compare(m.Period, "2026-06", StringComparison.Ordinal) > 0);

        // Asking about a current period still answers with the sitting lessee.
        var forToday = await repo.GetOutstandingMonthsAsync(
            stall.Id, null, new DateOnly(PhilippineTime.Today.Year, PhilippineTime.Today.Month, 1), CancellationToken.None);
        Assert.All(forToday, m => Assert.True(string.Compare(m.Period, "2026-06", StringComparison.Ordinal) >= 0));
    }

    [Fact]
    public async Task MoneyAlreadyCollected_IsCreditedToTheDaysOwnOccupancy()
    {
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.BaseRental, section: MarketSection.FishSection, dailyRate: Daily);

        var lastMonth = new DateOnly(PhilippineTime.Today.Year, PhilippineTime.Today.Month, 1).AddMonths(-1);
        var handover = lastMonth.AddDays(7);

        var outgoing = Term(stall.Id, "Merlita A. Abuso", lastMonth.AddYears(-1), 3, 900m);
        outgoing.Terminate("Head", handover.AddDays(-1));
        var incoming = Term(stall.Id, "Rosalinda G. Huelma", handover, 3, 900m);

        // Two days paid inside the OUTGOING lessee's window.
        var paidDays = new[] { lastMonth, lastMonth.AddDays(1) }.Select(d =>
        {
            var dc = DailyCollection.Create(stall.Id, d, "Head", Daily);
            dc.MarkPaid($"OR-{d:yyyyMMdd}", Guid.NewGuid());
            return dc;
        }).ToArray();

        context.AddRange(facility, stall, outgoing, incoming);
        context.AddRange(paidDays);
        await context.SaveChangesAsync();

        var repo = new PaymentRepository(context);
        var period = $"{lastMonth.Year:0000}-{lastMonth.Month:00}";

        var theirShare = (await repo.GetOutstandingMonthsAsync(stall.Id, outgoing.Id, null, CancellationToken.None))
            .Single(m => m.Period == period);
        var sittingShare = (await repo.GetOutstandingMonthsAsync(stall.Id, incoming.Id, null, CancellationToken.None))
            .Single(m => m.Period == period);

        // Their two paid days reduce THEIR balance and leave the sitting lessee's untouched.
        Assert.Equal(5 * Daily, theirShare.BalanceDue);
        Assert.Equal((DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month) - 7) * Daily, sittingShare.BalanceDue);
    }
}
