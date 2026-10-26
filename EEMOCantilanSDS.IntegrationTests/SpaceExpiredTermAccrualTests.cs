using Microsoft.EntityFrameworkCore;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// A space rental follows the stall rule at the end of a signed term: an expired, unrenewed term stops accruing NEW monthly charges, the months
/// already incurred (up to the term's last day) stay collectible on the original account, and a space held without a contract is unaffected.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SpaceExpiredTermAccrualTests(PostgresFixture db)
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    private async Task<(Guid Tenant, ObligationAccount Signed, ObligationAccount SpaceOnly)> SeedAsync()
    {
        await db.ResetAsync();
        var tenant = await SpaceTestKit.SeedTenantAsync(db, "Expired term");
        var registrations = new List<SpaceRegistration>();
        ObligationAccount Open(string space, OccupancyArrangement arrangement, bool signed)
        {
            var account = ObligationAccount.Create(tenant, ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, space, null, null,
                new DateOnly(2022, 10, 1), "test", actualOccupant: $"Holder {space}");
            var registration = SpaceRegistration.Create(tenant, ObligationKind.KanmanggaySpaceRental, space, "test");
            registrations.Add(registration);
            account.LinkRegistration(registration.Id);
            account.SetOccupancyBasis(arrangement, signed ? "C-1" : null);
            if (signed) account.SetLeaseDetails("Holder", new DateOnly(2022, 10, 25), 3, null, null, 1000m);
            return account;
        }
        var signedAccount = Open("5", OccupancyArrangement.SignedContract, signed: true);   // three years from 2022-10-25: last day 2025-10-24
        var spaceOnly = Open("6", OccupancyArrangement.SpaceOnly, signed: false);
        await using var ctx = db.CreateContext(tenant);
        ctx.SpaceRegistrations.AddRange(registrations);
        foreach (var account in new[] { signedAccount, spaceOnly })
        {
            ctx.ObligationAccounts.Add(account);
            ctx.ObligationRates.Add(ObligationRate.Create(tenant, account.Id, new DateOnly(2022, 10, 1), 1000m, "test"));
        }
        await ctx.SaveChangesAsync();
        return (tenant, signedAccount, spaceOnly);
    }

    [SkippableFact]
    public async Task AnExpiredUnrenewedTerm_StopsNewMonthlyCharges_ButEveryMonthUpToItsLastDayStaysCollectible()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        var (tenant, signed, spaceOnly) = await SeedAsync();
        await using var ctx = db.CreateContext(tenant);
        var accounts = await ctx.ObligationAccounts.AsNoTracking().ToListAsync();
        var quotes = await new ObligationCollectionSource(ctx).GetQuotesAsync(tenant, accounts, Today, default, lookbackMonths: 480);

        var signedMonths = quotes.Where(q => q.AccountId == signed.Id).Select(q => q.PeriodStart).OrderBy(x => x).ToList();
        Assert.Equal(new DateOnly(2022, 10, 1), signedMonths.First());
        Assert.Equal(new DateOnly(2025, 10, 1), signedMonths.Last());                      // the month that contains the term's last day; nothing after it
        Assert.Equal(37, signedMonths.Count);
        Assert.All(quotes.Where(q => q.AccountId == signed.Id), q => { Assert.True(q.CanAddToDraft); Assert.Equal(1000m, q.OutstandingAmount); });

        var openMonths = quotes.Where(q => q.AccountId == spaceOnly.Id).Select(q => q.PeriodStart).OrderBy(x => x).ToList();
        Assert.Equal(new DateOnly(2026, 10, 1), openMonths.Last());                        // no contract, no term to run out: unchanged
        Assert.Equal(49, openMonths.Count);

    }
}
