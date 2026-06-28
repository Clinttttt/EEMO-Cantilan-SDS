using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The stall profile's transparency Collection History is cursor-paginated newest-first. For NPM
/// each recorded daily collection (paid or absent) is one row, carrying its status, amount, OR
/// number, and collector.
/// </summary>
public class PaymentRepositoryCollectionHistoryTests : RepositoryTestBase
{
    // 5 paid days (2026-06-01..05) + 1 absent day (2026-06-06); newest first => absent leads.
    private static async Task<(Guid stallId, Guid collectorId)> SeedNpmAsync(EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext context)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Maria Santos", "Maria Santos", new DateOnly(2026, 1, 1), 3, 900m);
        var collector = CollectorUser.Create("Juan Collector", "EEMO-2026-001", "juan", "juan@eemo.gov", "09170000000", "Secret123!");

        var paidDays = Enumerable.Range(1, 5).Select(d =>
        {
            var dc = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, d));
            dc.MarkPaid($"OR-{d}", collector.Id);
            return dc;
        }).ToList();

        var absent = DailyCollection.Create(stall.Id, new DateOnly(2026, 6, 6));
        absent.MarkAbsent();

        context.AddRange(facility, stall, contract);
        context.Add(collector);
        context.AddRange(paidDays);
        context.Add(absent);
        await context.SaveChangesAsync();
        return (stall.Id, collector.Id);
    }

    [Fact]
    public async Task NpmHistory_HonorsPageSize_NewestFirst_AndReportsHasMore()
    {
        var context = NewContext();
        var (stallId, _) = await SeedNpmAsync(context);
        var repo = new PaymentRepository(context);

        var page1 = await repo.GetStallCollectionHistoryAsync(stallId, cursor: null, pageSize: 2, CancellationToken.None);

        Assert.Equal(2, page1.Items.Count);
        Assert.True(page1.HasMore);
        Assert.NotNull(page1.NextCursor);
        // Newest first: 2026-06-06 (absent) then 2026-06-05.
        Assert.Equal(new DateTime(2026, 6, 6), page1.Items[0].Date);
        Assert.Equal(new DateTime(2026, 6, 5), page1.Items[1].Date);
        // Cursor is the last row's date, so the next page continues strictly before it.
        Assert.Equal(new DateTime(2026, 6, 5), page1.NextCursor);
    }

    [Fact]
    public async Task NpmHistory_MapsStatusAmountOrAndCollector()
    {
        var context = NewContext();
        var (stallId, _) = await SeedNpmAsync(context);
        var repo = new PaymentRepository(context);

        var page1 = await repo.GetStallCollectionHistoryAsync(stallId, cursor: null, pageSize: 2, CancellationToken.None);

        var absentRow = page1.Items[0];
        Assert.Equal("Absent", absentRow.Status);
        Assert.Equal(0m, absentRow.Amount);
        Assert.True(string.IsNullOrEmpty(absentRow.ORNumber));
        Assert.Null(absentRow.CollectorName);

        var paidRow = page1.Items[1];
        Assert.Equal("Paid", paidRow.Status);
        Assert.Equal(FeeRates.NpmDailyFee, paidRow.Amount);
        Assert.Equal("OR-5", paidRow.ORNumber);
        Assert.Equal("Juan Collector", paidRow.CollectorName);
        Assert.Equal("Maria Santos", paidRow.PayorName);
    }

    [Fact]
    public async Task NpmHistory_CursorWalk_ReturnsAllRowsOnce_ThenStops()
    {
        var context = NewContext();
        var (stallId, _) = await SeedNpmAsync(context);
        var repo = new PaymentRepository(context);

        var seen = new List<DateTime>();
        DateTime? cursor = null;
        var guard = 0;
        while (true)
        {
            var page = await repo.GetStallCollectionHistoryAsync(stallId, cursor, pageSize: 2, CancellationToken.None);
            seen.AddRange(page.Items.Select(i => i.Date));
            if (!page.HasMore) break;
            cursor = page.NextCursor;
            if (++guard > 10) throw new Exception("pagination did not terminate");
        }

        // 6 recorded days, each exactly once, strictly descending.
        Assert.Equal(6, seen.Count);
        Assert.Equal(6, seen.Distinct().Count());
        Assert.Equal(seen.OrderByDescending(d => d).ToList(), seen);
    }

    [Fact]
    public async Task History_UnknownStall_ReturnsEmpty()
    {
        var context = NewContext();
        await SeedNpmAsync(context);
        var repo = new PaymentRepository(context);

        var page = await repo.GetStallCollectionHistoryAsync(Guid.NewGuid(), cursor: null, pageSize: 10, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
        Assert.Null(page.NextCursor);
    }
}
