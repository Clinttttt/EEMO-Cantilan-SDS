using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EEMOCantilanSDS.Api;

public static class WorkingCopyOfficeCashTurnoverRegistration
{
    /// <summary>
    /// Working-copy only: with no <c>OfficeCashTurnover</c> settings nothing changes (the module stays off). With them, Office cash turnover
    /// is enabled for the NAMED tenants only, and only when the API's own connection is the named loopback database on the named port.
    /// Any mismatch, a missing tenant list, or the retained municipal database keeps it off and logs why. Never place these settings in a
    /// shared or production configuration.
    /// </summary>
    public static IServiceCollection AddWorkingCopyOfficeCashTurnover(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("OfficeCashTurnover");
        if (!section.GetValue<bool>("Enabled")) return services;

        var tenants = (section.GetSection("Tenants").Get<string[]>() ?? [])
            .Select(x => Guid.TryParse(x, out var id) ? id : Guid.Empty).Where(x => x != Guid.Empty).ToHashSet();
        var refusals = new List<string>(PreviewMonthlyRentGate.DatabaseRefusals(
            configuration.GetConnectionString("DefaultConnection"), section["Database"] ?? string.Empty, section.GetValue<int>("Port")));
        if (tenants.Count == 0) refusals.Add("At least one tenant must be named.");

        using var loggers = LoggerFactory.Create(b => b.AddConsole());
        var log = loggers.CreateLogger("OfficeCashTurnover");
        var options = refusals.Count == 0 ? new OfficeCashTurnoverOptions(true, tenants) : new OfficeCashTurnoverOptions();
        if (refusals.Count > 0) log.LogError("Office cash turnover stays OFF: {Reasons}", string.Join(" | ", refusals));
        else log.LogWarning("Office cash turnover ENABLED for {Count} named tenant(s) on the working-copy database. Not for shared or production use.", tenants.Count);

        services.Replace(ServiceDescriptor.Singleton(options));
        return services;
    }
}
