using EEMOCantilanSDS.Application.Command.Stalls.CreateStall;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A market has a fixed number of physical stalls, so when a lessee leaves the office must be able to put the
/// next lessee into the SAME stall — not be pushed into inventing "Stall 24" for a market that has 23. These
/// cover the handover: the stall keeps its number, its section and its whole history, a new contract term begins,
/// the previous lessee's payor links are revoked, and a stall that is still occupied is refused.
/// </summary>
public class CreateStallReassignTests
{
    private static CreateStallCommand Command(bool reuse, string stallNo = "1") => new(
        FacilityCode.NPM,
        stallNo,
        MonthlyRate: 900m,
        Fees: ApplicableFees.DailyRental | ApplicableFees.Electricity,
        Section: MarketSection.MeatSection,
        AreaLocation: null,
        AreaSqm: 4,
        AreaNote: null,
        DailyRate: 30m,
        ActualOccupant: "Teofila Reyes",
        NameOnContract: "Teofila Reyes",
        ContractDate: new DateTime(2026, 7, 30),
        ContractYears: 3,
        CustomSectionName: null,
        ReuseVacatedStall: reuse);

    private static (CreateStallCommandHandler handler, Mock<IStallRepository> stalls, Mock<IPayorRepository> payors, Mock<IUnitOfWork> uow)
        Build(Stall? existing)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");

        var stalls = new Mock<IStallRepository>();
        var facilities = new Mock<IFacilityRepository>();
        var payors = new Mock<IPayorRepository>();
        var uow = new Mock<IUnitOfWork>();

        facilities.Setup(r => r.GetByCodeAsync(FacilityCode.NPM, It.IsAny<CancellationToken>())).ReturnsAsync(facility);
        stalls.Setup(r => r.FindStallByNumberAsync(
                It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new CreateStallCommandHandler(
            stalls.Object, facilities.Object, payors.Object, uow.Object,
            CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

        return (handler, stalls, payors, uow);
    }

    private static Stall VacatedStall(bool closed)
    {
        var stall = Stall.Create(Guid.NewGuid(), "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        // A term that lapsed years ago; kept on the stall as history.
        stall.Contracts.Add(Contract.Create(stall.Id, "Wilma K. Tecson", "Wilma K. Tecson", new DateOnly(2020, 1, 1), 1, 900m));
        if (closed) stall.Close(new DateOnly(2026, 6, 7), "Head");
        return stall;
    }

    [Fact]
    public async Task AVacatedStall_TakesTheNewLessee_KeepingItsNumberAndHistory()
    {
        var existing = VacatedStall(closed: true);
        var previousTerm = existing.Contracts.Single();
        var (handler, stalls, payors, uow) = Build(existing);

        Contract? added = null;
        stalls.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>()))
            .Callback<Contract, CancellationToken>((c, _) => added = c)
            .Returns(Task.CompletedTask);

        var result = await handler.Handle(Command(reuse: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("1", result.Value!.StallNo);                 // same physical number
        Assert.Equal(existing.Id, result.Value.Id);               // same stall record — nothing was duplicated
        Assert.Equal(StallStatus.Active, existing.Status);        // reopened for the new lessee
        Assert.False(previousTerm.IsActive);                      // previous term kept, but ended
        Assert.Contains(previousTerm, existing.Contracts);        // history preserved
        Assert.NotNull(added);
        Assert.Equal("Teofila Reyes", added!.ActualOccupant);
        Assert.True(added.IsActive);

        // The space changed hands: the previous lessee's online links must not carry over.
        payors.Verify(p => p.RemoveStallLinksAsync(existing.Id, It.IsAny<CancellationToken>()), Times.Once);

        // Nothing new was registered as a stall.
        stalls.Verify(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AnExpiredButStillOpenStall_IsAlsoReusable()
    {
        var existing = VacatedStall(closed: false);   // active status, but its only term lapsed in 2021
        var (handler, stalls, _, _) = Build(existing);
        stalls.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await handler.Handle(Command(reuse: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Value!.Id);
    }

    [Fact]
    public async Task AnOccupiedStall_IsRefused_SoTwoLesseesCannotShareOneSpace()
    {
        var occupied = Stall.Create(Guid.NewGuid(), "1", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        occupied.Contracts.Add(Contract.Create(occupied.Id, "Current Lessee", null, PhilippineTime.Today.AddYears(-1), 5, 900m));

        var (handler, stalls, payors, _) = Build(occupied);

        var result = await handler.Handle(Command(reuse: true), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        stalls.Verify(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>()), Times.Never);
        payors.Verify(p => p.RemoveStallLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithoutTheOfficesConfirmation_AStallIsNeverReused()
    {
        // The default path is unchanged: a create is a create. Only an explicit confirmation reassigns.
        var existing = VacatedStall(closed: true);
        var (handler, stalls, _, _) = Build(existing);
        stalls.Setup(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        stalls.Setup(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await handler.Handle(Command(reuse: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(existing.Id, result.Value!.Id);           // a genuinely new stall record
        stalls.Verify(r => r.AddAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
