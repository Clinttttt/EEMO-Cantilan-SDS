using EEMOCantilanSDS.Application.Command.Facilities.SetNpmMonthBasis;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Testing.Support;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Facilities;

/// <summary>
/// Stating the office's basis for a market month.
///
/// <para>
/// The office's own decision, written on its own market's row. What these hold is that it is a STATE and not a re-pricing:
/// the rule answers what a month owes when it is asked, so a period the office has already worked keeps the figures it was
/// worked at, and only the next question is answered differently.
/// </para>
/// </summary>
public class NpmMonthBasisHandlerTests
{
    private static (SetNpmMonthBasisCommandHandler handler, Facility npm, Mock<IUnitOfWork> uow) Build(
        NpmMonthBasis startingBasis = NpmMonthBasis.RentGoal, bool marketExists = true)
    {
        var npm = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        if (startingBasis != NpmMonthBasis.RentGoal) npm.SetMonthBasis(startingBasis);

        var facilityRepo = new Mock<IFacilityRepository>();
        facilityRepo.Setup(r => r.GetByCodeAsync(FacilityCode.NPM, It.IsAny<CancellationToken>()))
            .ReturnsAsync(marketExists ? npm : null);

        var uow = new Mock<IUnitOfWork>();

        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantCode).Returns("cantilan");

        var handler = new SetNpmMonthBasisCommandHandler(
            facilityRepo.Object, uow.Object, CacheTestDoubles.Invalidator, tenant.Object,
            new FixedClock(new DateTime(2026, 8, 31)));

        return (handler, npm, uow);
    }

    [Fact]
    public async Task AnOfficeMayStateThePureDaysBasis()
    {
        var (handler, npm, uow) = Build();

        var result = await handler.Handle(new SetNpmMonthBasisCommand(NpmMonthBasis.PureDays), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(NpmMonthBasis.PureDays, npm.MonthBasis);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnOfficeMayGoBackToTheMonthlyGoal()
    {
        // Reversible on purpose: an office that tries a basis and finds its paper says otherwise must not need a developer.
        var (handler, npm, _) = Build(startingBasis: NpmMonthBasis.PureDays);

        var result = await handler.Handle(new SetNpmMonthBasisCommand(NpmMonthBasis.RentGoal), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(NpmMonthBasis.RentGoal, npm.MonthBasis);
    }

    [Fact]
    public async Task ConfirmingTheBasisAlreadyInForceIsStillRecorded()
    {
        // Changed deliberately from an earlier version of this test, which asserted the row was left untouched.
        // Confirming the rule in force IS a decision the office took, and recording it is what stops the console asking
        // the same question on every visit. A confirmation that changed nothing would leave the office answering for ever.
        var (handler, npm, uow) = Build();
        Assert.Null(npm.MonthBasisStatedAt);

        var result = await handler.Handle(new SetNpmMonthBasisCommand(NpmMonthBasis.RentGoal), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(NpmMonthBasis.RentGoal, npm.MonthBasis);
        Assert.NotNull(npm.MonthBasisStatedAt);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnOfficeThatHasNeverBeenAskedIsDistinguishableFromOneThatChoseTheDefault()
    {
        // Both read as RentGoal, which is why the statement is recorded separately. Without it the console could not tell
        // "chose the monthly goal" from "has never been asked", and would either nag an office that had answered or never
        // ask one that had not.
        var (handler, npm, _) = Build();
        Assert.Equal(NpmMonthBasis.RentGoal, npm.MonthBasis);
        Assert.Null(npm.MonthBasisStatedAt);

        await handler.Handle(new SetNpmMonthBasisCommand(NpmMonthBasis.RentGoal), default);

        Assert.Equal(NpmMonthBasis.RentGoal, npm.MonthBasis);
        Assert.NotNull(npm.MonthBasisStatedAt);
    }

    [Fact]
    public async Task ABasisThisPlatformDoesNotMeasureByIsRefused()
    {
        // A figure arriving from outside the console must not become a basis nobody implemented.
        var (handler, npm, uow) = Build();

        var result = await handler.Handle(new SetNpmMonthBasisCommand((NpmMonthBasis)99), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(NpmMonthBasis.RentGoal, npm.MonthBasis);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnOfficeWithNoMarketHasNoMarketMonthToMeasure()
    {
        var (handler, _, uow) = Build(marketExists: false);

        var result = await handler.Handle(new SetNpmMonthBasisCommand(NpmMonthBasis.PureDays), default);

        Assert.False(result.IsSuccess);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
