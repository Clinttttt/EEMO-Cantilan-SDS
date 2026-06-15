using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Slaughterhouse;
using EEMOCantilanSDS.Domain.Entities.TaboanMarket;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Regression tests for the cross-facility transaction transparency feed
/// (<see cref="TransactionFeedRepository.GetRecentTransactionsAsync"/>).
/// </summary>
public class TransactionFeedTests : RepositoryTestBase
{
    [Fact]
    public async Task GetRecent_All_MergesEverySourceWithCorrectAmounts()
    {
        var context = NewContext();

        // NPM stall rent payment (₱900) — needs Facility + Stall + active Contract.
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(npm.Id, "A-1", 900m, ApplicableFees.BaseRental);
        var contract = Contract.Create(stall.Id, "Juan Dela Cruz", null, new DateOnly(2024, 1, 1), 3, 900m);
        var payment = PaymentRecord.Create(stall.Id, 2024, 1, 900m);
        payment.RecordPayment("OR-NPM-1", Guid.NewGuid(), PaymentStatus.Paid);
        context.Facilities.Add(npm);
        context.Stalls.Add(stall);
        context.Contracts.Add(contract);
        context.PaymentRecords.Add(payment);

        // SLH slaughter (hog ×2 = ₱500)
        var slhFac = Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH");
        context.Facilities.Add(slhFac);
        context.SlaughterTransactions.Add(
            SlaughterTransaction.CreateHog(slhFac.Id, null, "Owner A", 2, "OR-SLH-1", new DateOnly(2024, 2, 1)));

        // TRM trip (₱30)
        context.TrmTrips.Add(
            TrmTrip.Create(Guid.NewGuid(), 1, "Driver X", "ABC123", "North", "OR-TRM-1"));

        // TPM paid attendance (₱100)
        var vendor = TpmVendor.Create("Vendor V", "Vegetables");
        var att = TpmAttendance.Create(vendor.Id, new DateOnly(2024, 1, 5));
        att.MarkPaid(null);
        context.TpmVendors.Add(vendor);
        context.TpmAttendances.Add(att);

        await context.SaveChangesAsync();

        var repo = new TransactionFeedRepository(context);
        var feed = await repo.GetRecentTransactionsAsync(null, null, 100, CancellationToken.None);

        Assert.Equal(4, feed.Count);
        Assert.Contains(feed, t => t.FacilityCode == FacilityCode.NPM && t.Amount == 900m && t.Party == "Juan Dela Cruz");
        Assert.Contains(feed, t => t.FacilityCode == FacilityCode.SLH && t.Amount == 500m);
        Assert.Contains(feed, t => t.FacilityCode == FacilityCode.TRM && t.Amount == 30m && t.Party == "Driver X");
        Assert.Contains(feed, t => t.FacilityCode == FacilityCode.TPM && t.Amount == 100m && t.Party == "Vendor V");

        // Newest-first ordering invariant.
        var times = feed.Select(t => t.OccurredAt).ToList();
        Assert.Equal(times.OrderByDescending(x => x).ToList(), times);
    }

    [Fact]
    public async Task GetRecent_FacilityFilter_ReturnsOnlyThatFacility()
    {
        var context = NewContext();

        context.TrmTrips.Add(TrmTrip.Create(Guid.NewGuid(), 1, "Driver X", "ABC123", "North", "OR-TRM-1"));
        var vendor = TpmVendor.Create("Vendor V", "Vegetables");
        var att = TpmAttendance.Create(vendor.Id, new DateOnly(2024, 1, 5));
        att.MarkPaid(null);
        context.TpmVendors.Add(vendor);
        context.TpmAttendances.Add(att);
        await context.SaveChangesAsync();

        var repo = new TransactionFeedRepository(context);

        var trm = await repo.GetRecentTransactionsAsync(FacilityCode.TRM, null, 100, CancellationToken.None);
        Assert.Single(trm);
        Assert.All(trm, t => Assert.Equal(FacilityCode.TRM, t.FacilityCode));

        var tpm = await repo.GetRecentTransactionsAsync(FacilityCode.TPM, null, 100, CancellationToken.None);
        Assert.Single(tpm);
        Assert.All(tpm, t => Assert.Equal(FacilityCode.TPM, t.FacilityCode));

        // A facility with no transactions returns an empty feed.
        var bbq = await repo.GetRecentTransactionsAsync(FacilityCode.BBQ, null, 100, CancellationToken.None);
        Assert.Empty(bbq);
    }

    [Fact]
    public async Task GetRecent_OnDate_ReturnsOnlyThatDaysTransactions()
    {
        var context = NewContext();
        var vendor = TpmVendor.Create("Vendor V", "Vegetables");
        context.TpmVendors.Add(vendor);

        // Two market days; filtering by Jan 12 must exclude the Jan 5 entry.
        var jan05 = TpmAttendance.Create(vendor.Id, new DateOnly(2024, 1, 5)); jan05.MarkPaid(null);
        var jan12 = TpmAttendance.Create(vendor.Id, new DateOnly(2024, 1, 12)); jan12.MarkPaid(null);
        context.TpmAttendances.AddRange(jan05, jan12);
        await context.SaveChangesAsync();

        var repo = new TransactionFeedRepository(context);

        var onJan12 = await repo.GetRecentTransactionsAsync(null, new DateOnly(2024, 1, 12), 100, CancellationToken.None);
        Assert.Single(onJan12);
        Assert.Equal(new DateOnly(2024, 1, 12), DateOnly.FromDateTime(onJan12[0].OccurredAt));
        Assert.False(onJan12[0].HasTime);   // market day carries a calendar date only

        var allDays = await repo.GetRecentTransactionsAsync(null, null, 100, CancellationToken.None);
        Assert.Equal(2, allDays.Count);
    }

    [Fact]
    public async Task GetRecent_ExcludesUnpaidStallRecords()
    {
        var context = NewContext();

        var ncc = Facility.Create(FacilityCode.NCC, "New Commercial Center", "NCC");
        var stall = Stall.Create(ncc.Id, "C-1", 1200m, ApplicableFees.BaseRental);
        // Unpaid placeholder record — must NOT appear in the feed.
        var unpaid = PaymentRecord.Create(stall.Id, 2024, 1, 1200m);
        context.Facilities.Add(ncc);
        context.Stalls.Add(stall);
        context.PaymentRecords.Add(unpaid);
        await context.SaveChangesAsync();

        var repo = new TransactionFeedRepository(context);
        var feed = await repo.GetRecentTransactionsAsync(FacilityCode.NCC, null, 100, CancellationToken.None);

        Assert.Empty(feed);
    }

    [Fact]
    public async Task GetRecent_CollapsesMultiAnimalSlaughterReceiptIntoOneRow()
    {
        var context = NewContext();
        var slhFac = Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH");
        context.Facilities.Add(slhFac);

        // One receipt (OR-651) for Ana Reyes covering a hog (₱250) and a cow (₱365) = ₱615.
        var date = new DateOnly(2026, 6, 11);
        context.SlaughterTransactions.Add(SlaughterTransaction.CreateHog(slhFac.Id, null, "Ana Reyes", 1, "OR-651", date));
        context.SlaughterTransactions.Add(SlaughterTransaction.CreateLargeAnimal(slhFac.Id, null, "Ana Reyes", AnimalType.Cow, 1, "OR-651", date));
        await context.SaveChangesAsync();

        var repo = new TransactionFeedRepository(context);
        var feed = await repo.GetRecentTransactionsAsync(FacilityCode.SLH, null, 100, CancellationToken.None);

        var row = Assert.Single(feed);
        Assert.Equal(FacilityCode.SLH, row.FacilityCode);
        Assert.Equal("Ana Reyes", row.Party);
        Assert.Equal("OR-651", row.ORNumber);
        Assert.Equal(615m, row.Amount);
    }

    [Fact]
    public async Task GetRecent_DistinctSlaughterReceipts_StayAsSeparateRows()
    {
        var context = NewContext();
        var slhFac = Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH");
        context.Facilities.Add(slhFac);

        var date = new DateOnly(2026, 6, 11);
        context.SlaughterTransactions.Add(SlaughterTransaction.CreateHog(slhFac.Id, null, "Ana Reyes", 1, "OR-651", date));
        context.SlaughterTransactions.Add(SlaughterTransaction.CreateHog(slhFac.Id, null, "Jinggoy Estrada", 1, "OR-652", date));
        await context.SaveChangesAsync();

        var repo = new TransactionFeedRepository(context);
        var feed = await repo.GetRecentTransactionsAsync(FacilityCode.SLH, null, 100, CancellationToken.None);

        Assert.Equal(2, feed.Count);
    }
}
