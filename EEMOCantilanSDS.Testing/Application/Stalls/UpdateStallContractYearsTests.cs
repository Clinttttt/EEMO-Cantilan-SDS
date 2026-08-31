using EEMOCantilanSDS.Application.Command.Stalls.UpdateStall;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// A signed contract must run for at least one year — the office's ruling of 2026-08-16 — and the stall edit form is the only
/// screen that could ever have set one to nought, because it is the only one that edits an EXISTING term. Every other path
/// (creating a stall, renewing, assigning a past occupant, the stallholder import) already required a year or more.
///
/// <para>
/// Nought years mattered because the platform could not answer for such a row consistently: expiry computed from the term said
/// it had expired the day after it began, while the shared rule said a term stating no years had not run out. The state is now
/// refused, and the two rules are one.
/// </para>
///
/// <para>
/// The second test is the regression this fix nearly introduced. Enforcing "at least one year" in the VALIDATOR looked
/// obviously right and was wrong: the stall DTO reports <c>activeContract?.DurationYears ?? 0</c>, so a stall with no active
/// contract reports nought years, and both edit forms pass whatever they were handed straight back. A validator rule would have
/// refused an ordinary edit — a remark, an area, a rate — to any stall that simply has no contract.
/// </para>
/// </summary>
public class UpdateStallContractYearsTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    private static UpdateStallCommandHandler Build(Mock<IStallRepository> repo)
        => new(repo.Object, new Mock<IUnitOfWork>().Object, CacheTestDoubles.Invalidator, CacheTestDoubles.Tenant);

    private static (Stall Stall, Mock<IStallRepository> Repo) Fixture(OccupancyArrangement? arrangement)
    {
        var stall = Stall.Create(Guid.NewGuid(), "1", 900m, ApplicableFees.BaseRental);

        // No arrangement at all means a stall with NO active contract, which is a real state: a space the office has recorded
        // but not let, or one whose contract has been closed out.
        if (arrangement is { } a)
        {
            stall.Contracts.Add(Contract.Create(
                stall.Id, "Merlita A. Abuso", "Merlita A. Abuso", Start,
                durationYears: a == OccupancyArrangement.SignedContract ? 3 : 0, monthlyRate: 900m, arrangement: a));
        }

        var repo = new Mock<IStallRepository>();
        repo.Setup(r => r.GetByIdWithContractsAsync(stall.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stall);
        return (stall, repo);
    }

    private static UpdateStallCommand Command(Guid stallId, int? years) => new(
        StallId: stallId,
        MonthlyRate: 900m,
        Fees: ApplicableFees.BaseRental,
        AreaSqm: null,
        AreaNote: null,
        DailyRate: null,
        ActualOccupant: "Merlita A. Abuso",
        NameOnContract: "Merlita A. Abuso",
        Remarks: null,
        ContractDate: Start.ToDateTime(TimeOnly.MinValue),
        ContractYears: years);

    [Fact]
    public async Task ASignedContractCannotBeEditedToNoughtYears()
    {
        var (stall, repo) = Fixture(OccupancyArrangement.SignedContract);

        var result = await Build(repo).Handle(Command(stall.Id, 0), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);          // a stated refusal, not a server error
        Assert.Contains("at least one year", result.Error!);
        Assert.Contains("space-only", result.Error!);               // and it says how to record it properly
        Assert.Equal(3, stall.Contracts.Single().DurationYears);    // the existing term is untouched
    }

    [Fact]
    public async Task AStallWithNoActiveContractIsStillEditable()
    {
        // The regression guard. Nought arrives here because there is no term to state, and it must not be read as an attempt
        // to set one.
        var (stall, repo) = Fixture(arrangement: null);

        var result = await Build(repo).Handle(Command(stall.Id, 0), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ASpaceOnlyOccupancyStaysOpenEndedWhenItsDateIsCorrected()
    {
        // Nought is legitimate for an occupancy with no signed contract, and correcting such a row's effectivity date must
        // neither be refused nor leave it holding a nought-year term.
        var (stall, repo) = Fixture(OccupancyArrangement.SpaceOnly);

        var result = await Build(repo).Handle(Command(stall.Id, 0), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DomainRules.OpenEndedTermYears, stall.Contracts.Single().DurationYears);
        Assert.False(stall.Contracts.Single().IsExpiredOn(Start.AddYears(50)));
    }

    [Fact]
    public async Task TheVALIDATORMustKeepAcceptingNoughtYears()
    {
        // The assertion that would have caught the mistake. The tests above go through the handler, so a rule added to the
        // validator would have refused the command before any of them ran — and all four would still have looked like handler
        // failures rather than the edge closing on a legitimate edit. Stated here directly against the validator so the reason
        // nought is allowed cannot be "tidied up" later without something failing.
        // The validator now looks a stall up, because the monthly requirement is relaxed only for a MARKET whose month owes
        // the days it has and the command carries a stall id rather than a facility. That makes the rule asynchronous, so it
        // is awaited here as the pipeline awaits it (ValidationBehavior calls ValidateAsync). This edit states a monthly
        // rate, so it never reaches the lookup: the repository double is never asked.
        var result = await new UpdateStallCommandValidator(Mock.Of<IStallRepository>(), CacheTestDoubles.FeeRateResolver)
            .ValidateAsync(Command(Guid.NewGuid(), 0));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
    }

    [Fact]
    public async Task AnOrdinarySignedTermCorrectionStillApplies()
    {
        // So the refusal cannot be passing by rejecting every edit.
        var (stall, repo) = Fixture(OccupancyArrangement.SignedContract);

        var result = await Build(repo).Handle(Command(stall.Id, 5), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, stall.Contracts.Single().DurationYears);
    }
}
