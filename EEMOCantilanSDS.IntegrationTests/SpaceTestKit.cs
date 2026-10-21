using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>Shared fixtures for the physical-space identity and space-lifecycle tests: a tenant, occupancies on a registration, a workflow with a stated caller.</summary>
internal static class SpaceTestKit
{
    public static readonly DateOnly Today = new(2026, 10, 19);

    public sealed class BusinessClock(DateOnly today) : IClock
    {
        public DateOnly PhilippineToday => today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => today.ToDateTime(TimeOnly.MinValue);
    }

    public sealed class Caller(Guid tenantId, string? role, bool authenticated = true) : ICurrentUserService
    {
        public bool IsAuthenticated => authenticated;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => authenticated ? Guid.Parse("22222222-2222-2222-2222-222222222222") : null;
        public string? Username => "lifecycle-test";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "lifecycle-test";
        public Guid? MunicipalityId => tenantId;
    }

    public sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    public static SpaceLifecycleWorkflow Workflow(AppDbContext context, Guid tenant, string? role = "SuperAdmin", Guid? claimedTenant = null, bool authenticated = true) =>
        new(context, new Caller(claimedTenant ?? tenant, role, authenticated), new Tenant(tenant), new BusinessClock(Today));

    public static async Task<Guid> SeedTenantAsync(PostgresFixture db, string name)
    {
        var municipality = Municipality.Create($"SP-{Guid.NewGuid():N}"[..24], name, "Surigao del Sur", MunicipalityStatus.Active, tenantCode: $"sp-{Guid.NewGuid():N}"[..32]);
        await using var setup = db.CreateContext(Guid.Empty);
        setup.Municipalities.Add(municipality);
        await setup.SaveChangesAsync();
        return municipality.Id;
    }

    public sealed record Occupancy(SpaceRegistration Registration, ObligationAccount Account, ObligationRate Rate);

    /// <summary>
    /// One occupancy on a (possibly new) space: the account, its approved amount from its first day, and optionally its closure. Saved immediately.
    /// </summary>
    public static async Task<Occupancy> AddAsync(PostgresFixture db, Guid tenant, string number, DateOnly from, DateOnly? closedOn = null, string holder = "Ana Reyes",
        decimal amount = 1200m, ObligationKind kind = ObligationKind.KanmanggaySpaceRental, SpaceRegistration? onSpace = null, bool signed = false, bool register = true, DateOnly? rateFrom = null)
    {
        var registration = onSpace ?? (register ? SpaceRegistration.Create(tenant, kind, number, "test") : null);
        var account = ObligationAccount.Create(tenant, kind, Guid.Empty, null, number, null, null, from, "test", actualOccupant: holder);
        if (signed)
        {
            account.SetOccupancyBasis(OccupancyArrangement.SignedContract, "KM-REF");
            account.SetLeaseDetails("Signed Lessee", from, 3, null, null);
        }
        var rate = ObligationRate.Create(tenant, account.Id, rateFrom ?? from, amount, "test");
        await using var context = db.CreateContext(tenant);
        if (registration is not null)
        {
            if (onSpace is null) context.SpaceRegistrations.Add(registration);
            account.LinkRegistration(registration.Id);
        }
        if (closedOn is { } closed) account.Close(closed);
        context.ObligationAccounts.Add(account);
        context.ObligationRates.Add(rate);
        await context.SaveChangesAsync();
        return new(registration!, account, rate);
    }

    /// <summary>An assessed, unpaid month on an account: a debt that must stay with the occupancy that owes it.</summary>
    public static async Task AssessAsync(PostgresFixture db, Guid tenant, ObligationAccount account, ObligationRate rate, DateOnly periodStart)
    {
        await using var context = db.CreateContext(tenant);
        context.ObligationPeriods.Add(ObligationPeriod.Assess(account, rate, periodStart, "test"));
        await context.SaveChangesAsync();
    }
}
