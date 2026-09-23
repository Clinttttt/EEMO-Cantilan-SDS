using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

public class CollectorRepositoryTests : RepositoryTestBase
{
    /// <summary>
    /// "Last active" means the last time the collector actually did something.
    /// </summary>
    /// <remarks>
    /// It used to be <c>LastActiveAt</c> alone, which one method writes — RecordLogin. The mobile app holds its token and an
    /// entry can be recorded against a collector without a sign-in at all, so the office watched a collector record a daily
    /// collection on 16 September while the column read "Sep 12", his last sign-in.
    /// </remarks>
    [Fact]
    public async Task LastActive_IsTheLastThingRecorded_NotTheLastSignIn()
    {
        await using var ctx = NewContext();

        var collector = CollectorUser.Create("Juan Dels", "EEMO-2026-001", "juan", "juan@x.com", "0917", TestPasswords.Hash("pw"));
        collector.RecordLogin();                                          // signs in now
        var signedIn = collector.LastActiveAt!.Value;

        // ...then records a collection four days later, without signing in again.
        var recordedAt = signedIn.AddDays(4);
        var daily = DailyCollection.Create(Guid.NewGuid(), DateOnly.FromDateTime(recordedAt));
        daily.MarkPaid(orNumber: "", collectorId: collector.Id);
        daily.CreatedAt = recordedAt;
        daily.UpdatedAt = recordedAt;

        ctx.Add(collector);
        ctx.Add(daily);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);

        var listed = Assert.Single(await repo.GetAllCollectorsWithStatsAsync(recordedAt.Year, recordedAt.Month));
        Assert.Equal(recordedAt, listed.LastActiveAt);

        // The detail view answers the same, through the same rule.
        var detail = await repo.GetCollectorActivityAsync(collector.Id, recordedAt.Year, recordedAt.Month);
        Assert.Equal(recordedAt, detail!.LastActiveAt);
    }

    /// <summary>
    /// And it is not scoped to the month a screen is showing: a collector who last worked in August was last active in
    /// August, not never.
    /// </summary>
    [Fact]
    public async Task LastActive_ReachesBackBeforeTheMonthOnScreen()
    {
        await using var ctx = NewContext();

        var collector = CollectorUser.Create("Personal Dose", "EEMO-2026-002", "pd", "pd@x.com", "0918", TestPasswords.Hash("pw"));
        var august = new DateTime(2026, 8, 20, 6, 0, 0, DateTimeKind.Utc);

        var slaughter = SlaughterTransaction.CreateHog(
            facilityId: Guid.NewGuid(),
            collectorId: collector.Id,
            ownerName: "Dela Cruz, Ramon",
            heads: 1,
            orNumber: "OR-1",
            transactionDate: new DateOnly(2026, 8, 20),
            ratePerHead: 250m);
        slaughter.CreatedAt = august;

        ctx.Add(collector);
        ctx.Add(slaughter);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);

        // Reading September: the month's takings are nought, and the last activity is still August's.
        var listed = Assert.Single(await repo.GetAllCollectorsWithStatsAsync(2026, 9));
        Assert.Equal(0m, listed.CollectedThisMonth);
        Assert.Equal(august, listed.LastActiveAt);
    }

    // Regression for P1 (daily collections keyed by CollectorId, not CreatedBy) and
    // P3 (a Partial payment is counted at PartialAmount, not the full bill).
    //
    // The recorded moment is set deliberately, because this figure is counted on WHEN THE MONEY WAS TAKEN — the same basis as the
    // collector's Report of Collections. It used to be counted on the period the fee was FOR, which is why the two screens
    // disagreed whenever arrears were settled; the office ruled on the cash basis on 2026-09-10.
    [Fact]
    public async Task GetAllCollectorsWithStats_CountsPartialAtPartialAmount_AndDailyByCollectorId()
    {
        await using var ctx = NewContext();

        var collector = CollectorUser.Create("Juan Dela Cruz", "EEMO-2026-001", "juan", "juan@x.com", "0917", TestPasswords.Hash("pw"));
        var stallId = Guid.NewGuid();
        var takenAt = new DateTime(2026, 1, 15, 3, 0, 0, DateTimeKind.Utc);   // 11:00 on 15 January, Philippine time

        var payment = PaymentRecord.Create(stallId, 2026, 1, baseRental: 900m);
        payment.UpdateStatus(PaymentStatus.Partial, partialAmount: 300m, remarks: null, updatedBy: "t", collectorId: collector.Id);
        payment.BackdateReceipt(takenAt);

        var daily = DailyCollection.Create(stallId, new DateOnly(2026, 1, 15));
        daily.MarkPaid(orNumber: "", collectorId: collector.Id);
        daily.CreatedAt = takenAt;
        daily.UpdatedAt = takenAt;

        ctx.Add(collector);
        ctx.Add(payment);
        ctx.Add(daily);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var stats = await repo.GetAllCollectorsWithStatsAsync(2026, 1);

        var dto = Assert.Single(stats);
        Assert.Equal(330m, dto.CollectedThisMonth); // 300 (partial) + 30 (daily fee) — NOT 900
        Assert.Equal(2, dto.Transactions);          // 1 payment + 1 daily collection
    }

    /// <summary>
    /// An owed day settled later belongs to the month the MONEY came in, matching the Report of Collections.
    /// </summary>
    /// <remarks>
    /// This is the case the two screens disagreed on, and the case the other tests here could not see: they seed a fee whose own day
    /// and whose recorded moment fall in the same month, so either basis gives the same answer. A December day collected in January
    /// separates them.
    ///
    /// <para>The office ruled on 2026-09-10 that this figure is CASH — what the collector handled and must remit — and that a period
    /// flattered by arrears is disclosed on the report, which states how much of its total answered for earlier periods. Asserted from
    /// BOTH months, because a basis that merely moved the money would satisfy one assertion and fail the other.</para>
    /// </remarks>
    [Fact]
    public async Task GetAllCollectorsWithStats_CountsAnOwedDayInTheMonthItWasCollected()
    {
        await using var ctx = NewContext();

        var collector = CollectorUser.Create("Juan", "EEMO-2026-001", "juan", "juan@x.com", "0917", TestPasswords.Hash("pw"));

        // A 29 December day, collected on 5 January.
        var owed = DailyCollection.Create(Guid.NewGuid(), new DateOnly(2025, 12, 29));
        owed.MarkPaid(orNumber: "1441911", collectorId: collector.Id);
        owed.CreatedAt = new DateTime(2025, 12, 29, 3, 0, 0, DateTimeKind.Utc);
        owed.UpdatedAt = new DateTime(2026, 1, 5, 3, 0, 0, DateTimeKind.Utc);

        ctx.Add(collector);
        ctx.Add(owed);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);

        var january = Assert.Single(await repo.GetAllCollectorsWithStatsAsync(2026, 1));
        Assert.Equal(30m, january.CollectedThisMonth);
        Assert.Equal(1, january.Transactions);

        var december = Assert.Single(await repo.GetAllCollectorsWithStatsAsync(2025, 12));
        Assert.Equal(0m, december.CollectedThisMonth);
        Assert.Equal(0, december.Transactions);
    }

    [Fact]
    public async Task CollectorListAndActivity_CountAdjustedDailyFeeOnce_AndKeepFishSeparate()
    {
        await using var ctx = NewContext();
        var today = PhilippineTime.Today;

        var collector = CollectorUser.Create("NPM Collector", "EEMO-2026-010", "npm-collector", "npm@x.com", "0917", TestPasswords.Hash("pw"));
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var normalStall = Stall.Create(facility.Id, "N-1", 900m, ApplicableFees.DailyRental | ApplicableFees.FishFee, section: MarketSection.FishSection);
        var adjustedStall = Stall.Create(facility.Id, "N-2", 900m, ApplicableFees.DailyRental | ApplicableFees.FishFee, section: MarketSection.FishSection);
        var normalContract = Contract.Create(normalStall.Id, "Normal", "Normal", new DateOnly(2020, 1, 1), 20, 900m);
        var adjustedContract = Contract.Create(adjustedStall.Id, "Adjusted", "Adjusted", new DateOnly(2020, 1, 1), 20, 900m);

        var normal = DailyCollection.Create(normalStall.Id, today);
        normal.MarkPaid("OR-NORMAL", collector.Id, fishKilos: 2m);
        var adjusted = DailyCollection.Create(adjustedStall.Id, today, dailyFee: 40m);
        adjusted.MarkPaid("OR-ADJUSTED", collector.Id, fishKilos: 3m);
        adjusted.AddMonthEndAdjustment(60m);

        ctx.AddRange(collector, facility, normalStall, adjustedStall, normalContract, adjustedContract, normal, adjusted);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);

        // TestFeeRates states ₱1/kg: (₱30 + ₱2 fish) + (₱40 + ₱60 adjustment + ₱3 fish) = ₱135.
        var listed = Assert.Single(await repo.GetAllCollectorsWithStatsAsync(today.Year, today.Month));
        Assert.Equal(135m, listed.CollectedThisMonth);
        Assert.Equal(2, listed.Transactions);

        var activity = await repo.GetCollectorActivityAsync(collector.Id, today.Year, today.Month);
        Assert.NotNull(activity);
        Assert.Equal(135m, activity.CollectedThisMonth);
        Assert.Contains(activity.RecentTransactions, row => row.ORNumber == "OR-NORMAL" && row.Amount == 32m);
        Assert.Contains(activity.RecentTransactions, row => row.ORNumber == "OR-ADJUSTED" && row.Amount == 103m);
    }

    [Fact]
    public async Task GetAllCollectorsWithStats_IgnoresOtherCollectorsCollections()
    {
        await using var ctx = NewContext();

        var collector = CollectorUser.Create("Juan", "EEMO-2026-001", "juan", "juan@x.com", "0917", TestPasswords.Hash("pw"));
        var otherCollectorId = Guid.NewGuid();

        var theirs = PaymentRecord.Create(Guid.NewGuid(), 2026, 1, 900m);
        theirs.UpdateStatus(PaymentStatus.Paid, 0m, null, "t", otherCollectorId);

        ctx.Add(collector);
        ctx.Add(theirs);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var dto = Assert.Single(await repo.GetAllCollectorsWithStatsAsync(2026, 1));

        Assert.Equal(0m, dto.CollectedThisMonth);
        Assert.Equal(0, dto.Transactions);
    }

    // Regression: collectors assigned to per-transaction facilities (SLH/TRM/TPM) previously
    // showed ₱0 / 0 because only PaymentRecords + DailyCollections were aggregated.
    [Fact]
    public async Task GetAllCollectorsWithStats_IncludesSlaughterTripAndMarketCollections()
    {
        await using var ctx = NewContext();
        var today = PhilippineTime.Today;

        var collector = CollectorUser.Create("Pedro Cruz", "EEMO-2026-009", "pedro", "pedro@x.com", "0917", TestPasswords.Hash("pw"));

        // SLH: Hog ×1 = ₱250 (TransactionDate carries the period)
        var slh = SlaughterTransaction.CreateHog(Guid.NewGuid(), collector.Id, "Owner A", 1, "OR-S1", today);

        // TRM: one trip = ₱30 (RecordedAt = UtcNow → current month)
        var trip = TrmTrip.Create(Guid.NewGuid(), 1, "Driver A", "ABC 123", "Route 1", "OR-T1", collectorId: collector.Id);

        // TPM: one paid vendor = ₱100 on a Friday in the current month
        var friday = new DateOnly(today.Year, today.Month, 1);
        while (friday.DayOfWeek != DayOfWeek.Friday) friday = friday.AddDays(1);
        var tpm = TpmAttendance.Create(Guid.NewGuid(), friday);
        tpm.MarkPaid(collector.Id);

        ctx.Add(collector);
        ctx.Add(slh);
        ctx.Add(trip);
        ctx.Add(tpm);
        await ctx.SaveChangesAsync();

        var repo = new CollectorRepository(ctx);
        var dto = Assert.Single(await repo.GetAllCollectorsWithStatsAsync(today.Year, today.Month));

        Assert.Equal(380m, dto.CollectedThisMonth); // 250 (SLH) + 30 (TRM) + 100 (TPM)
        Assert.Equal(3, dto.Transactions);
    }
}
