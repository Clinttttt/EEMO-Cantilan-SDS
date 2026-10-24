using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Tenancy;
using EEMOCantilanSDS.Client.Services;
using Moq;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// How the signed-in LGU's branding survives the prerender boundary.
///
/// <para>
/// Prerendering is enabled at the interactive root, so a component initialises TWICE: once for the static response and again
/// when the circuit starts. Left alone that means two API calls and a visible flash — the screen renders branded, then with
/// whatever the fallbacks are, then branded again. <c>Apply</c> is what a page hands its persisted answer to, and the
/// behaviour worth pinning is that it stops the second fetch rather than merely hiding it.
/// </para>
/// </summary>
public class BrandingStatePrerenderTests
{
    private static MunicipalityBrandingDto Branding(string code, string name) =>
        new(Code: code, TenantCode: code.ToLowerInvariant(), Name: name, Province: "Surigao del Sur",
            OfficeName: "Municipal Economic Enterprise Office", SealPath: null, Status: "Active", IsActive: true,
            OfficeAcronym: "MEEO");

    [Fact]
    public async Task BrandingCarriedOverIsNotFetchedAgain()
    {
        var api = new Mock<IMunicipalitiesApiClient>();
        var state = new BrandingState(api.Object);

        state.Apply(Branding("MADRID", "Madrid"));
        await state.EnsureLoadedAsync();

        api.Verify(a => a.GetCurrentBrandingAsync(), Times.Never);
        Assert.True(state.Resolved);
        Assert.Equal("Madrid", state.Municipality);
        Assert.Equal("MEEO", state.OfficeAcronym);
    }

    [Fact]
    public async Task WithNothingCarriedOverItAsksOnce_HoweverManyCallersWantIt()
    {
        var api = new Mock<IMunicipalitiesApiClient>();
        api.Setup(a => a.GetCurrentBrandingAsync())
           .ReturnsAsync(Result<MunicipalityBrandingDto>.Success(Branding("MADRID", "Madrid")));
        var state = new BrandingState(api.Object);

        await state.EnsureLoadedAsync();
        await state.EnsureLoadedAsync();

        api.Verify(a => a.GetCurrentBrandingAsync(), Times.Once);
        Assert.Equal("Madrid", state.Municipality);
    }

    [Fact]
    public async Task AFailedLoadLeavesNothingResolved()
    {
        // The distinction the change-password screen depends on: the accessors still answer (with the default office's
        // literals), but Resolved stays false so a screen can choose to claim nobody's identity instead.
        var api = new Mock<IMunicipalitiesApiClient>();
        api.Setup(a => a.GetCurrentBrandingAsync())
           .ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("unreachable"));
        var state = new BrandingState(api.Object);

        await state.EnsureLoadedAsync();

        Assert.False(state.Resolved);
        Assert.Null(state.Current);
        Assert.Equal(BrandingState.WaitingSealPath, state.SealPath);
        Assert.False(state.HasOwnSeal);
    }

    [Fact]
    public async Task AFailedReadCanRecoverOnTheNextCallInTheSameCircuit()
    {
        var api = new Mock<IMunicipalitiesApiClient>();
        api.SetupSequence(x => x.GetCurrentBrandingAsync())
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Failure("unavailable"))
            .ReturnsAsync(Result<MunicipalityBrandingDto>.Success(Branding("CARRASCAL", "Carrascal") with { SealPath = "https://api.test/carrascal/seal?v=1" }));
        var state = new BrandingState(api.Object);
        await state.EnsureLoadedAsync();
        Assert.False(state.Resolved);
        await state.EnsureLoadedAsync();
        Assert.Equal("Carrascal", state.Current?.Name);
        Assert.True(state.HasOwnSeal);
        api.Verify(x => x.GetCurrentBrandingAsync(), Times.Exactly(2));
    }

    [Fact]
    public async Task ConcurrentFirstReadsShareTheSameRequest()
    {
        var arrival = new TaskCompletionSource<Result<MunicipalityBrandingDto>>();
        var api = new Mock<IMunicipalitiesApiClient>();
        api.Setup(x => x.GetCurrentBrandingAsync()).Returns(arrival.Task);
        var state = new BrandingState(api.Object);
        var first = state.EnsureLoadedAsync();
        var second = state.EnsureLoadedAsync();
        Assert.Same(first, second);
        arrival.SetResult(Result<MunicipalityBrandingDto>.Success(Branding("CARRASCAL", "Carrascal")));
        await Task.WhenAll(first, second);
        api.Verify(x => x.GetCurrentBrandingAsync(), Times.Once);
    }

    [Fact]
    public async Task AnOlderReadCannotOverwriteNewlyAppliedBranding()
    {
        var arrival = new TaskCompletionSource<Result<MunicipalityBrandingDto>>();
        var api = new Mock<IMunicipalitiesApiClient>();
        api.Setup(x => x.GetCurrentBrandingAsync()).Returns(arrival.Task);
        var state = new BrandingState(api.Object);
        var pending = state.EnsureLoadedAsync();
        state.Apply(Branding("CARRASCAL", "New office identity"));
        arrival.SetResult(Result<MunicipalityBrandingDto>.Success(Branding("CARRASCAL", "Old office identity")));
        await pending;
        Assert.Equal("New office identity", state.Current?.Name);
    }

    [Fact]
    public void WhatIsCarriedOverIsWhatWasLoaded()
    {
        var api = new Mock<IMunicipalitiesApiClient>();
        var state = new BrandingState(api.Object);
        var branding = Branding("MADRID", "Madrid");

        state.Apply(branding);

        Assert.Same(branding, state.Current);
    }
}
