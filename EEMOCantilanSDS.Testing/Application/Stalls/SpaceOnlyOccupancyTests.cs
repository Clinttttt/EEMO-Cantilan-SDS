using EEMOCantilanSDS.Application.Command.Stalls.CreateStall;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Spaces the office lets without a signed contract. The head office's own registers show the barbecue stand and the
/// ice plant entirely so, and part of the commercial centre too: the leasee column reads "No contract (space only)"
/// — or "(Extension TCC)" — and every contract-derived column is blank, while the rent charged is stated as usual.
///
/// <para>What these pin down: rent is assessed exactly as for a contract; nothing about such an occupancy falls due
/// for renewal, because there is no term to run out; and no name, term or contract rate is kept for it, so none can
/// find its way onto a sheet that must say there is no contract.</para>
/// </summary>
public class SpaceOnlyOccupancyTests
{
    private static Contract SpaceOnly(Guid stallId, decimal monthlyRent = 1_600m) =>
        Contract.Create(
            stallId, "Joy Ruaza", nameOnContract: "Joy Ruaza",
            new DateOnly(2024, 1, 1), durationYears: 0, monthlyRate: monthlyRent,
            arrangement: OccupancyArrangement.SpaceOnly);

    [Fact]
    public void ASpaceOnlyOccupancy_KeepsNoNameOnAContractThatDoesNotExist()
    {
        // Whatever the form was carrying, the record must not hold a name for the "per signed contract" column.
        var contract = SpaceOnly(Guid.NewGuid());

        Assert.Null(contract.NameOnContract);
        Assert.False(contract.HasSignedContract);
        Assert.Equal(OccupancyArrangement.SpaceOnly, contract.Arrangement);
    }

    [Fact]
    public void ASpaceOnlyOccupancy_IsOpenEnded_SoItNeverFallsDueForRenewal()
    {
        var contract = SpaceOnly(Guid.NewGuid());

        Assert.False(contract.IsExpired);
        Assert.False(contract.IsExpiringSoon);
        Assert.Equal(DomainRules.OpenEndedTermYears, contract.DurationYears);
    }

    [Fact]
    public void ASpaceOnlyOccupancy_IsBilledLikeAnyOther()
    {
        // The rent is the whole point of the record: ₱1,600 a month, ₱19,200 for the year, exactly as the office's
        // sheet states, and collectable on any day of the occupancy.
        var contract = SpaceOnly(Guid.NewGuid());

        Assert.Equal(1_600m, contract.MonthlyRentalRate);
        Assert.Equal(19_200m, contract.WholeYearRental);
        Assert.True(contract.IsCollectableOn(new DateOnly(2026, 7, 31)));
        Assert.False(contract.IsCollectableOn(new DateOnly(2023, 12, 31)));   // before they moved in
    }

    [Fact]
    public void AnEndedSpaceOnlyOccupancy_StillReadsAsOneOccupancyWithItsOwnWindow()
    {
        // The occupancy machinery must treat it like any other: a dated end closes the window, and the money owed
        // stops there rather than running on to a term that does not exist.
        var stall = Stall.Create(Guid.NewGuid(), "1", 1_600m, ApplicableFees.BaseRental);
        var contract = SpaceOnly(stall.Id);
        contract.Terminate("Head", new DateOnly(2026, 6, 30));
        stall.Contracts.Add(contract);

        var occupancy = Assert.Single(stall.Occupancies(new DateOnly(2026, 7, 31)));

        Assert.Equal(new DateOnly(2024, 1, 1), occupancy.Start);
        Assert.Equal(new DateOnly(2026, 6, 30), occupancy.End);
        Assert.Equal(new DateOnly(2026, 6, 30), occupancy.BillableEnd);
    }

    [Fact]
    public async Task RegisteringASpaceOnlyStall_RecordsTheArrangementAndNoContractName()
    {
        var facility = Facility.Create(FacilityCode.BBQ, "Barbecue Stand", "BBQ");

        var stalls = new Mock<IStallRepository>();
        var facilities = new Mock<IFacilityRepository>();
        var payors = new Mock<IPayorRepository>();
        var uow = new Mock<IUnitOfWork>();

        facilities.Setup(r => r.GetByCodeAsync(FacilityCode.BBQ, It.IsAny<CancellationToken>())).ReturnsAsync(facility);

        Contract? saved = null;
        stalls.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>()))
            .Callback<Contract, CancellationToken>((c, _) => saved = c)
            .Returns(Task.CompletedTask);

        var handler = new CreateStallCommandHandler(
            stalls.Object, facilities.Object, payors.Object, uow.Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant, new FixedClock(DateTime.UtcNow));

        var result = await handler.Handle(new CreateStallCommand(
            FacilityCode.BBQ, "1", 1_600m, ApplicableFees.BaseRental,
            Section: null, AreaLocation: null, AreaSqm: null, AreaNote: null, DailyRate: null,
            ActualOccupant: "Joy Ruaza",
            // A clerk may well have typed a name before choosing the basis; it must not be kept.
            NameOnContract: "Joy Ruaza",
            ContractDate: new DateTime(2024, 1, 1),
            ContractYears: 0,
            Arrangement: OccupancyArrangement.SpaceOnly), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.Equal(OccupancyArrangement.SpaceOnly, saved!.Arrangement);
        Assert.Null(saved.NameOnContract);
        Assert.Equal(1_600m, saved.MonthlyRentalRate);
        Assert.False(saved.IsExpired);
    }

    [Fact]
    public void ASignedContract_IsUnaffected()
    {
        // The default path must behave exactly as it did: a real term, a name, and an expiry that can lapse.
        var contract = Contract.Create(
            Guid.NewGuid(), "Myra Pude", "Myra Pude", new DateOnly(2023, 6, 7), 3, 2_400m);

        Assert.True(contract.HasSignedContract);
        Assert.Equal("Myra Pude", contract.NameOnContract);
        Assert.Equal(3, contract.DurationYears);
        Assert.Equal(new DateOnly(2026, 6, 7), contract.ExpiryDate);
    }
}
