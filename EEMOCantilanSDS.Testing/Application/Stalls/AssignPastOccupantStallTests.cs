using EEMOCantilanSDS.Application.Command.Stalls.AssignPastOccupantStall;
using EEMOCantilanSDS.Application.Command.Stalls.CreateStall;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Stalls;
using EEMOCantilanSDS.Application.Queries.Stalls.GetStallReassignmentPreview;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A lessee whose occupancy ended cannot be renewed in place once the stall has been let to somebody else — the
/// space belongs to the sitting lessee. When such a payor returns, the office places them in a stall of their own.
///
/// What these pin down: the new stall inherits the old one's facility, section and fee shape (so no municipality's
/// vocabulary is assumed), the number offered is one past the highest in that section, the previous stall and its
/// outstanding balance are never written to, and a daily-billed stall that lacks a daily rate still registers.
/// </summary>
public class AssignPastOccupantStallTests
{
    private static readonly Guid FacilityId = Guid.NewGuid();

    private static Stall PastStall(
        decimal? dailyRate = 30m,
        MarketSection? section = MarketSection.MeatSection,
        string? customSection = null,
        int durationYears = 3)
    {
        var stall = Stall.Create(
            FacilityId, "3", 900m,
            ApplicableFees.BaseRental | ApplicableFees.Electricity,
            section: section,
            areaSqm: 4,
            dailyRate: dailyRate,
            customSectionName: customSection);

        // The term that ended. On a re-let stall the register's past-occupancy row carries this stall's id.
        stall.Contracts.Add(Contract.Create(
            stall.Id, "Ramil C. Orjeles", "Ramil C. Orjeles", new DateOnly(2023, 6, 1), durationYears, 900m));

        return stall;
    }

    private static (AssignPastOccupantStallCommandHandler handler, Mock<ISender> sender, Mock<IStallRepository> stalls)
        BuildCommand(Stall past)
    {
        var stalls = new Mock<IStallRepository>();
        var sender = new Mock<ISender>();

        stalls.Setup(r => r.GetByIdWithContractsAsync(past.Id, It.IsAny<CancellationToken>())).ReturnsAsync(past);
        stalls.Setup(r => r.GetFacilityCodeByStallIdAsync(past.Id, It.IsAny<CancellationToken>())).ReturnsAsync(FacilityCode.NPM);

        sender.Setup(s => s.Send(It.IsAny<CreateStallCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StallDto>.Success(new StallDto(
                Guid.NewGuid(), "24", StallStatus.Active, "Ramil C. Orjeles", "Ramil C. Orjeles",
                4, new DateTime(2026, 7, 31), 900m, 30m, null, MarketSection.MeatSection, null, null, null)));

        return (new AssignPastOccupantStallCommandHandler(stalls.Object, sender.Object), sender, stalls);
    }

    private static AssignPastOccupantStallCommand Command(Guid previousStallId, string stallNo = "24") => new(
        previousStallId, stallNo, new DateTime(2026, 7, 31), 3, 900m, null);

    [Fact]
    public async Task TheNewStall_InheritsTheFacilitySectionAndFeeShapeOfTheOldOne()
    {
        var past = PastStall();
        var (handler, sender, _) = BuildCommand(past);

        CreateStallCommand? sent = null;
        sender.Setup(s => s.Send(It.IsAny<CreateStallCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<StallDto>>, CancellationToken>((c, _) => sent = (CreateStallCommand)c)
            .ReturnsAsync(Result<StallDto>.Success(new StallDto(
                Guid.NewGuid(), "24", StallStatus.Active, "Ramil C. Orjeles", null,
                null, null, 900m, 30m, null, MarketSection.MeatSection, null, null, null)));

        var result = await handler.Handle(Command(past.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(sent);
        Assert.Equal(FacilityCode.NPM, sent!.FacilityCode);
        Assert.Equal(MarketSection.MeatSection, sent.Section);
        Assert.Equal(ApplicableFees.BaseRental | ApplicableFees.Electricity, sent.Fees);
        Assert.Equal(30m, sent.DailyRate);
        Assert.Equal(4, sent.AreaSqm);
        // The lessee comes from the ended term, not from whoever the form was opened by.
        Assert.Equal("Ramil C. Orjeles", sent.ActualOccupant);
        Assert.Equal("24", sent.StallNo);
        // Never a takeover: this is a stall of their own, so the number must be free.
        Assert.False(sent.ReuseVacatedStall);
    }

    [Fact]
    public async Task ACustomSection_IsCarriedOverRatherThanTranslated()
    {
        // A municipality may name its own sections. The new stall must land in the same one, by its stored name.
        var past = PastStall(section: null, customSection: "Gulayan");
        var (handler, sender, _) = BuildCommand(past);

        CreateStallCommand? sent = null;
        sender.Setup(s => s.Send(It.IsAny<CreateStallCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<StallDto>>, CancellationToken>((c, _) => sent = (CreateStallCommand)c)
            .ReturnsAsync(Result<StallDto>.Success(new StallDto(
                Guid.NewGuid(), "24", StallStatus.Active, "Ramil C. Orjeles", null,
                null, null, 900m, 30m, null, null, null, null, null)));

        await handler.Handle(Command(past.Id), CancellationToken.None);

        Assert.Null(sent!.Section);
        Assert.Equal("Gulayan", sent.CustomSectionName);
    }

    [Fact]
    public async Task ASpaceHeldWithoutAContract_IsReLetOnTheSameBasis()
    {
        // A barbecue or ice-plant space is let without a signed contract. Re-placing that payor must not invent a
        // term and a leasee name for them: the sheets print "No contract" for such a row, and it never falls due
        // for renewal.
        var past = PastStall();
        past.Contracts.Clear();
        past.Contracts.Add(Contract.Create(
            past.Id, "Ramil C. Orjeles", null, new DateOnly(2023, 6, 1), 0, 900m,
            arrangement: OccupancyArrangement.SpaceOnly));

        var (handler, sender, _) = BuildCommand(past);

        CreateStallCommand? sent = null;
        sender.Setup(s => s.Send(It.IsAny<CreateStallCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<StallDto>>, CancellationToken>((c, _) => sent = (CreateStallCommand)c)
            .ReturnsAsync(Result<StallDto>.Success(new StallDto(
                Guid.NewGuid(), "24", StallStatus.Active, "Ramil C. Orjeles", null,
                null, null, 900m, 30m, null, MarketSection.MeatSection, null, null, null)));

        await handler.Handle(Command(past.Id), CancellationToken.None);

        Assert.Equal(OccupancyArrangement.SpaceOnly, sent!.Arrangement);
    }

    [Fact]
    public async Task ThePreviousStall_IsNeverWrittenTo()
    {
        var past = PastStall();
        var (handler, _, stalls) = BuildCommand(past);

        await handler.Handle(Command(past.Id), CancellationToken.None);

        // The balance on the closed account belongs to the term that incurred it; nothing is transferred or closed.
        stalls.Verify(r => r.UpdateAsync(It.IsAny<Stall>(), It.IsAny<CancellationToken>()), Times.Never);
        stalls.Verify(r => r.AddContractAsync(It.IsAny<Contract>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Single(past.Contracts);
    }

    [Fact]
    public async Task OnAReLetStall_ThePastLesseeIsPlaced_NotTheSittingOne()
    {
        // The trap this exists for: Stall 3's LATEST term belongs to the lessee occupying it now. Reading "the
        // stall's contract" would register a second stall for that person — while the payor who actually returned,
        // and whose row the office clicked, is left out.
        var past = PastStall();
        var endedTerm = past.Contracts.Single();
        past.Contracts.Add(Contract.Create(
            past.Id, "Teofila Reyes", "Teofila Reyes", new DateOnly(2026, 6, 8), 3, 900m));

        var (handler, sender, _) = BuildCommand(past);

        CreateStallCommand? sent = null;
        sender.Setup(s => s.Send(It.IsAny<CreateStallCommand>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<Result<StallDto>>, CancellationToken>((c, _) => sent = (CreateStallCommand)c)
            .ReturnsAsync(Result<StallDto>.Success(new StallDto(
                Guid.NewGuid(), "24", StallStatus.Active, "Ramil C. Orjeles", null,
                null, null, 900m, 30m, null, MarketSection.MeatSection, null, null, null)));

        var command = Command(past.Id) with { ContractId = endedTerm.Id };
        await handler.Handle(command, CancellationToken.None);

        Assert.Equal("Ramil C. Orjeles", sent!.ActualOccupant);
    }

    [Fact]
    public async Task ThePreview_ReadsTheTermItWasAskedFor()
    {
        var past = PastStall();
        var endedTerm = past.Contracts.Single();
        past.Contracts.Add(Contract.Create(
            past.Id, "Teofila Reyes", "Teofila Reyes", new DateOnly(2026, 6, 8), 3, 900m));

        var handler = BuildPreview(past, new[] { Numbered("3"), Numbered("23") });

        var forPastLessee = await handler.Handle(
            new GetStallReassignmentPreviewQuery(past.Id, endedTerm.Id), CancellationToken.None);

        Assert.Equal("Ramil C. Orjeles", forPastLessee.Value!.Occupant);
        Assert.Equal("24", forPastLessee.Value.SuggestedStallNo);
    }

    [Fact]
    public async Task AStallThatCannotBeFound_IsReportedRatherThanGuessedAt()
    {
        var stalls = new Mock<IStallRepository>();
        stalls.Setup(r => r.GetByIdWithContractsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Stall?)null);

        var handler = new AssignPastOccupantStallCommandHandler(stalls.Object, new Mock<ISender>().Object);
        var result = await handler.Handle(Command(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    // ── The preview the form is filled from ───────────────────────────────────────────────────────────

    private static GetStallReassignmentPreviewQueryHandler BuildPreview(
        Stall past, IReadOnlyList<Stall> siblings, BillingArchetype archetype = BillingArchetype.DailyStall)
    {
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM", archetype: archetype);

        var stalls = new Mock<IStallRepository>();
        var facilities = new Mock<IFacilityRepository>();

        stalls.Setup(r => r.GetByIdWithContractsAsync(past.Id, It.IsAny<CancellationToken>())).ReturnsAsync(past);
        stalls.Setup(r => r.GetFacilityCodeByStallIdAsync(past.Id, It.IsAny<CancellationToken>())).ReturnsAsync(FacilityCode.NPM);
        stalls.Setup(r => r.GetStallsWithContractsByFacilityAsync(
                It.IsAny<FacilityCode>(), It.IsAny<MarketSection?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(siblings);
        facilities.Setup(r => r.GetByCodeAsync(FacilityCode.NPM, It.IsAny<CancellationToken>())).ReturnsAsync(facility);

        return new GetStallReassignmentPreviewQueryHandler(stalls.Object, facilities.Object);
    }

    private static Stall Numbered(string stallNo) =>
        Stall.Create(FacilityId, stallNo, 900m, ApplicableFees.BaseRental, section: MarketSection.MeatSection);

    [Fact]
    public async Task TheSuggestedNumber_IsOnePastTheHighestInThatSection()
    {
        var past = PastStall();
        var handler = BuildPreview(past, new[] { Numbered("3"), Numbered("23"), Numbered("7") });

        var result = await handler.Handle(new GetStallReassignmentPreviewQuery(past.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("24", result.Value!.SuggestedStallNo);
        Assert.Equal("Ramil C. Orjeles", result.Value.Occupant);
        Assert.Equal(900m, result.Value.MonthlyRate);
        Assert.Equal(3, result.Value.SuggestedDurationYears);
    }

    [Fact]
    public async Task NumbersThatAreNotPlainIntegers_DoNotDefeatTheSuggestion()
    {
        var past = PastStall();
        var handler = BuildPreview(past, new[] { Numbered("A-1"), Numbered("12"), Numbered("Annex") });

        var result = await handler.Handle(new GetStallReassignmentPreviewQuery(past.Id), CancellationToken.None);

        Assert.Equal("13", result.Value!.SuggestedStallNo);
    }

    [Fact]
    public async Task WhetherTheFeeIsDaily_ComesFromTheFacilitysOwnBillingArchetype()
    {
        var past = PastStall();

        var daily = await BuildPreview(past, new[] { Numbered("3") }, BillingArchetype.DailyStall)
            .Handle(new GetStallReassignmentPreviewQuery(past.Id), CancellationToken.None);
        var monthly = await BuildPreview(past, new[] { Numbered("3") }, BillingArchetype.MonthlyRental)
            .Handle(new GetStallReassignmentPreviewQuery(past.Id), CancellationToken.None);

        Assert.True(daily.Value!.IsDailyBilled);
        Assert.False(monthly.Value!.IsDailyBilled);
    }

    [Fact]
    public async Task ALegacyTermLongerThanAContractMayRun_IsOfferedAtTheLimit()
    {
        // Pre-filling 20 years would only be refused by the create path; the form starts from a usable figure.
        var past = PastStall(durationYears: 20);
        var handler = BuildPreview(past, new[] { Numbered("3") });

        var result = await handler.Handle(new GetStallReassignmentPreviewQuery(past.Id), CancellationToken.None);

        Assert.Equal(10, result.Value!.SuggestedDurationYears);
    }
}
