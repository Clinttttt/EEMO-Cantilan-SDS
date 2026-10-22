using EEMOCantilanSDS.Application.Common.Revenue;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EEMOCantilanSDS.Api;

public static class FishMeatRequestActivation
{
    public static IServiceCollection AddFishMeatRequestActivation(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("StallTrack:FishMeatRequests");
        if (!section.GetValue<bool>("Enabled")) return services;
        if (configuration.GetValue<bool>("WorkingCopyFishMeatRequests:Enabled"))
            throw new InvalidOperationException("Choose normal tenant activation or working-copy activation, not both.");
        var tenants = section.GetSection("MunicipalityIds").Get<Guid[]>() ?? [];
        if (tenants.Length == 0 || tenants.Any(x => x == Guid.Empty) || tenants.Distinct().Count() != tenants.Length)
            throw new InvalidOperationException("Fish/Meat requests require an explicit nonempty municipality allow-list.");
        // Nonfinancial request availability never activates registration-required collection policy.
        services.Replace(ServiceDescriptor.Singleton(new FishMeatRegistrationOptions
        {
            RequestsInstalled = true, RequestTenants = tenants.ToHashSet()
        }));
        return services;
    }
}
