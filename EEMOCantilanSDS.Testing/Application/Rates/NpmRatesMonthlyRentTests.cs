using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Queries.Stalls.GetNpmRates;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// What the portal needs to ask an office to confirm its ordinance month: the rent in force, and whether the LGU has
/// actually stated it. Until it has, the system charges thirty of the daily fee — right where the ordinance follows
/// that convention, and quietly wrong where it does not, which is precisely why the office is asked.
///
/// <para>The reference tenant is exempt, but only on POSITIVE proof — its own municipality row, marked default. A
/// request that cannot prove it is asked, because asking is the safe direction.</para>
/// </summary>
public class NpmRatesMonthlyRentTests : RepositoryTestBase
{
    private static ICurrentUserService CallerOf(Guid? municipalityId)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(u => u.MunicipalityId).Returns(municipalityId);
        return user.Object;
    }

    private static Municipality Lgu(string name, string code, bool isDefault) =>
        Municipality.Create(
            code: code,
            name: name,
            province: "Surigao del Sur",
            status: MunicipalityStatus.Active,
            tenantCode: code,
            isDefault: isDefault);

    [Fact]
    public async Task WithNoStatedMonthlyRent_TheRentInUseIsThirtyInstallments_AndTheOfficeIsAsked()
    {
        var context = NewContext();
        var lgu = Lgu("Madrid", "madrid-sds", isDefault: false);
        context.AddRange(lgu, Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));
        await context.SaveChangesAsync();

        var rates = (await new GetNpmRatesQueryHandler(new FeeRateResolver(context), CallerOf(lgu.Id), context, new FixedClock(DateTime.UtcNow))
            .Handle(new GetNpmRatesQuery(), CancellationToken.None)).Value!;

        Assert.Equal(FeeRates.NpmDailyFee, rates.DailyRate);
        Assert.Equal(0m, rates.MonthlyRent);                                                  // none stated
        Assert.False(rates.IsMonthlyRentConfirmed);
        Assert.True(rates.NeedsMonthlyRentConfirmation);                                       // so it is asked
        Assert.Equal(FeeRates.NpmDailyFee * DomainRules.DailyBilledMonthDays, rates.MonthlyRentInUse);
    }

    [Fact]
    public async Task TheReferenceTenant_IsNeverAsked()
    {
        // Cantilan's ordinance IS the constants this platform derives from, so thirty of its daily fee is already the
        // figure on its own paper: ₱30 × 30 = ₱900. There is nothing for it to confirm.
        var context = NewContext();
        var cantilan = Lgu("Cantilan", "cantilan-sds", isDefault: true);
        context.AddRange(cantilan, Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));
        await context.SaveChangesAsync();

        var rates = (await new GetNpmRatesQueryHandler(new FeeRateResolver(context), CallerOf(cantilan.Id), context, new FixedClock(DateTime.UtcNow))
            .Handle(new GetNpmRatesQuery(), CancellationToken.None)).Value!;

        Assert.False(rates.NeedsMonthlyRentConfirmation);
        Assert.False(rates.IsMonthlyRentConfirmed);       // still derived — the figures are unchanged
        Assert.Equal(900m, rates.MonthlyRentInUse);
    }

    [Fact]
    public async Task ARequestThatCannotProveItsMunicipality_IsAsked()
    {
        // A request carrying no municipality claim resolves to the default tenant CODE by a platform-wide fallback,
        // so a code comparison exempted it — the question then went unasked for the very LGU that needed it. Nothing
        // is exempt without its own row saying so.
        var context = NewContext();
        context.AddRange(
            Lgu("Cantilan", "cantilan-sds", isDefault: true),
            Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));
        await context.SaveChangesAsync();

        var rates = (await new GetNpmRatesQueryHandler(new FeeRateResolver(context), CallerOf(null), context, new FixedClock(DateTime.UtcNow))
            .Handle(new GetNpmRatesQuery(), CancellationToken.None)).Value!;

        Assert.True(rates.NeedsMonthlyRentConfirmation);
    }

    [Fact]
    public async Task OnceTheLguStatesItsMonthlyRent_ItIsConfirmed_AndInUse()
    {
        var context = NewContext();
        var lgu = Lgu("Madrid", "madrid-sds", isDefault: false);
        context.AddRange(lgu, Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));
        context.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 35m, new DateOnly(2020, 1, 1), Guid.Empty));
        context.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 1_000m, new DateOnly(2020, 1, 1), Guid.Empty));
        await context.SaveChangesAsync();

        var rates = (await new GetNpmRatesQueryHandler(new FeeRateResolver(context), CallerOf(lgu.Id), context, new FixedClock(DateTime.UtcNow))
            .Handle(new GetNpmRatesQuery(), CancellationToken.None)).Value!;

        Assert.Equal(35m, rates.DailyRate);
        Assert.Equal(1_000m, rates.MonthlyRent);
        Assert.Equal(1_000m, rates.MonthlyRentInUse);      // not the ₱1,050 thirty installments would make
        Assert.True(rates.IsMonthlyRentConfirmed);
        Assert.False(rates.NeedsMonthlyRentConfirmation);   // so the question never returns
    }

    [Fact]
    public async Task ConfirmingTheFigureInUse_EndsTheQuestion_WithoutChangingTheMoney()
    {
        // What the dialog's "Confirm ₱900" does: it writes the figure already in force as the LGU's own stated rent.
        // The office is no longer asked, and not a peso moves.
        var context = NewContext();
        var lgu = Lgu("Madrid", "madrid-sds", isDefault: false);
        context.AddRange(lgu, Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));
        await context.SaveChangesAsync();

        var handler = new GetNpmRatesQueryHandler(new FeeRateResolver(context), CallerOf(lgu.Id), context, new FixedClock(DateTime.UtcNow));
        var before = (await handler.Handle(new GetNpmRatesQuery(), CancellationToken.None)).Value!;

        context.Add(FacilityRate.Create(
            FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, before.MonthlyRentInUse, new DateOnly(2020, 1, 1), Guid.Empty));
        await context.SaveChangesAsync();

        var after = (await handler.Handle(new GetNpmRatesQuery(), CancellationToken.None)).Value!;

        Assert.True(after.IsMonthlyRentConfirmed);
        Assert.False(after.NeedsMonthlyRentConfirmation);
        Assert.Equal(before.MonthlyRentInUse, after.MonthlyRentInUse);
    }
}
