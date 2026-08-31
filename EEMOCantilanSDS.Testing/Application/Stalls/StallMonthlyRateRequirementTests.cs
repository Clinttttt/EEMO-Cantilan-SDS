using EEMOCantilanSDS.Application.Command.Stalls.CreateStall;
using EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Testing.Support;
using Moq;

namespace EEMOCantilanSDS.Testing.Application.Stalls;

/// <summary>
/// Whether a stall has to state a MONTHLY figure.
///
/// <para>
/// FOUND BY AUDIT, and it was a blocker of my own making. Hiding the monthly field from the vendor form for an office that
/// measures its market month by the days that month has left both stall validators still demanding a figure greater than
/// nought - so that office could choose the rule and then be unable to record or edit a single market stall, refused for a
/// number the screen no longer offered it.
/// </para>
///
/// <para>
/// The requirement now applies where the office HAS a monthly rent: every facility other than the market, and the market
/// itself while it is on the monthly goal. Both halves are asserted, because the second is the one every live office
/// depends on.
/// </para>
/// </summary>
public class StallMonthlyRateRequirementTests
{
    private static CreateStallCommand CreateOf(FacilityCode code, decimal monthlyRate) => new(
        FacilityCode: code,
        StallNo: "1",
        MonthlyRate: monthlyRate,
        Fees: ApplicableFees.DailyRental,
        Section: MarketSection.VegetableArea,
        AreaLocation: null,
        AreaSqm: 4.8,
        AreaNote: null,
        DailyRate: 30m,
        ActualOccupant: "Juan Dela Cruz",
        NameOnContract: "Juan Dela Cruz",
        ContractDate: new DateTime(2026, 1, 1),
        ContractYears: 3);

    private static IStallRepository UniqueStallRepo()
    {
        var repo = new Mock<IStallRepository>();
        repo.Setup(r => r.IsStallNoUniqueAsync(
                It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return repo.Object;
    }

    private static CreateStallCommandValidator CreateValidator(bool onPureDays) =>
        new(UniqueStallRepo(),
            new FixedClock(new DateTime(2026, 8, 31)),
            onPureDays ? CacheTestDoubles.PureDaysFeeRateResolver : CacheTestDoubles.FeeRateResolver);

    [Fact]
    public async Task AMarketStallOnThePureDaysBasisNeedsNoMonthlyFigure()
    {
        var result = await CreateValidator(onPureDays: true).ValidateAsync(CreateOf(FacilityCode.NPM, 0m));

        Assert.DoesNotContain(result.Errors, e => e.ErrorMessage.Contains("Monthly rate"));
    }

    [Fact]
    public async Task AMarketStallOnTheMonthlyGoalStillNeedsOne()
    {
        var result = await CreateValidator(onPureDays: false).ValidateAsync(CreateOf(FacilityCode.NPM, 0m));

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Monthly rate must be greater than"));
    }

    [Fact]
    public async Task AMonthlyRentalStallAlwaysNeedsOne()
    {
        // An iceplant's rent falls due by the month. How a MARKET month is measured has nothing to say about it, so the
        // requirement stands even for an office whose market is on the days basis.
        var result = await CreateValidator(onPureDays: true).ValidateAsync(CreateOf(FacilityCode.ICE, 0m));

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Monthly rate must be greater than"));
    }

    [Fact]
    public async Task AStatedFigureIsAcceptedOnEitherBasis()
    {
        Assert.DoesNotContain(
            (await CreateValidator(onPureDays: true).ValidateAsync(CreateOf(FacilityCode.NPM, 900m))).Errors,
            e => e.ErrorMessage.Contains("Monthly rate"));

        Assert.DoesNotContain(
            (await CreateValidator(onPureDays: false).ValidateAsync(CreateOf(FacilityCode.NPM, 900m))).Errors,
            e => e.ErrorMessage.Contains("Monthly rate"));
    }

    // ── Editing an existing stall ────────────────────────────────────────────────────────────────────────────────────

    private static UpdateStallCommand UpdateOf(Guid stallId, decimal monthlyRate) => new(
        StallId: stallId,
        MonthlyRate: monthlyRate,
        Fees: null,
        AreaSqm: null,
        AreaNote: null,
        DailyRate: null,
        ActualOccupant: "Juan Dela Cruz",
        NameOnContract: "Juan Dela Cruz",
        Remarks: null,
        ContractDate: null,
        ContractYears: 3);

    private static UpdateStallCommandValidator UpdateValidator(FacilityCode facilityOfStall, bool onPureDays, Guid stallId)
    {
        var facility = Facility.Create(facilityOfStall, facilityOfStall.ToString(), facilityOfStall.ToString());
        var stall = Stall.Create(facility.Id, "1", 0m, ApplicableFees.DailyRental, section: MarketSection.VegetableArea);
        typeof(Stall).GetProperty(nameof(Stall.Id))!.SetValue(stall, stallId);
        typeof(Stall).GetProperty(nameof(Stall.Facility))!.SetValue(stall, facility);

        var repo = new Mock<IStallRepository>();
        repo.Setup(r => r.GetByIdAsync(stallId, It.IsAny<CancellationToken>())).ReturnsAsync(stall);

        return new(repo.Object, onPureDays ? CacheTestDoubles.PureDaysFeeRateResolver : CacheTestDoubles.FeeRateResolver);
    }

    [Fact]
    public async Task EditingAMarketStallOnThePureDaysBasisNeedsNoMonthlyFigure()
    {
        var id = Guid.NewGuid();

        var result = await UpdateValidator(FacilityCode.NPM, onPureDays: true, id).ValidateAsync(UpdateOf(id, 0m));

        Assert.DoesNotContain(result.Errors, e => e.ErrorMessage.Contains("Monthly rate"));
    }

    [Fact]
    public async Task EditingAMarketStallOnTheMonthlyGoalStillNeedsOne()
    {
        var id = Guid.NewGuid();

        var result = await UpdateValidator(FacilityCode.NPM, onPureDays: false, id).ValidateAsync(UpdateOf(id, 0m));

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Monthly rate must be greater than"));
    }

    [Fact]
    public async Task EditingAMonthlyRentalStallAlwaysNeedsOne()
    {
        var id = Guid.NewGuid();

        var result = await UpdateValidator(FacilityCode.ICE, onPureDays: true, id).ValidateAsync(UpdateOf(id, 0m));

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Monthly rate must be greater than"));
    }

    [Fact]
    public async Task AStallThatCannotBeFoundIsNotExcused()
    {
        // A stall id that answers nothing must not become a way past the requirement. Refusing is the safe direction.
        var repo = new Mock<IStallRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Stall?)null);

        var result = await new UpdateStallCommandValidator(repo.Object, CacheTestDoubles.PureDaysFeeRateResolver)
            .ValidateAsync(UpdateOf(Guid.NewGuid(), 0m));

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Monthly rate must be greater than"));
    }
}
