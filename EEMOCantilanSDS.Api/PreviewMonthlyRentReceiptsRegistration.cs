using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EEMOCantilanSDS.Api;

public static class PreviewMonthlyRentReceiptsRegistration
{
    /// <summary>
    /// Preview-only: with no <c>PreviewMonthlyRentReceipts</c> settings nothing changes. With them, the Office rent receipt is
    /// enabled for one tenant and facility, and only when the API's own connection is the named loopback preview database.
    /// Any mismatch keeps the default (off) and logs why. Never place these settings in a shared or production configuration.
    /// </summary>
    public static IServiceCollection AddPreviewMonthlyRentReceipts(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("PreviewMonthlyRentReceipts");
        if (!section.GetValue<bool>("Enabled")) return services;

        _ = Enum.TryParse<FacilityCode>(section["Facility"], true, out var facility);
        _ = Guid.TryParse(section["Tenant"], out var tenant);
        var target = new PreviewMonthlyRentTarget(section["Database"] ?? string.Empty, section.GetValue<int>("Port"), tenant, facility);
        var options = PreviewMonthlyRentGate.OptionsFor(true, target, configuration.GetConnectionString("DefaultConnection"), out var refusals);

        using var loggers = LoggerFactory.Create(b => b.AddConsole());
        var log = loggers.CreateLogger("PreviewMonthlyRentReceipts");
        if (refusals.Count > 0) log.LogError("Preview rent receipts stay OFF: {Reasons}", string.Join(" | ", refusals));
        else log.LogWarning("Preview rent receipts ENABLED for {Facility} in one preview tenant. Not for shared or production use.", target.Facility);

        services.Replace(ServiceDescriptor.Singleton(options));
        return services;
    }
}
