using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Reads a RESTORED SNAPSHOT of production and checks the occupancy rules against real history — the multi-occupancy
/// shapes hand-built fixtures cannot reproduce: stalls let several times, mid-month handovers, arrears settled after
/// a handover, stalls standing empty, and stalls re-let and occupied today.
///
/// <para>Read-only throughout, and skipped (each test no-ops, stating why in its output) unless
/// <c>STALLTRACK_SNAPSHOT_DB</c> points at a local snapshot database. That keeps CI and a fresh clone green and
/// makes it impossible to run these against the live system by accident.</para>
/// </summary>
public class OccupancyHistorySnapshotTests(ITestOutputHelper output)
{
    /// <summary>False when there is no snapshot to read; the caller then returns without asserting.</summary>
    private bool SnapshotReady()
    {
        if (SnapshotDatabase.Available) return true;
        output.WriteLine("SKIPPED: " + SnapshotDatabase.SkipReason);
        return false;
    }

    private static async Task<List<Guid>> TenantIdsAsync()
    {
        await using var context = SnapshotDatabase.OpenRead(Guid.Empty);
        return await context.Municipalities.IgnoreQueryFilters().Select(m => m.Id).ToListAsync();
    }

    [Fact]
    public async Task OccupancyWindowsNeverOverlapOnAStall()
    {
        // The invariant every per-lessee figure rests on: at most one lessee holds a stall on any given day.
        if (!SnapshotReady()) return;
        var today = PhilippineTime.Today;

        await using var context = SnapshotDatabase.OpenRead(Guid.Empty);
        var stalls = await context.Stalls.IgnoreQueryFilters()
            .Include(s => s.Contracts).Include(s => s.Facility)
            .Where(s => s.Contracts.Any())
            .ToListAsync();

        var overlaps = new List<string>();

        foreach (var stall in stalls)
        {
            var windows = stall.Occupancies(today).OrderBy(o => o.Start).ToList();
            for (var i = 1; i < windows.Count; i++)
            {
                if (windows[i].Start <= windows[i - 1].End)
                    overlaps.Add($"{stall.Facility?.Name} stall {stall.StallNo}: "
                        + $"{windows[i - 1].Occupant} to {windows[i - 1].End:yyyy-MM-dd} overlaps "
                        + $"{windows[i].Occupant} from {windows[i].Start:yyyy-MM-dd}");
            }
        }

        output.WriteLine($"Checked {stalls.Count} stalls that have ever been let.");
        Assert.True(overlaps.Count == 0, "Overlapping occupancies found:\n" + string.Join('\n', overlaps));
    }

    [Fact]
    public async Task EveryEndedOccupancyReachesTheInactiveRegister()
    {
        // The defect this suite exists for: a stall re-let to a new lessee used to drop its previous lessee entirely.
        if (!SnapshotReady()) return;
        var today = PhilippineTime.Today;

        foreach (var tenant in await TenantIdsAsync())
        {
            await using var context = SnapshotDatabase.OpenRead(tenant);

            var ended = (await context.Stalls.Include(s => s.Contracts)
                    .Where(s => s.Contracts.Any()).ToListAsync())
                .SelectMany(s => s.Occupancies(today))
                .Count(o => !o.IsCurrent);

            var register = await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None);

            output.WriteLine($"Tenant {tenant}: {ended} ended occupancies, {register.Count} register rows.");
            Assert.True(register.Count >= ended,
                $"Tenant {tenant}: {ended} ended occupancies but only {register.Count} rows on the register.");
        }
    }

    [Fact]
    public async Task NoLesseeIsCreditedWithAnotherLesseesCollections()
    {
        // Per stall, what all ended occupancies are credited with may never exceed everything ever collected on it —
        // the check that catches money crossing between lessees on a re-let stall.
        if (!SnapshotReady()) return;

        foreach (var tenant in await TenantIdsAsync())
        {
            await using var context = SnapshotDatabase.OpenRead(tenant);
            var register = await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None);

            foreach (var group in register.GroupBy(r => r.StallId))
            {
                var credited = group.Sum(r => r.LifetimeCollected);

                var everCollected =
                    await context.PaymentRecords.Where(p => p.StallId == group.Key).SumAsync(p => p.AmountPaid)
                    + await context.DailyCollections.Where(d => d.StallId == group.Key && d.IsPaid).SumAsync(d => d.DailyFee);

                Assert.True(credited <= everCollected + 0.01m,
                    $"Stall {group.First().StallNo}: ended occupancies credited ₱{credited:N2} but only "
                    + $"₱{everCollected:N2} was ever collected on that stall.");
            }
        }
    }

    [Fact]
    public async Task AReLetStallsPastOccupancyIsReadOnly()
    {
        // A row for a stall somebody else now holds must not offer Renew or Reopen — that would displace the
        // sitting lessee.
        if (!SnapshotReady()) return;
        var today = PhilippineTime.Today;

        var examined = 0;

        foreach (var tenant in await TenantIdsAsync())
        {
            await using var context = SnapshotDatabase.OpenRead(tenant);

            var reLet = (await context.Stalls.Include(s => s.Contracts)
                    .Where(s => s.Contracts.Count > 1).ToListAsync())
                .Where(s => s.Occupancies(today).Any(o => o.IsCurrent) && s.Occupancies(today).Any(o => !o.IsCurrent))
                .Select(s => s.Id)
                .ToHashSet();

            if (reLet.Count == 0) continue;

            var register = await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None);
            foreach (var row in register.Where(r => reLet.Contains(r.StallId)))
            {
                examined++;
                Assert.True(row.StallReLet, $"Stall {row.StallNo} is re-let but its past occupancy is still actionable.");
            }
        }

        output.WriteLine(examined == 0
            ? "No re-let stalls in the snapshot yet — nothing to check."
            : $"Checked {examined} past occupancies on re-let stalls.");
    }

    [Fact]
    public async Task AVacantStallStillReportsItsLastLessee()
    {
        if (!SnapshotReady()) return;
        var today = PhilippineTime.Today;

        var examined = 0;

        foreach (var tenant in await TenantIdsAsync())
        {
            await using var context = SnapshotDatabase.OpenRead(tenant);

            var vacant = (await context.Stalls.Include(s => s.Contracts)
                    .Where(s => s.Contracts.Any() && s.Status == StallStatus.Active).ToListAsync())
                .Where(s => s.IsVacant(today))
                .Select(s => s.Id)
                .ToHashSet();

            if (vacant.Count == 0) continue;

            var register = await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None);
            foreach (var stallId in vacant)
            {
                examined++;
                Assert.Contains(register, r => r.StallId == stallId);
            }
        }

        output.WriteLine($"Checked {examined} vacant stalls.");
    }
}
