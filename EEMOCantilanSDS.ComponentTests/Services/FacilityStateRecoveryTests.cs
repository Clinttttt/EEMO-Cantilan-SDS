using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Services;

/// <summary>
/// The shared facility catalogue behind every facility name, acronym and URL. A first read that failed must not be kept for the whole circuit, a
/// facility created after the first read must be reachable by its acronym once the catalogue is re-read, and only a facility with no acronym at
/// all ever shows its internal slot code.
/// </summary>
public sealed class FacilityStateRecoveryTests
{
    private static FacilitySidebarSummaryDto Facility(FacilityCode code, string name, string shortName) => new(code, name, shortName, 0);

    private static Result<IReadOnlyList<FacilitySidebarSummaryDto>> Ok(params FacilitySidebarSummaryDto[] facilities) =>
        Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(facilities);

    [Fact]
    public async Task AFailedFirstRead_IsRetriedByTheNextCaller_AndASuccessfulOneIsNeverReadAgain()
    {
        var api = new Mock<IFacilitiesApiClient>();
        api.SetupSequence(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Failure("Down", 503))
            .ReturnsAsync(Ok(Facility(FacilityCode.Custom1, "Rest Rooms", "RR")));
        var state = new FacilityState(api.Object, retryAfterFailure: TimeSpan.Zero);

        await state.EnsureLoadedAsync();
        Assert.False(state.TryResolveSlug("rr", out _));                    // nothing read yet: the acronym is unknown

        await state.EnsureLoadedAsync();                                    // the failed read is not kept: this one tries again
        Assert.True(state.TryResolveSlug("rr", out var code));
        Assert.Equal(FacilityCode.Custom1, code);

        await state.EnsureLoadedAsync(); await state.EnsureLoadedAsync();   // a successful read stays for the circuit
        api.Verify(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ADownApiIsNotHammered_ReadsAfterAFailureWaitForTheRetryWindow()
    {
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Failure("Down", 503));
        var state = new FacilityState(api.Object, retryAfterFailure: TimeSpan.FromHours(1));

        for (var i = 0; i < 5; i++) await state.EnsureLoadedAsync();        // five pages opening in a row while the API is down

        api.Verify(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task CallersWhileAReadIsInFlightShareIt()
    {
        var gate = new TaskCompletionSource<Result<IReadOnlyList<FacilitySidebarSummaryDto>>>();
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>())).Returns(gate.Task);
        var state = new FacilityState(api.Object, retryAfterFailure: TimeSpan.Zero);

        var first = state.EnsureLoadedAsync(); var second = state.EnsureLoadedAsync();
        gate.SetResult(Ok(Facility(FacilityCode.NPM, "Public Market", "PM")));
        await Task.WhenAll(first, second);

        api.Verify(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task AFacilityCreatedAfterTheFirstRead_IsReachableByItsAcronymOnceTheCatalogueIsReloaded()
    {
        var api = new Mock<IFacilitiesApiClient>();
        api.SetupSequence(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Ok(Facility(FacilityCode.Custom1, "Rest Rooms", "RR")))
            .ReturnsAsync(Ok(Facility(FacilityCode.Custom1, "Rest Rooms", "RR"), Facility(FacilityCode.Custom2, "Cold Storage", "CS")));
        var state = new FacilityState(api.Object);
        await state.EnsureLoadedAsync();

        Assert.False(state.TryResolveSlug("cs", out _));                    // before the reload: the new acronym does not resolve (the reported bounce)

        await state.ReloadAsync();                                          // what creating a facility now does
        Assert.True(state.TryResolveSlug("cs", out var code));
        Assert.Equal(FacilityCode.Custom2, code);
        Assert.Equal("CS", state.ShortNameOf(FacilityCode.Custom2));
        Assert.Equal("cs", state.SlugOf(FacilityCode.Custom2));
    }

    [Fact]
    public async Task ARenamedAcronymResolvesByItsNewSlug_AndTheStableSlotSlugStillWorks()
    {
        var api = new Mock<IFacilitiesApiClient>();
        api.SetupSequence(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Ok(Facility(FacilityCode.Custom1, "Rest Rooms", "RR")))
            .ReturnsAsync(Ok(Facility(FacilityCode.Custom1, "Rest Rooms", "TOILET")));
        var state = new FacilityState(api.Object);
        await state.EnsureLoadedAsync();
        await state.ReloadAsync();

        Assert.True(state.TryResolveSlug("toilet", out var renamed)); Assert.Equal(FacilityCode.Custom1, renamed);
        Assert.False(state.TryResolveSlug("rr", out _));                    // the old acronym no longer names it
        Assert.True(state.TryResolveSlug("custom1", out var stable)); Assert.Equal(FacilityCode.Custom1, stable);   // old bookmarks keep working
    }

    [Fact]
    public async Task TwoFacilitiesWithTheSameAcronym_NeverShareASlug()
    {
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Ok(Facility(FacilityCode.Custom1, "Cold Storage", "CS"), Facility(FacilityCode.Custom2, "Cash Stand", "CS")));
        var state = new FacilityState(api.Object);
        await state.EnsureLoadedAsync();

        Assert.Equal("cs", state.SlugOf(FacilityCode.Custom1));
        Assert.Equal("custom2", state.SlugOf(FacilityCode.Custom2));        // the later claimant keeps its stable slot slug
        Assert.True(state.TryResolveSlug("cs", out var first)); Assert.Equal(FacilityCode.Custom1, first);
    }

    [Theory]
    [InlineData("RR", "Rest Rooms", "RR")]                                  // the saved acronym wins
    [InlineData("", "Cold Storage", "CS")]                                  // none saved: derived from the facility's own name
    [InlineData("  ", "Madrid Public Market", "MPM")]
    [InlineData("", "", "Custom3")]                                         // only a facility with neither falls back to the internal slot code
    public void TheAcronymShownIsTheSavedOne_ElseDerivedFromTheName_ElseTheSlotCode(string saved, string name, string expected) =>
        Assert.Equal(expected, FacilityState.AcronymOf(new FacilitySidebarSummaryDto(FacilityCode.Custom3, name, saved, 0)));
}
