using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Dtos.Facilities;
using EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityConfiguration;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class GetFacilityConfigurationQueryHandlerTests
{
    private static ConfiguredFacilityDto Configured(string code) =>
        new(code, code, code, null, "Custom", true, 0, new List<ConfiguredRateDto>());

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
        // 8 canonical types − 2 configured = 6 canonical available, plus one Head-named custom slot.
        Assert.Equal(6, result.Value.Available.Count(a => !a.IsCustom));
        Assert.Contains(result.Value.Available, a => a.IsCustom);
        Assert.DoesNotContain(result.Value.Available, a => a.Code is "NPM" or "TCC");
        Assert.Contains(result.Value.Available, a => a.Code == "SLH");
        // Every canonical available carries a friendly name (never a bare code) and a billing model.
        Assert.All(result.Value.Available.Where(a => !a.IsCustom), a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
            Assert.False(string.IsNullOrWhiteSpace(a.BillingModel));
        });
    }

    [Fact]
    public async Task AllCanonicalConfigured_StillOffersACustomSlot()
    {
        var all = new[] { "NPM", "TCC", "NCC", "BBQ", "ICE", "SLH", "TRM", "TPM" }
            .Select(Configured).ToList();
        var repo = new Mock<IFacilityRepository>();
        repo.Setup(r => r.GetConfiguredFacilitiesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(all);

        var handler = new GetFacilityConfigurationQueryHandler(repo.Object);
        var result = await handler.Handle(new GetFacilityConfigurationQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // No canonical types left, but a custom facility can still be added.
        Assert.DoesNotContain(result.Value!.Available, a => !a.IsCustom);
        Assert.Single(result.Value.Available);
        Assert.True(result.Value.Available[0].IsCustom);
    }
}
