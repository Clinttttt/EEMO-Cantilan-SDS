using System.Diagnostics;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class NpmRentMonthFinancialTests
{
    [SkippableFact]
    public async Task Synthetic_LGU_year_keeps_12000_months_300_receipts_and_report_totals_bounded()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed();
        await using (var setup = fixture.Create(w.Tenant))
        {
            setup.NpmRentMonths.RemoveRange(await setup.NpmRentMonths.ToListAsync());
            await setup.SaveChangesAsync();
            var facility = await setup.Facilities.SingleAsync();
            var pairs = Enumerable.Range(1, 1000).Select(i => {
                var stall = i == 1 ? setup.Stalls.Single() : Stall.Create(facility.Id, "LGU-" + i, 900,
                    ApplicableFees.DailyRental, municipalityId: w.Tenant);
                var contract = i == 1 ? setup.Contracts.Single() : Contract.Create(stall.Id, "Synthetic " + i,
                    "Fixture", new(2025, 1, 1), 3, 900);
                return (stall, contract);
            }).ToArray();
            foreach (var (stall, contract) in pairs.Skip(1)) setup.AddRange(stall, contract);
            await setup.SaveChangesAsync();
            foreach (var period in Enumerable.Range(1, 12).Select(m => new DateOnly(2025, m, 1)))
                foreach (var (stall, contract) in pairs) setup.Add(Prepare(facility, stall, contract, w.Actor, period));
            await setup.SaveChangesAsync();
        }
        // Domain-created synthetic report evidence, not a live API/writer or a new posting authority.
        await using (var cash = fixture.Create(w.Tenant))
        {
            var classification = await cash.RevenueClassifications.SingleAsync();
            var policy = await cash.RevenueClassificationPolicies.SingleAsync();
            var sources = await cash.NpmRentMonths.Where(x => x.PeriodStart == new DateOnly(2025, 8, 1))
                .OrderBy(x => x.StallId).Take(300).ToListAsync();
            foreach (var source in sources)
            {
                var received = sources.IndexOf(source) < 200 ? 900m : 350m;
                cash.Collections.Add(Collection.Post(BusinessDate, new Clock().UtcNow, w.Actor.ToString(), "Synthetic Head", "SuperAdmin",
                    [new CollectionLineDraft(classification, policy, received, CollectionSourceKind.NpmRentMonth, source.Id, null,
                        Allocations: [new(CollectionSourceKind.NpmRentMonth, source.Id, received,
                            SourceSnapshot: "{\"fixture\":\"large-LGU\"}")])], clientOperationId: Guid.NewGuid()));
                cash.Add(source.RecordSettlementChange(3, w.Actor, "Synthetic Head", "SuperAdmin", new Clock().UtcNow, "Synthetic receipt fixture"));
            }
            await cash.SaveChangesAsync();
        }
        var counter = new QueryCounter();
        await using var db = fixture.Create(w.Tenant, counter); var flow = Workflow(db, w);
        var ids = await db.Stalls.Select(x => x.Id).ToArrayAsync();
        var allocatedBefore = GC.GetTotalAllocatedBytes(); var retainedBefore = GC.GetTotalMemory(true);
        counter.Reads = 0; var watch = Stopwatch.StartNew(); decimal outstanding = 0; decimal receivedTotal = 0;
        for (var m = 1; m <= 12; m++)
        {
            var positions = await flow.ReadReportPositionsAsync(ids, new(2025, m, 1), BusinessDate);
            Assert.Equal(1000, positions.Count);
            outstanding += positions.Values.Sum(x => x!.Position.Outstanding);
            receivedTotal += positions.Values.Sum(x => x!.Position.CanonicalCash);
        }
        watch.Stop(); var allocated = GC.GetTotalAllocatedBytes() - allocatedBefore;
        var retained = GC.GetTotalMemory(true) - retainedBefore;
        var reportReads = counter.Reads;
        Assert.Equal(215000, receivedTotal); Assert.Equal(10585000, outstanding);
        Assert.True(reportReads <= 144, $"Year report exceeded 12 reads/month: {reportReads}");
        Assert.True(retained < 256L * 1024 * 1024, $"Unexpected retained managed memory: {retained}");
        var facilityId = await db.Facilities.Select(x => x.Id).SingleAsync();
        var moves = await flow.ReadCashMovementsAsync(facilityId, new(2026, 10, 1), new(2026, 10, 31));
        Assert.Equal(300, moves.Count); Assert.Equal(215000, moves.Sum(x => x.Amount));
        Assert.All(moves, x => Assert.Equal(BusinessDate, x.BusinessDate));
        var receipts = await db.Collections.AsNoTracking().OrderBy(x => x.ReferenceNumber)
            .Select(x => new { x.Id, x.ReferenceCode, x.TotalAmount, x.BusinessDate }).ToListAsync();
        Assert.Equal(300, receipts.Select(x => x.ReferenceCode).Distinct().Count());
        Assert.All(receipts, x => Assert.StartsWith("SRC-2026-", x.ReferenceCode));
        Assert.Empty(await db.DailyCollections.ToListAsync()); Assert.Empty(await db.CollectionRemittances.ToListAsync());
        var evidence = new { stalls = 1000, assessmentMonths = 12000, receipts = 300, collected = receivedTotal,
            outstanding, sqlReads = reportReads, elapsedMs = watch.ElapsedMilliseconds, allocatedBytes = allocated,
            retainedManagedBytes = retained, processWorkingSetBytes = Environment.WorkingSet };
        output.WriteLine(JsonSerializer.Serialize(evidence));
        var directory = Path.Combine(Path.GetTempPath(), "stalltrack-npm-acceptance"); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "large-lgu-metrics.json"), JsonSerializer.Serialize(evidence));
        await File.WriteAllTextAsync(Path.Combine(directory, "synthetic-src-evidence.json"), JsonSerializer.Serialize(receipts));
    }
}
