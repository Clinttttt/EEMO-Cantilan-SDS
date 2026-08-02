using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Follow-up History answers "what needed following up THEN", so contract attention is judged as of the end of the
/// period on screen: a term still running in 2024 was not an expired contract in 2024, and a term that ran out in
/// 2024 stays expired in every later view. These lock that reading in, because a year view showing nothing under
/// Contract is only correct when nothing had in fact lapsed by then.
/// </summary>
public class ContractAttentionAsOfTests : RepositoryTestBase
{
    private static async Task<StallRepository> SeedAsync(
        EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext context, DateOnly effectivity, int years)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "24", 900m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        stall.Contracts.Add(Contract.Create(stall.Id, "Dennis S. Doloriel", "Dennis S. Doloriel", effectivity, years, 900m));

        context.AddRange(facility, stall);
        await context.SaveChangesAsync();
        return new StallRepository(context);
    }

    [Fact]
    public async Task ATermStillRunning_IsNotAnExpiredContract_InThatYearsView()
    {
        // Jun 2023 → Jun 7, 2026. Seen from 2024 and 2025 the term had years to run, so the office had nothing to
        // follow up: the rows the office sees under "Whole time" belong to 2026, when the term actually lapsed.
        var context = NewContext();
        var repo = await SeedAsync(context, new DateOnly(2023, 6, 7), 3);

        var as2024 = await repo.GetContractAttentionAsOfAsync(2024, 12, DomainRules.ExpiringSoonMonths, CancellationToken.None);
        var as2025 = await repo.GetContractAttentionAsOfAsync(2025, 12, DomainRules.ExpiringSoonMonths, CancellationToken.None);

        Assert.Empty(as2024);
        Assert.Empty(as2025);
    }

    [Fact]
    public async Task TheSameTerm_IsExpired_OnceItsYearHasPassed()
    {
        var context = NewContext();
        var repo = await SeedAsync(context, new DateOnly(2023, 6, 7), 3);

        var as2026 = await repo.GetContractAttentionAsOfAsync(2026, 12, DomainRules.ExpiringSoonMonths, CancellationToken.None);

        var row = Assert.Single(as2026);
        Assert.True(row.IsExpired);
        Assert.Equal(new DateOnly(2026, 6, 7), row.ExpiryDate);
    }

    [Fact]
    public async Task ATermExpiringWithinThreeMonths_IsFlaggedAsExpiring_NotExpired()
    {
        // Jan 1, 2026 expiry seen from the end of 2025: inside the warning window, so it reads "Contract expiring".
        var context = NewContext();
        var repo = await SeedAsync(context, new DateOnly(2023, 1, 1), 3);

        var as2025 = await repo.GetContractAttentionAsOfAsync(2025, 12, DomainRules.ExpiringSoonMonths, CancellationToken.None);

        var row = Assert.Single(as2025);
        Assert.False(row.IsExpired);
        Assert.Equal(new DateOnly(2026, 1, 1), row.ExpiryDate);
    }

    [Fact]
    public async Task ATermThatRanOutInAPastYear_StaysExpired_InEveryLaterView()
    {
        // A term that lapsed in June 2024 must appear as expired in 2024's own view and in every year after it —
        // this is the case that would be a real loss of history if a year view dropped it.
        var context = NewContext();
        var repo = await SeedAsync(context, new DateOnly(2021, 6, 7), 3);

        foreach (var year in new[] { 2024, 2025, 2026 })
        {
            var rows = await repo.GetContractAttentionAsOfAsync(year, 12, DomainRules.ExpiringSoonMonths, CancellationToken.None);
            var row = Assert.Single(rows);
            Assert.True(row.IsExpired);
        }
    }
}
