using EEMOCantilanSDS.Application.Command.Payments.BulkImportDailyHistory;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Application.Payments;

/// <summary>
/// Importing the market's collection history into a CUSTOM section.
///
/// <para>
/// The market numbers its spaces independently per section, so "1" is a different space in the Vegetable Area, in Sari Sari and
/// in every other section an LGU adds. An import row names a NUMBER, and the section it belongs to comes from the chooser the
/// clerk used — so the only thing standing between one lessee's money and another lessee's account is that the section is
/// carried through and matched.
/// </para>
///
/// <para>
/// The existing tests for this import mock the stall lookup with <c>It.IsAny&lt;MarketSection?&gt;()</c>, which cannot fail if
/// the section is dropped: whatever is asked for, the mock hands back the same stall. These use the real repository over a real
/// context so the filter is actually exercised — which is what "reachable through the section chooser but has no end-to-end
/// test" meant.
/// </para>
/// </summary>
public class BulkImportDailyHistoryCustomSectionTests : RepositoryTestBase
{
    private const string SariSari = "Sari Sari";
    private const string Carinderia = "Carinderia";

    /// <summary>A month safely in the past, so "that day has not happened yet" never interferes.</summary>
    private static readonly DateOnly Past = new DateOnly(2026, 3, 1);

    private static readonly DateOnly Today = new DateOnly(2026, 8, 15);

    private static Stall Space(Guid facilityId, string stallNo, MarketSection? section, string? customSection)
    {
        var stall = Stall.Create(facilityId, stallNo, 0m, ApplicableFees.BaseRental,
            section: section, customSectionName: customSection);

        stall.Contracts.Add(Contract.Create(
            stall.Id, $"Lessee of {customSection ?? section?.ToString()} {stallNo}",
            $"Lessee of {customSection ?? section?.ToString()} {stallNo}",
            new DateOnly(2025, 1, 1), durationYears: 3, monthlyRate: 900m));

        return stall;
    }

    private static BulkImportDailyHistoryCommandHandler Handler(AppDbContext context) =>
        new(new StallRepository(context),
            new DailyCollectionRepository(context),
            new PaymentRepository(context),
            new NpmMarketClosureRepository(context),
            new FacilityRepository(context),
            new FeeRateResolver(context),
            new UnitOfWork(context),
            CacheTestDoubles.Invalidator,
            CacheTestDoubles.Tenant,
            new FixedClock(Today.ToDateTime(TimeOnly.MinValue).AddHours(-8)));

    /// <summary>An NPM facility with the same space NUMBER in three different sections.</summary>
    private async Task<(AppDbContext Context, Stall Vegetable, Stall Sari, Stall Carinderia)> SeedThreeSpacesCalledOne()
    {
        var context = NewContext();

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        context.Add(facility);
        await context.SaveChangesAsync();

        var vegetable = Space(facility.Id, "1", MarketSection.VegetableArea, null);
        var sari = Space(facility.Id, "1", null, SariSari);
        var carinderia = Space(facility.Id, "1", null, Carinderia);

        context.AddRange(vegetable, sari, carinderia);
        await context.SaveChangesAsync();

        return (context, vegetable, sari, carinderia);
    }

    [Fact]
    public async Task ARowForACustomSectionSettlesTheSpaceInThatSection()
    {
        var (context, vegetable, sari, carinderia) = await SeedThreeSpacesCalledOne();

        var result = await Handler(context).Handle(
            new BulkImportDailyHistoryCommand(
                FacilityCode.NPM,
                Section: null,
                Rows: new[] { new ImportDailyPaymentRow(1, "1", "Lessee", Past.Year, Past.Month, 4, "OR-3001") },
                CustomSectionName: SariSari),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var settled = await context.DailyCollections.AsNoTracking().ToListAsync();

        // Four market days, all against the Sari Sari space — and nothing at all against the identically numbered
        // spaces in the Vegetable Area or the other custom section.
        Assert.Equal(4, settled.Count(d => d.StallId == sari.Id));
        Assert.DoesNotContain(settled, d => d.StallId == vegetable.Id);
        Assert.DoesNotContain(settled, d => d.StallId == carinderia.Id);
    }

    [Fact]
    public async Task ARowForTheOtherCustomSectionSettlesTheOtherSpace()
    {
        // The mirror of the case above. Asserting only one direction would pass even if the import always chose the
        // first matching number.
        var (context, vegetable, sari, carinderia) = await SeedThreeSpacesCalledOne();

        await Handler(context).Handle(
            new BulkImportDailyHistoryCommand(
                FacilityCode.NPM,
                Section: null,
                Rows: new[] { new ImportDailyPaymentRow(1, "1", "Lessee", Past.Year, Past.Month, 2, "OR-3002") },
                CustomSectionName: Carinderia),
            CancellationToken.None);

        var settled = await context.DailyCollections.AsNoTracking().ToListAsync();

        Assert.Equal(2, settled.Count(d => d.StallId == carinderia.Id));
        Assert.DoesNotContain(settled, d => d.StallId == sari.Id);
        Assert.DoesNotContain(settled, d => d.StallId == vegetable.Id);
    }

    [Fact]
    public async Task ARowForAStandardSectionIsNotSatisfiedByACustomSectionSpace()
    {
        // The same protection in the other direction: choosing the Vegetable Area must not settle the Sari Sari space.
        var (context, vegetable, sari, carinderia) = await SeedThreeSpacesCalledOne();

        await Handler(context).Handle(
            new BulkImportDailyHistoryCommand(
                FacilityCode.NPM,
                Section: MarketSection.VegetableArea,
                Rows: new[] { new ImportDailyPaymentRow(1, "1", "Lessee", Past.Year, Past.Month, 3, "OR-3003") }),
            CancellationToken.None);

        var settled = await context.DailyCollections.AsNoTracking().ToListAsync();

        Assert.Equal(3, settled.Count(d => d.StallId == vegetable.Id));
        Assert.DoesNotContain(settled, d => d.StallId == sari.Id);
        Assert.DoesNotContain(settled, d => d.StallId == carinderia.Id);
    }

    [Fact]
    public async Task TheLookupReturnsOnlyTheNamedSectionsSpaces()
    {
        // Asserted at the repository, deliberately, and not only through the import.
        //
        // Dropping the custom-section filter makes the lookup return BOTH custom sections' spaces, and both are numbered "1".
        // The import then keys them into a dictionary by number, where the second silently overwrites the first — so which
        // lessee's account the money lands on depends on the order rows come back in. One of the import tests above would fail
        // and its mirror would pass, by luck. This test cannot be lucky: it counts what the filter returned.
        var (context, _, sari, _) = await SeedThreeSpacesCalledOne();
        var repository = new StallRepository(context);

        var sariSpaces = await repository.GetStallsWithContractsByFacilityAsync(
            FacilityCode.NPM, section: null, customSectionName: SariSari, CancellationToken.None);

        Assert.Single(sariSpaces);
        Assert.Equal(sari.Id, sariSpaces[0].Id);
    }

    [Fact]
    public async Task ANumberThatExistsOnlyInAnotherSectionIsRejected_NotSilentlyMatched()
    {
        // A clerk who picks the wrong section must be told, not have the money recorded against a space they did not name.
        var (context, _, _, _) = await SeedThreeSpacesCalledOne();

        var result = await Handler(context).Handle(
            new BulkImportDailyHistoryCommand(
                FacilityCode.NPM,
                Section: null,
                Rows: new[] { new ImportDailyPaymentRow(1, "99", "Lessee", Past.Year, Past.Month, 3, "OR-3004") },
                CustomSectionName: SariSari),
            CancellationToken.None);

        Assert.True(result.IsSuccess);   // the batch reports per row rather than failing wholesale
        Assert.Empty(await context.DailyCollections.AsNoTracking().ToListAsync());
        Assert.Equal(1, result.Value!.RejectedCount);
    }
}
