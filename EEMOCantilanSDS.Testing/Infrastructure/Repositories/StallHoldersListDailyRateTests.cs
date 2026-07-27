using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The "List of Stallholders" money columns for a DAILY-collected facility (NPM).
///
/// Regression: the roster printed the stored <c>Stall.MonthlyRate</c> — a figure typed by whoever
/// registered the stall — so every municipality was shown Cantilan's ₱900/month even when its own
/// ordinance rate was ₱40/day (₱1,200). A daily-collected stall has no monthly contract rate: the official
/// form states the monthly EQUIVALENT of the daily fee over a flat 30-day month, and it must be derived
/// from the tenant's resolved rate through the same <see cref="Stall.ResolveDailyFee"/> rule that billing
/// and settlement use, so the roster can never disagree with the ledger.
/// </summary>
public class StallHoldersListDailyRateTests : RepositoryTestBase
{
    private static readonly DateOnly RateEffective = new(2020, 1, 1);

    private static (Facility Facility, Stall Stall, Contract Contract) NpmStall(
        string stallNo = "1",
        decimal storedMonthlyRate = 900m,
        MarketSection? section = MarketSection.VegetableArea,
        string? customSectionName = null,
        decimal? storedDailyRate = null)
    {
        var facility = Facility.Create(FacilityCode.NPM, "Public Market", "NPM");
        var stall = Stall.Create(
            facility.Id, stallNo, storedMonthlyRate, ApplicableFees.DailyRental,
            section: section, dailyRate: storedDailyRate, customSectionName: customSectionName);
        var contract = Contract.Create(
            stall.Id, "Diego Brando", "Diego Brando", PhilippineTime.Today.AddMonths(-1), 3, storedMonthlyRate);
        return (facility, stall, contract);
    }

    [Fact]
    public async Task Npm_DerivesMonthlyFromTheTenantsDailyRate_NotTheStoredMonthlyRate()
    {
        // A ₱40/day municipality whose stall still carries the ₱900 that was typed at registration.
        var context = NewContext();
        var (facility, stall, contract) = NpmStall(storedMonthlyRate: 900m);
        var rate = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, RateEffective, Guid.Empty);

        context.AddRange(facility, stall, contract, rate);
        await context.SaveChangesAsync();

        var dto = await new StallRepository(context)
            .GetStallHoldersListAsync(FacilityCode.NPM, null, null, CancellationToken.None);

        var row = Assert.Single(Assert.Single(dto.Sections).Rows);
        Assert.Equal(1_200m, row.MonthlyRentalRate);      // ₱40 × 30 — NOT the stored ₱900
        Assert.Equal(1_200m, row.ActualMonthlyRental);
        Assert.Equal(14_400m, row.WholeYearRental);       // ₱1,200 × 12

        // Section and grand totals must move with the rows, or the document contradicts itself.
        Assert.Equal(1_200m, dto.Sections[0].SectionMonthlyTotal);
        Assert.Equal(1_200m, dto.Sections[0].SectionActualMonthly);
        Assert.Equal(14_400m, dto.Sections[0].SectionWholeYearTotal);
        Assert.Equal(1_200m, dto.GrandTotalMonthlyRate);
        Assert.Equal(14_400m, dto.GrandTotalWholeYearRental);
    }

    [Fact]
    public async Task Npm_WithNoTenantRate_KeepsTheOrdinanceFigures_SoCantilanIsUnchanged()
    {
        // No FacilityRate rows → the resolver falls back to the ₱30 ordinance, which is Cantilan's case.
        // ₱30 × 30 = ₱900 and ₱10,800 a year: byte-for-byte what the roster showed before this change.
        var context = NewContext();
        var (facility, stall, contract) = NpmStall(storedMonthlyRate: 900m);

        context.AddRange(facility, stall, contract);
        await context.SaveChangesAsync();

        var dto = await new StallRepository(context)
            .GetStallHoldersListAsync(FacilityCode.NPM, null, null, CancellationToken.None);

        var row = Assert.Single(Assert.Single(dto.Sections).Rows);
        Assert.Equal(FeeRates.NpmDailyFee * DomainRules.DailyBilledMonthDays, row.MonthlyRentalRate);
        Assert.Equal(900m, row.MonthlyRentalRate);
        Assert.Equal(10_800m, row.WholeYearRental);
        Assert.Equal(900m, dto.GrandTotalMonthlyRate);
        Assert.Equal(10_800m, dto.GrandTotalWholeYearRental);
    }

    [Fact]
    public async Task Npm_StoredMonthlyRate_IsIgnoredEvenWhenItIsWildlyWrong()
    {
        // Proves the figure is derived rather than read: a nonsense stored rate cannot reach the roster.
        var context = NewContext();
        var (facility, stall, contract) = NpmStall(storedMonthlyRate: 12_345m);
        var rate = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, RateEffective, Guid.Empty);

        context.AddRange(facility, stall, contract, rate);
        await context.SaveChangesAsync();

        var dto = await new StallRepository(context)
            .GetStallHoldersListAsync(FacilityCode.NPM, null, null, CancellationToken.None);

        Assert.Equal(1_200m, Assert.Single(Assert.Single(dto.Sections).Rows).MonthlyRentalRate);
    }

    [Fact]
    public async Task Npm_CustomSection_UsesItsOwnDailyRate_LikeBillingDoes()
    {
        // A per-LGU custom section bills its own rate via Stall.ResolveDailyFee, so the roster must too:
        // ₱25 × 30 = ₱750, not the tenant's ₱40 ordinance rate and not the stored monthly figure.
        var context = NewContext();
        var (facility, stall, contract) = NpmStall(
            storedMonthlyRate: 900m, section: null, customSectionName: "Sari-sari Area", storedDailyRate: 25m);
        var rate = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, RateEffective, Guid.Empty);

        context.AddRange(facility, stall, contract, rate);
        await context.SaveChangesAsync();

        var dto = await new StallRepository(context)
            .GetStallHoldersListAsync(FacilityCode.NPM, null, null, CancellationToken.None);

        var section = Assert.Single(dto.Sections);
        Assert.Equal("Sari-sari Area", section.SectionName);
        Assert.Equal(750m, Assert.Single(section.Rows).MonthlyRentalRate);
        Assert.Equal(9_000m, Assert.Single(section.Rows).WholeYearRental);
        Assert.Equal(750m, dto.GrandTotalMonthlyRate);
    }

    [Fact]
    public async Task MonthlyBilledFacility_StillUsesItsContractedMonthlyRate()
    {
        // Guard for the other seven facilities: they DO have a monthly contract rate, and an NPM daily rate
        // in the same tenant must not leak into their roster.
        var context = NewContext();
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC");
        var stall = Stall.Create(facility.Id, "1", 2_760m, ApplicableFees.BaseRental);
        var contract = Contract.Create(
            stall.Id, "Joseph Quinones", "Joseph Quinones", PhilippineTime.Today.AddMonths(-1), 3, 2_760m);
        var npmRate = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40m, RateEffective, Guid.Empty);

        context.AddRange(facility, stall, contract, npmRate);
        await context.SaveChangesAsync();

        var dto = await new StallRepository(context)
            .GetStallHoldersListAsync(FacilityCode.TCC, null, null, CancellationToken.None);

        var row = Assert.Single(Assert.Single(dto.Sections).Rows);
        Assert.Equal(2_760m, row.MonthlyRentalRate);
        Assert.Equal(33_120m, row.WholeYearRental);
        Assert.Equal(2_760m, dto.GrandTotalMonthlyRate);
    }
}
