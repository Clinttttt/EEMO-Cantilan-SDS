using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Client.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using Moq;

namespace EEMOCantilanSDS.ComponentTests;

/// <summary>
/// A <see cref="FacilityState"/> for component tests: the office's own record of its facilities, which is what
/// decides the words its screens show for its facilities and its market's collection areas.
///
/// <para>
/// A component that reads this catalogue needs it registered even when the test is about something else, so
/// <see cref="WithNoRecord"/> gives one that has loaded nothing - every label then falls back to the platform's
/// canonical wording, which is what those tests were written against.
/// </para>
/// </summary>
internal static class FacilityCatalogFixture
{
    /// <summary>An office that has recorded nothing: labels fall back to the canonical wording.</summary>
    public static FacilityState WithNoRecord() => new(Mock.Of<IFacilitiesApiClient>());

    /// <summary>An office's daily market, named by the office, with its own name for each collection area.</summary>
    public static FacilityState NamingTheMarket(
        string facilityName, string shortName, string? vegetable, string? fish, string? meat)
    {
        var api = new Mock<IFacilitiesApiClient>();
        api.Setup(a => a.GetFacilitySummariesAsync(It.IsAny<int>(), It.IsAny<int>()))
           .ReturnsAsync(Result<IReadOnlyList<FacilitySidebarSummaryDto>>.Success(new List<FacilitySidebarSummaryDto>
           {
               new(FacilityCode.NPM, facilityName, shortName, 0, vegetable, fish, meat),
           }));

        var state = new FacilityState(api.Object);
        state.EnsureLoadedAsync().GetAwaiter().GetResult();
        return state;
    }
}
