using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EEMOCantilanSDS.Api;

public static class WorkingCopyNpmMonthlyRegistration
{
    public static IServiceCollection AddWorkingCopyNpmMonthly(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection("WorkingCopyNpmMonthly");
        if (!section.GetValue<bool>("Enabled")) return services;
        var approval = section.GetSection("Approval").Get<NpmOpeningMonthPolicy>();
        // ConfigurationBinder converts an ISO-8601 Z timestamp into Local DateTime on Windows.
        // Parse the explicit UTC approval evidence with RoundtripKind instead of weakening the UTC guard.
        var rawApprovedAt = section["Approval:ApprovedAtUtc"];
        if (approval is not null && DateTime.TryParseExact(rawApprovedAt, "O",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var utcStamp)
            && utcStamp.Kind == DateTimeKind.Utc)
            approval = approval with { ApprovedAtUtc = utcStamp };
        var refusals = PreviewMonthlyRentGate.DatabaseRefusals(configuration.GetConnectionString("DefaultConnection"),
            "stalltrack_workcopy_main", 55491);
        if (!environment.IsDevelopment() || refusals.Count != 0 || approval is null
            || approval.MunicipalityId == Guid.Empty || approval.FacilityId == Guid.Empty
            || approval.ApprovedByUserId == Guid.Empty || approval.ApprovedAtUtc.Kind != DateTimeKind.Utc
            || approval.ApprovedAtUtc > DateTime.UtcNow || approval.EffectiveFrom == default
            || !approval.ApprovalReference.StartsWith("TEST-", StringComparison.Ordinal))
            throw new InvalidOperationException("NPM test activation requires Development, the exact isolated working-copy database and explicit TEST approval evidence.");
        services.Replace(ServiceDescriptor.Singleton(new NpmRentMonthRuntime(true, true, true,
            new HashSet<Guid> { approval.MunicipalityId })));
        services.AddSingleton(new NpmOpeningMonthPolicies([approval]));
        return services;
    }

    public static async Task VerifyWorkingCopyNpmMonthlyAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("WorkingCopyNpmMonthly:Enabled")) return;
        await using var scope = app.Services.CreateAsyncScope();
        var policy = scope.ServiceProvider.GetRequiredService<NpmOpeningMonthPolicies>().Approvals!.Single();
        scope.ServiceProvider.GetRequiredService<ICurrentMunicipalityAccessor>().Set(policy.MunicipalityId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Municipalities.SingleAsync(x => x.Id == policy.MunicipalityId);
        // The restored Cantilan data is an explicitly authorized, isolated demo copy. Never allow this
        // exception without the exact tenant/facility identity, a TEST-only reference, and the loopback
        // database/port gate in AddWorkingCopyNpmMonthly above. Real LGU deployment stays disabled.
        var restoredDemoOnly = tenant.Id == Guid.Parse("0ea98ba6-f7b1-4928-96f1-6177d45741bd")
            && tenant.TenantCode == "cantilan-sds"
            && policy.FacilityId == Guid.Parse("67c13967-de75-4b31-8939-2c7c04e99e71")
            && policy.ApprovalReference.StartsWith("TEST-WORKCOPY-CANTILAN-", StringComparison.Ordinal);
        if (!(tenant.TenantCode.StartsWith("npm-test-", StringComparison.Ordinal) || restoredDemoOnly)
            || !await db.Facilities.AnyAsync(x => x.Id == policy.FacilityId && x.MunicipalityId == policy.MunicipalityId
                && x.Code == FacilityCode.NPM && x.Archetype == BillingArchetype.DailyStall && x.MonthBasis == NpmMonthBasis.RentGoal)
            || !await db.AdminUsers.AnyAsync(x => x.Id == policy.ApprovedByUserId && x.MunicipalityId == policy.MunicipalityId
                && x.Role == AdminRole.SuperAdmin && x.IsActive)
            || !(await db.Database.GetAppliedMigrationsAsync()).Contains("20261020050758_AddNpmRentMonthFinancialSource20261020"))
            throw new InvalidOperationException("NPM test activation refused: synthetic tenant, facility, Head or installed financial migration evidence is missing.");
        // Select all four typed columns before the server can listen; migration history alone is insufficient.
        _ = await db.NpmRentMonths.Select(x => new { x.SettlementActivatedAtUtc, x.SettlementActivatedByUserId, x.SettlementSealedAtUtc }).Take(1).ToListAsync();
        _ = await db.CollectionAllocations.Select(x => x.NpmRentMonthId).Take(1).ToListAsync();
        app.Logger.LogWarning("Working-copy TEST NPM monthly policy enabled for tenant {Tenant} / facility {Facility} only; reference {Reference}.",
            policy.MunicipalityId, policy.FacilityId, policy.ApprovalReference);
    }
}
