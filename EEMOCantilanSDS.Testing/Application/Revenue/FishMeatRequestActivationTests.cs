using EEMOCantilanSDS.Api;
using EEMOCantilanSDS.Application.Common.Revenue;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EEMOCantilanSDS.Testing;

public sealed class FishMeatRequestActivationTests
{
    private static FishMeatRegistrationOptions Build(IDictionary<string,string?>? settings = null)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string,string?>()).Build();
        var services = new ServiceCollection();
        services.AddSingleton(new FishMeatRegistrationOptions());
        services.AddFishMeatRequestActivation(cfg);
        return services.BuildServiceProvider().GetRequiredService<FishMeatRegistrationOptions>();
    }

    [Fact]
    public void No_config_enables_nonfinancial_requests_for_each_valid_municipality()
    {
        var options = Build();
        Assert.True(options.RequestsAvailable(Guid.NewGuid()));
        Assert.True(options.RequestsAvailable(Guid.NewGuid()));
        Assert.False(options.RequestsAvailable(Guid.Empty));
        Assert.Empty(options.RegistrationRequiredFrom);
    }

    [Fact]
    public void Explicit_disabled_config_keeps_registration_requests_off()
    {
        var options = Build(new Dictionary<string, string?> {
            ["StallTrack:FishMeatRequests:Enabled"] = "false"
        });
        Assert.False(options.RequestsAvailable(Guid.NewGuid()));
        Assert.Empty(options.RegistrationRequiredFrom);
    }

    [Fact]
    public void Optional_allow_list_can_restrict_tenants_without_enabling_payment_policy()
    {
        var tenant = Guid.NewGuid();
        var options = Build(new Dictionary<string, string?> {
            ["StallTrack:FishMeatRequests:Enabled"] = "true",
            ["StallTrack:FishMeatRequests:MunicipalityIds:0"] = tenant.ToString()
        });
        Assert.True(options.RequestsAvailable(tenant));
        Assert.False(options.RequestsAvailable(Guid.NewGuid()));
        Assert.Empty(options.RegistrationRequiredFrom);
    }

    [Fact]
    public void Duplicate_or_empty_id_is_refused_in_optional_allow_list()
    {
        var tenant = Guid.NewGuid();
        var settings = new Dictionary<string, string?> {
            ["StallTrack:FishMeatRequests:MunicipalityIds:0"] = tenant.ToString(),
            ["StallTrack:FishMeatRequests:MunicipalityIds:1"] = tenant.ToString()
        };
        Assert.Throws<InvalidOperationException>(() => Build(settings));
        settings["StallTrack:FishMeatRequests:MunicipalityIds:1"] = Guid.Empty.ToString();
        Assert.Throws<InvalidOperationException>(() => Build(settings));
    }

    [Fact]
    public void Working_copy_activation_keeps_its_independently_guarded_options()
    {
        var services = new ServiceCollection();
        var scopedTenant = Guid.NewGuid();
        services.AddSingleton(new FishMeatRegistrationOptions {
            RequestsInstalled = true, RequestTenants = new HashSet<Guid> { scopedTenant }
        });
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{
            ["WorkingCopyFishMeatRequests:Enabled"] = "true"
        }).Build();
        services.AddFishMeatRequestActivation(cfg);
        var options = services.BuildServiceProvider().GetRequiredService<FishMeatRegistrationOptions>();
        Assert.True(options.RequestsAvailable(scopedTenant));
        Assert.False(options.RequestsAvailable(Guid.NewGuid()));
    }

    [Fact]
    public void Simultaneous_normal_and_working_copy_activation_is_rejected()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["WorkingCopyFishMeatRequests:Enabled"] = "true",
            ["StallTrack:FishMeatRequests:Enabled"] = "true"
        }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddFishMeatRequestActivation(cfg));
    }
}