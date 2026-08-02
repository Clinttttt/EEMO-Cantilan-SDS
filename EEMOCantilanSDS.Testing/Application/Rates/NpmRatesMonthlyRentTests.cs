using EEMOCantilanSDS.Application.Queries.Stalls.GetNpmRates;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// What the portal needs to ask an office to confirm its ordinance month: the rent in force, and whether the LGU has
/// actually stated it. Until it has, the system charges thirty of the daily fee — right where the ordinance follows
/// that convention, and quietly wrong where it does not, which is precisely why the office is asked.
/// </summary>
public class NpmRatesMonthlyRentTests : RepositoryTestBase
{
    [Fact]
    public async Task WithNoStatedMonthlyRent_TheRentInUseIsThirtyInstallments_AndIsUnconfirmed()
    {
        var context = NewContext();
        context.Add(Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));
        await context.SaveChangesAsync();

        var result = await new GetNpmRatesQueryHandler(new FeeRateResolver(context))
            .Handle(new GetNpmRatesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var rates = result.Value!;
        Assert.Equal(FeeRates.NpmDailyFee, rates.DailyRate);
        Assert.Equal(0m, rates.MonthlyRent);                                                  // none stated
        Assert.False(rates.IsMonthlyRentConfirmed);                                            // so the office is asked
        Assert.Equal(FeeRates.NpmDailyFee * DomainRules.DailyBilledMonthDays, rates.MonthlyRentInUse);
    }

    [Fact]
    public async Task OnceTheLguStatesItsMonthlyRent_ItIsConfirmed_AndInUse()
    {
        var context = NewContext();
        context.Add(Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));
        context.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m, new DateOnly(2020, 1, 1), Guid.Empty));
        context.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 1_000m, new DateOnly(2020, 1, 1), Guid.Empty));
        await context.SaveChangesAsync();

        var result = await new GetNpmRatesQueryHandler(new FeeRateResolver(context))
            .Handle(new GetNpmRatesQuery(), CancellationToken.None);

        var rates = result.Value!;
        Assert.Equal(35m, rates.DailyRate);
        Assert.Equal(1_000m, rates.MonthlyRent);
        Assert.Equal(1_000m, rates.MonthlyRentInUse);      // not the ₱1,050 thirty installments would make
        Assert.True(rates.IsMonthlyRentConfirmed);          // so the reminder never shows again
    }

    [Fact]
    public async Task ConfirmingTheFigureInUse_EndsTheReminder_WithoutChangingTheMoney()
    {
        // What the reminder's "Confirm ₱900" button does: it writes the figure already in force as the LGU's own
        // stated rent. The office is no longer asked, and not a peso moves.
        var context = NewContext();
        context.Add(Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));
        await context.SaveChangesAsync();

        var handler = new GetNpmRatesQueryHandler(new FeeRateResolver(context));
        var before = (await handler.Handle(new GetNpmRatesQuery(), CancellationToken.None)).Value!;

        context.Add(FacilityRate.Create(
            FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, before.MonthlyRentInUse, new DateOnly(2020, 1, 1), Guid.Empty));
        await context.SaveChangesAsync();

        var after = (await handler.Handle(new GetNpmRatesQuery(), CancellationToken.None)).Value!;

        Assert.True(after.IsMonthlyRentConfirmed);
        Assert.Equal(before.MonthlyRentInUse, after.MonthlyRentInUse);
    }
}
