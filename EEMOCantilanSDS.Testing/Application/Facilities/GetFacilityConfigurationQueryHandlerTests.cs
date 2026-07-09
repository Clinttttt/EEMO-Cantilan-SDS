using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityConfiguration;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class GetFacilityConfigurationQueryHandlerTests
{
    private static ConfiguredFacilityDto Configured(string code) =>
        new(code, code, code, null, "Custom", true, 0, new List<FacilityRateLineDto>());

    [Fact]
    public async Task ReturnsConfigured_AndAvailableIsCanonicalMinusConfigured()
    {
        var configured = new List<ConfiguredFacilityDto> { Configured("NPM"), Configured("TCC") };
        var repo = new Mock<IFacilityRepository>();
        repo.Setup(r => r.GetConfiguredFacilitiesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(configured);

        var handler = new GetFacilityConfigurationQueryHandler(repo.Object);
        var result = await handler.Handle(new GetFacilityConfigurationQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Configured.Count);
        // 8 canonical types − 2 configured = 6 available, none overlapping the configured codes.
        Assert.Equal(6, result.Value.Available.Count);
        Assert.DoesNotContain(result.Value.Available, a => a.Code is "NPM" or "TCC");
        Assert.Contains(result.Value.Available, a => a.Code == "SLH");
        // Available carries a friendly name (never a bare code) and a billing model.
        Assert.All(result.Value.Available, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
            Assert.False(string.IsNullOrWhiteSpace(a.BillingModel));
        });
    }

    [Fact]
    public async Task AllConfigured_YieldsNoAvailable()
    {
        var all = new[] { "NPM", "TCC", "NCC", "BBQ", "ICE", "SLH", "TRM", "TPM" }
            .Select(Configured).ToList();
        var repo = new Mock<IFacilityRepository>();
        repo.Setup(r => r.GetConfiguredFacilitiesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(all);

        var handler = new GetFacilityConfigurationQueryHandler(repo.Object);
        var result = await handler.Handle(new GetFacilityConfigurationQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Available);
    }
}
