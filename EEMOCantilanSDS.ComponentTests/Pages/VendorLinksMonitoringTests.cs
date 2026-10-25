using Bunit;
using Bunit.TestDoubles;
using EEMOCantilanSDS.Application.Common.Interface.ApiClients;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Client.Components.Pages.Reports;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EEMOCantilanSDS.ComponentTests.Pages;

/// <summary>
/// The office can see, without looking anyone up, which NPM Fish / Meat occupants are linked to a registered vendor, which are not, and which
/// registered vendors hold no NPM stall. Only confirmed links count: a name match is a suggestion and is never treated as a link.
/// </summary>
public sealed class VendorLinksMonitoringTests : TestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void ListsLinkedUnlinkedAndNotOnNpm_FromConfirmedLinksOnly()
    {
        var year = PhilippineTime.Today.Year;
        var linkedVendor = new FishMeatVendorRegistrationDto(Guid.NewGuid(), year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Ana Reyes", null, null, null);
        var lookAlike = new FishMeatVendorRegistrationDto(Guid.NewGuid(), year, FishMeatVendorType.Meat, VendorRegistrationKind.New, "Ben Cruz", null, null, null);
        var walkIn = new FishMeatVendorRegistrationDto(Guid.NewGuid(), year, FishMeatVendorType.Fish, VendorRegistrationKind.Renew, "Carla Diaz", null, null, null);

        var linkedRow = new NpmVendorCandidate(Guid.NewGuid(), Guid.NewGuid(), "F-1", "Ana Reyes", "Fish Area", true, new(year, 1, 1),
            FishMeatVendorType.Fish, "v1",
            [new(Guid.NewGuid(), linkedVendor.Id, Guid.NewGuid(), Guid.NewGuid(), "Ana Reyes", "head", DateTime.UtcNow, "checked", null, null, null)], []);
        var correctedRow = new NpmVendorCandidate(Guid.NewGuid(), Guid.NewGuid(), "M-2", "Ben Cruz", "Meat Area", true, new(year, 1, 1),
            FishMeatVendorType.Meat, "v1",
            [new(Guid.NewGuid(), lookAlike.Id, Guid.NewGuid(), Guid.NewGuid(), "Ben Cruz", "head", DateTime.UtcNow, "checked", DateTime.UtcNow, "head", "wrong person")],
            [lookAlike]);

        var office = new Mock<IOfficeSourcesApiClient>();
        office.Setup(x => x.NpmVendorCandidatesAsync(year, false, false))
            .ReturnsAsync(Result<NpmVendorCandidates>.Success(new(year, 1, [linkedRow, correctedRow])));
        office.Setup(x => x.RegistrationsAsync(year))
            .ReturnsAsync(Result<IReadOnlyList<FishMeatVendorRegistrationDto>>.Success([linkedVendor, lookAlike, walkIn]));

        Services.AddSingleton(Mock.Of<ISetupApiClient>());
        Services.AddSingleton(Mock.Of<IStallsApiClient>());
        Services.AddSingleton(Mock.Of<IPaymentsApiClient>());
        Services.AddSingleton(Mock.Of<IMunicipalitiesApiClient>());
        Services.AddSingleton(Mock.Of<IFacilitiesApiClient>());
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.BrandingState>();
        Services.AddSingleton<EEMOCantilanSDS.Client.Services.FacilityState>();
        Services.AddSingleton(office.Object);
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddTestAuthorization().SetAuthorized("head").SetRoles("SuperAdmin");

        var cut = RenderComponent<VendorLinksMonitoring>();

        // Not linked: the occupant whose only link was corrected away, with the look-alike offered as a suggestion only.
        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("tbody tr"));
            Assert.Contains("Ben Cruz", row.TextContent);
            Assert.Contains("Not linked", row.TextContent);
            Assert.Contains("suggestion only", row.TextContent);
        }, Timeout);

        cut.FindAll(".vl-seg button").Single(b => b.TextContent.Contains("Linked") && !b.TextContent.Contains("not")).Click();
        var linked = Assert.Single(cut.FindAll("tbody tr"));
        Assert.Contains("Ana Reyes", linked.TextContent);
        Assert.Contains("Stall F-1", linked.TextContent);

        cut.FindAll(".vl-seg button").Single(b => b.TextContent.Contains("not on NPM")).Click();
        var vendors = cut.FindAll("tbody tr").Select(r => r.TextContent).ToList();
        Assert.Equal(2, vendors.Count);                    // the walk-in vendor and the look-alike, whose link was corrected
        Assert.Contains(vendors, v => v.Contains("Carla Diaz"));
        Assert.Contains(vendors, v => v.Contains("Ben Cruz"));
        Assert.DoesNotContain(vendors, v => v.Contains("Ana Reyes"));
    }
}
