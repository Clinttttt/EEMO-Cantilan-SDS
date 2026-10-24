using EEMOCantilanSDS.Application.Common.Revenue;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EEMOCantilanSDS.Api;

public static class FishMeatRequestActivation
{
    public static IServiceCollection AddFishMeatRequestActivation(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("StallTrack:FishMeatRequests");
        var enabled = section.GetValue<bool?>("Enabled");
        var workingCopy = configuration.GetValue<bool>("WorkingCopyFishMeatRequests:Enabled");

        if (workingCopy)
        {
            if (enabled == true)
                throw new InvalidOperationException("Choose normal tenant activation or working-copy activation, not both.");
            // The Development-only working-copy path already installed its own scoped options.
            return services;
        }

        // Identity requests are available by default for all valid tenants with a ready schema.
        // An operator can explicitly disable them or optionally restrict them to named tenants.
        if (enabled == false) return services;

        var tenants = section.GetSection("MunicipalityIds").Get<Guid[]>();
        if (tenants is { Length: > 0 } && (tenants.Any(x => x == Guid.Empty)
            || tenants.Distinct().Count() != tenants.Length))
            throw new InvalidOperationException("Fish/Meat request municipality allow-list contains invalid or duplicate IDs.");

        // Never activate the independent registration-required payment policy.
        services.Replace(ServiceDescriptor.Singleton(new FishMeatRegistrationOptions
        {
            RequestsInstalled = true,
            RequestTenants = tenants is { Length: > 0 } ? tenants.ToHashSet() : null
        }));
        return services;
    }
}