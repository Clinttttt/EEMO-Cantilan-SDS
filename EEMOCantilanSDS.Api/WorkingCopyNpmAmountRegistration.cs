using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EEMOCantilanSDS.Api;

public static class WorkingCopyNpmAmountRegistration
{
    public static IServiceCollection AddWorkingCopyNpmAmounts(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection("WorkingCopyNpmAmounts");
        if (!section.GetValue<bool>("Enabled")) return services;
        var tenant = section.GetValue<Guid>("MunicipalityId");
        var contracts = section.GetSection("ContractIds").Get<Guid[]>() ?? [];
        if (!environment.IsDevelopment() || tenant == Guid.Empty || contracts.Length == 0 || contracts.Contains(Guid.Empty)
            || PreviewMonthlyRentGate.DatabaseRefusals(configuration.GetConnectionString("DefaultConnection"), "stalltrack_workcopy_main", 55491).Count != 0
            || section["ApprovalReference"] is not { } reference || !reference.StartsWith("TEST-NPM-AMOUNT-", StringComparison.Ordinal))
            throw new InvalidOperationException("Amount acceptance requires the isolated working copy and explicit synthetic contract approval.");
        services.Replace(ServiceDescriptor.Scoped<NpmPartPaymentOptions>(sp =>
        {
            var current = sp.GetRequiredService<ICurrentMunicipalityAccessor>();
            if (current.MunicipalityId != tenant) return new();
            var db = sp.GetRequiredService<AppDbContext>();
            // Only explicitly listed synthetic identities qualify; a real or renamed account fails closed.
            var approved = db.Stalls.Where(x => x.MunicipalityId == tenant && x.Facility!.Code == FacilityCode.NPM
                    && x.StallNo.StartsWith("TEST-NPM-AMOUNT-"))
                .SelectMany(x => x.Contracts).Where(x => contracts.Contains(x.Id)).Select(x => x.Id).ToHashSet();
            return new(false, approved);
        }));
        return services;
    }
}
