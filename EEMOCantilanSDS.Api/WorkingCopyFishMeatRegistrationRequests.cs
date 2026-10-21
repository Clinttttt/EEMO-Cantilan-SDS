using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EEMOCantilanSDS.Api;

/// <summary>
/// Enables nonfinancial Fish/Meat identity requests only for the expressly authorized
/// Cantilan local working-copy database. No registration-required financial policy is enabled.
/// </summary>
public static class WorkingCopyFishMeatRegistrationRequests
{
    private static readonly Guid DemoTenant = Guid.Parse("0ea98ba6-f7b1-4928-96f1-6177d45741bd");
    private const string MigrationId = "20261020133853_AddFishMeatRegistrationRequestsWorkingCopy20261020";

    public static IServiceCollection AddWorkingCopyFishMeatRegistrationRequests(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection("WorkingCopyFishMeatRequests");
        if (!section.GetValue<bool>("Enabled")) return services;

        if (!environment.IsDevelopment()
            || section.GetValue<Guid>("MunicipalityId") != DemoTenant
            || PreviewMonthlyRentGate.DatabaseRefusals(
                configuration.GetConnectionString("DefaultConnection"), "stalltrack_workcopy_main", 55491).Count != 0)
            throw new InvalidOperationException(
                "Fish/Meat request activation refused: requires Development and the isolated Cantilan working-copy database.");

        // The separate policy requiring registration before collecting fees stays empty/disabled.
        services.Replace(ServiceDescriptor.Singleton(new FishMeatRegistrationOptions
        {
            RequestsInstalled = true,
            RequestTenants = new HashSet<Guid> { DemoTenant }
        }));
        return services;
    }

    public static async Task VerifyWorkingCopyFishMeatRegistrationRequestsAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("WorkingCopyFishMeatRequests:Enabled")) return;
        await using var scope = app.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentMunicipalityAccessor>().Set(DemoTenant);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // The tenant accessor was bound to DemoTenant above. Keep EF's soft-delete
        // and tenant filters in force; a missing/inactive local office must fail closed.
        var tenant = await db.Municipalities.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == DemoTenant);
        if (tenant is null || tenant.TenantCode != "cantilan-sds"
            || !(await db.Database.GetAppliedMigrationsAsync()).Contains(MigrationId))
            throw new InvalidOperationException(
                "Fish/Meat request activation refused: the expected working-copy tenant or EF migration is missing.");

        scope.ServiceProvider.GetRequiredService<ICurrentMunicipalityAccessor>().Set(DemoTenant);
        // Before listening, require the mapped table and its required indexes/columns to be readable.
        _ = await db.FishMeatRegistrationRequests.AsNoTracking()
            .Select(r => new { r.Id, r.CollectorId, r.ClientOperationId, r.DecisionOperationId,
                r.SubmittedAtUtc, r.Status, r.Version }).Take(1).ToArrayAsync();
        app.Logger.LogWarning("TEST-only Fish/Meat registration requests enabled on isolated working copy for {Tenant}; collection policy unchanged.", DemoTenant);
    }
}