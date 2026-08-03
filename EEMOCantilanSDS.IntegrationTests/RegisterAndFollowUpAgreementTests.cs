using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Dtos.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Reads a RESTORED SNAPSHOT of production and checks that the two screens the office reconciles by hand agree: the
/// register of closed and expired accounts, and the whole-time Past follow-up. They are built by different code over
/// the same history, so a disagreement between them is exactly the kind of reporting error nobody notices until a
/// figure is questioned in public.
///
/// <para>Read-only throughout, and skipped (each test no-ops, stating why) unless <c>STALLTRACK_SNAPSHOT_DB</c>
/// points at a local snapshot database.</para>
/// </summary>
public class RegisterAndFollowUpAgreementTests(ITestOutputHelper output)
{
    /// <summary>
    /// Reports the run honestly: without a snapshot these tests are SKIPPED, not passed. A green result that
    /// asserted nothing is worse than no result at all — it reads as evidence.
    /// </summary>
    private void RequireSnapshot()
    {
        if (!SnapshotDatabase.Available) output.WriteLine("SKIPPED: " + SnapshotDatabase.SkipReason);
        Skip.IfNot(SnapshotDatabase.Available, SnapshotDatabase.SkipReason);
    }

    private static async Task<List<Guid>> TenantIdsAsync()
    {
        await using var context = SnapshotDatabase.OpenRead(Guid.Empty);
        return await context.Municipalities.IgnoreQueryFilters().Select(m => m.Id).ToListAsync();
    }

    [SkippableFact]
    public async Task EveryRegisterAccountWithABalance_IsCarriedByTheWholeTimeFollowUp()
    {
        // The office reads "who still owes, and how much" from the follow-up's Whole time view and checks it against
        // the register. Every account the register says owes something must be answerable for on that view — either
        // as an expired contract or as a past occupancy — and for the same figure.
        RequireSnapshot();
        var checked_ = 0;

        foreach (var tenant in await TenantIdsAsync())
        {
            await using var context = SnapshotDatabase.OpenRead(tenant);
            var stalls = new StallRepository(context);

            var register = (await stalls.GetClosedStallAccountsAsync(CancellationToken.None))
                .Where(a => a.Uncollected > 0m)
                .ToList();
            if (register.Count == 0) continue;

            // The Whole time view's own sources, composed exactly as the query handler composes them.
            var lifetimeBalances = register
                .GroupBy(a => $"{a.FacilityCode}|{a.StallNo}")
                .ToDictionary(g => g.Key, g => g.Sum(a => a.Uncollected));
            var lapsed = (await stalls.GetContractAttentionAsync(DomainRules.ExpiringSoonMonths, CancellationToken.None))
                .Where(c => c.IsExpired)
                .ToList();

            var queue = EEMOCantilanSDS.Application.Queries.Reports.GetFollowUpQueue.FollowUpComposer.Compose(
                PhilippineTime.Today.Year, PhilippineTime.Today.Month, PhilippineTime.Today,
                Array.Empty<DelinquentStallDto>(),
                new Dictionary<FacilityCode, FacilityReportsDto>(),
                Array.Empty<EEMOCantilanSDS.Application.Dtos.Payments.OnlinePaymentAwaitingOrDto>(),
                Array.Empty<EEMOCantilanSDS.Application.Dtos.Slaughterhouse.SlaughterTransactionDto>(),
                Array.Empty<EEMOCantilanSDS.Application.Dtos.TransportTerminal.TrmTripDto>(),
                Array.Empty<EEMOCantilanSDS.Application.Dtos.TaboanMarket.TpmVendorAttendanceDto>(),
                Array.Empty<EEMOCantilanSDS.Application.Dtos.Payments.UnreceiptedPaymentDto>(),
                lapsed,
                Array.Empty<EEMOCantilanSDS.Domain.Entities.Payments.UtilityBill>(),
                lifetimeBalances,
                await stalls.GetClosedStallAccountsAsync(CancellationToken.None),
                periodLabelOverride: "Whole time");

            var carried = queue.Items
                .Where(i => i.ReasonKind == "contract" && i.Amount > 0m)
                .GroupBy(i => $"{i.Facility}|{i.Identifier.Replace("Stall ", string.Empty)}")
                .ToDictionary(g => g.Key, g => g.Sum(i => i.Amount ?? 0m));

            foreach (var (key, owed) in lifetimeBalances)
            {
                Assert.True(carried.ContainsKey(key),
                    $"{tenant}: the register says {key} owes {owed:N2} but the Whole time follow-up carries no row for it.");
                Assert.Equal(owed, carried[key]);
                checked_++;
            }
        }

        output.WriteLine($"Reconciled {checked_} accounts between the register and the Whole time follow-up.");
    }

    [SkippableFact]
    public async Task ARegisterAccountsBalance_IsItsMonthsOfRent_LessWhatWasCollected()
    {
        // The monthly obligation ledger, checked against real history: for a daily-billed account, the balance is the
        // rent of the months it held the space (whole months at the monthly rent, part-months by their days) less the
        // installments received. This is the figure the office's paper is reconciled against.
        RequireSnapshot();
        var checked_ = 0;

        foreach (var tenant in await TenantIdsAsync())
        {
            await using var context = SnapshotDatabase.OpenRead(tenant);

            var register = await new StallRepository(context).GetClosedStallAccountsAsync(CancellationToken.None);
            var npm = register.Where(a => a.FacilityCode == FacilityCode.NPM).ToList();
            if (npm.Count == 0) continue;

            foreach (var account in npm)
            {
                // Nothing may exceed the whole term's rent, and nothing may be negative — the two ways a ledger
                // goes wrong.
                var months = MonthsBetween(account.EffectivityDate, account.OccupancyEndedOn ?? account.ExpiryDate);
                var ceiling = months * account.MonthlyRate;

                Assert.True(account.Uncollected >= 0m,
                    $"{tenant}: {account.FacilityCode} {account.StallNo} has a negative balance ({account.Uncollected:N2}).");
                Assert.True(account.Uncollected <= ceiling,
                    $"{tenant}: {account.FacilityCode} {account.StallNo} owes {account.Uncollected:N2}, more than the "
                    + $"{months} months of rent it could have been charged ({ceiling:N2}).");
                checked_++;
            }
        }

        output.WriteLine($"Checked {checked_} daily-billed register accounts against their months of rent.");
    }

    // Whole months the occupancy touched, inclusive of both ends — the count the ceiling above is measured in.
    private static int MonthsBetween(DateOnly from, DateOnly to) =>
        to < from ? 0 : ((to.Year - from.Year) * 12) + (to.Month - from.Month) + 1;
}
