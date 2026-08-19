using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// Whose name a facility carries on an LGU's own screens.
///
/// <para>
/// Reported from use: Madrid's reports were headed NPM. The facility CODE happens to be NPM, and the display fallback was
/// Cantilan's own naming - so a municipality whose market is the Madrid Public Market was shown the New Public Market, in
/// the eyebrow of its own report. It is the borrowed-seal fault again, in words instead of a picture.
/// </para>
///
/// <para>
/// The rule now: the LGU's own record decides. An acronym it has not recorded is DERIVED from its own facility name, by the
/// same rule the activation console uses, so the two cannot disagree. Only a facility the LGU has no record of falls back
/// to the bare code - which states nothing about anybody.
/// </para>
/// </summary>
public class FacilityNamingTests
{
    private static FacilityState With(params (FacilityCode Code, string Name, string Short)[] facilities)
    {
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
           .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(
               facilities.Select(f => new FacilitySidebarSummaryDto(
                   Code: f.Code,
                   Name: f.Name,
                   ShortName: f.Short,
                   UnpaidCount: 0)).ToList()));

        var state = new FacilityState(api.Object);
        state.EnsureLoadedAsync().GetAwaiter().GetResult();
        return state;
    }

    [Fact]
    public void AnLGUsOwnNameAndAcronymAreUsed()
    {
        var state = With((FacilityCode.NPM, "Madrid Public Market", "MPM"));

        Assert.Equal("Madrid Public Market", state.NameOf(FacilityCode.NPM));
        Assert.Equal("MPM", state.ShortNameOf(FacilityCode.NPM));
    }

    [Fact]
    public void CANTILANSNamesAreNeverShownToAnotherLGU()
    {
        // The defect. The code is NPM either way; the words must not be.
        var state = With((FacilityCode.NPM, "Madrid Public Market", "MPM"));

        Assert.DoesNotContain("New Public Market", state.NameOf(FacilityCode.NPM));
        Assert.NotEqual("NPM", state.ShortNameOf(FacilityCode.NPM));
    }

    [Fact]
    public void AMissingAcronymIsDerivedFromTheLGUsOwnName()
    {
        // An LGU that recorded no acronym gets one from its own words - not from the municipality the code was named after.
        var state = With((FacilityCode.NPM, "Madrid Public Market", ""));

        Assert.Equal("MPM", state.ShortNameOf(FacilityCode.NPM));
    }

    [Fact]
    public void DerivationSkipsTheSmallWordsAndCapsTheLength()
    {
        var state = With(
            (FacilityCode.TCC, "Municipality of the Commercial Center", ""),
            (FacilityCode.SLH, "Slaughterhouse", ""),
            (FacilityCode.TRM, "North East South West Terminal", ""));

        Assert.Equal("MCC", state.ShortNameOf(FacilityCode.TCC));     // "of" and "the" are not initials
        Assert.Equal("SLA", state.ShortNameOf(FacilityCode.SLH));     // one word: its first three letters
        Assert.Equal("NESW", state.ShortNameOf(FacilityCode.TRM));    // capped at four
    }

    [Fact]
    public void AFacilityTheLGUDoesNotHaveFallsBackToTheCODE()
    {
        // A code says nothing about any municipality, which is what makes it the safe answer when there is no record.
        var state = With((FacilityCode.NPM, "Madrid Public Market", "MPM"));

        Assert.Equal("TPM", state.ShortNameOf(FacilityCode.TPM));
        Assert.Equal("TPM", state.NameOf(FacilityCode.TPM));
    }

    [Fact]
    public void BeforeTheCatalogLoadsNoLGUSNamesAreBorrowed()
    {
        // Nothing loaded at all: the page shows codes until the tenant's own catalog arrives. It used to show Cantilan's
        // names here, which is why a fresh LGU's first paint announced another municipality's facilities.
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
           .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Failure("offline"));

        var state = new FacilityState(api.Object);
        state.EnsureLoadedAsync().GetAwaiter().GetResult();

        Assert.Equal("NPM", state.ShortNameOf(FacilityCode.NPM));
        Assert.Equal("NPM", state.NameOf(FacilityCode.NPM));
        Assert.DoesNotContain("New Public Market", state.NameOf(FacilityCode.NPM));
    }
}
