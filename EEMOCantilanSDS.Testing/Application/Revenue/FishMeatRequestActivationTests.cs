using EEMOCantilanSDS.Api;
using EEMOCantilanSDS.Application.Common.Revenue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.Testing;

public sealed class FishMeatRequestActivationTests
{
    [Fact]
    public void Normal_activation_is_explicit_tenant_scoped_and_does_not_change_cash_policy()
    {
        var tenant = Guid.NewGuid(); var other = Guid.NewGuid();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["StallTrack:FishMeatRequests:Enabled"] = "true",
            ["StallTrack:FishMeatRequests:MunicipalityIds:0"] = tenant.ToString()
        }).Build();
        var services = new ServiceCollection(); services.AddSingleton(new FishMeatRegistrationOptions());
        services.AddFishMeatRequestActivation(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<FishMeatRegistrationOptions>();
        Assert.True(options.RequestsAvailable(tenant)); Assert.False(options.RequestsAvailable(other));
        Assert.False(options.RequestsAvailable(Guid.Empty));
        Assert.Empty(options.RegistrationRequiredFrom);
    }

    [Fact]
    public void Missing_allow_list_is_rejected_and_default_is_off()
    {
        var services = new ServiceCollection(); services.AddSingleton(new FishMeatRegistrationOptions());
        services.AddFishMeatRequestActivation(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        Assert.False(provider.GetRequiredService<FishMeatRegistrationOptions>().RequestsAvailable(Guid.NewGuid()));
        var enabled = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["StallTrack:FishMeatRequests:Enabled"] = "true"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => services.AddFishMeatRequestActivation(enabled));
    }
}
