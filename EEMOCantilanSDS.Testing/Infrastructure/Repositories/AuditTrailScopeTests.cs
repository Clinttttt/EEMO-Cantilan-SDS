using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Audit;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// The Audit Trail is a per-LGU record. Audit rows are stamped with whatever tenant was current when they were
/// written, and some flows write them under the default tenant — a platform operator creating another LGU's
/// Head, or a token-less auth step — so another municipality's staff were appearing in Cantilan's actor filter.
/// These cover the scoping and the plain-language details.
/// </summary>
public class AuditTrailScopeTests
{
    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private static DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static void Stamp(AppDbContext ctx, object entity, Guid municipalityId) =>
        ctx.Entry(entity).Property(nameof(IMunicipalityOwned.MunicipalityId)).CurrentValue = municipalityId;

    private static void SeedAdmin(AppDbContext ctx, Guid municipalityId, string username, string fullName)
    {
        var admin = AdminUser.Create(fullName, username, $"{username}@lgu.gov", "Secret123!", AdminRole.SuperAdmin);
        ctx.AdminUsers.Add(admin);
        Stamp(ctx, admin, municipalityId);
    }

    private static void SeedEvent(AppDbContext ctx, Guid municipalityId, string actor, string entityType = "AdminAccount")
    {
        var log = AuditLog.Create("id", actor, "SuperAdmin", "Updated", entityType);
        ctx.AuditLogs.Add(log);
        Stamp(ctx, log, municipalityId);
    }

    [Fact]
    public async Task AnotherMunicipalitysStaff_NeverAppearInThisOfficesTrail()
    {
        var options = Options();
        var cantilan = Guid.NewGuid();
        var madrid = Guid.NewGuid();

        using (var seed = new AppDbContext(options, new FixedMunicipality(cantilan)))
        {
            SeedAdmin(seed, cantilan, "cantilan.head", "Juan Dela Cruz");
            SeedAdmin(seed, madrid, "madrid.head", "Cly Head2");

            // Both rows are stamped Cantilan — exactly the mis-attribution the page has to survive.
            SeedEvent(seed, cantilan, "cantilan.head");
            SeedEvent(seed, cantilan, "madrid.head");
            SeedEvent(seed, cantilan, "system");
            await seed.SaveChangesAsync();
        }

        using var ctx = new AppDbContext(options, new FixedMunicipality(cantilan));
        var trail = await new AuditRepository(ctx, new FixedMunicipality(cantilan))
            .GetAuditTrailAsync(null, null, null, null, null, null, 1, 25, true, CancellationToken.None);

        Assert.DoesNotContain(trail.Items, i => i.ActorName == "madrid.head");
        Assert.Contains(trail.Items, i => i.ActorName == "cantilan.head");
        // "system" is unattributable and must be kept: an audit trail may not silently drop events.
        Assert.Contains(trail.Items, i => i.ActorName == "system");

        Assert.DoesNotContain(trail.Actors, o => o.Value == "madrid.head");
        Assert.Contains(trail.Actors, o => o.Label == "Juan Dela Cruz");
        Assert.Equal(2, trail.TotalEvents);
    }

    [Fact]
    public async Task AnUnresolvedTenant_ChangesNothing()
    {
        // Tests and token-less paths run with an empty tenant; the trail must behave exactly as before.
        var options = Options();
        var cantilan = Guid.NewGuid();

        using (var seed = new AppDbContext(options, new FixedMunicipality(Guid.Empty)))
        {
            SeedAdmin(seed, cantilan, "cantilan.head", "Juan Dela Cruz");
            SeedEvent(seed, cantilan, "cantilan.head");
            await seed.SaveChangesAsync();
        }

        using var ctx = new AppDbContext(options, new FixedMunicipality(Guid.Empty));
        var trail = await new AuditRepository(ctx, new FixedMunicipality(Guid.Empty))
            .GetAuditTrailAsync(null, null, null, null, null, null, 1, 25, true, CancellationToken.None);

        Assert.Single(trail.Items);
    }

    [Fact]
    public async Task APayment_IsDescribedByPayorStallFacilityAndSection()
    {
        // The point of the change: "Updated PaymentRecord" told an auditor nothing.
        var options = Options();
        var lgu = Guid.NewGuid();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM");
        var stall = Stall.Create(facility.Id, "12", 900m, ApplicableFees.DailyRental, section: MarketSection.MeatSection);
        var contract = Contract.Create(stall.Id, "Juan Dela Cruz", "Juan Dela Cruz", new DateOnly(2026, 1, 1), 3, 900m);

        using (var seed = new AppDbContext(options, new FixedMunicipality(lgu)))
        {
            seed.Facilities.Add(facility); Stamp(seed, facility, lgu);
            seed.Stalls.Add(stall); Stamp(seed, stall, lgu);
            seed.Contracts.Add(contract); Stamp(seed, contract, lgu);

            var snapshot = $"{{\"StallId\":\"{stall.Id}\",\"BillingYear\":2026,\"BillingMonth\":7," +
                           "\"AmountPaid\":900.00,\"ORNumber\":\"564694\"}";
            var log = AuditLog.Create("id", "cantilan.head", "SuperAdmin", "Created", "PaymentRecord",
                Guid.NewGuid(), null, snapshot);
            seed.AuditLogs.Add(log); Stamp(seed, log, lgu);

            await seed.SaveChangesAsync();
        }

        using var ctx = new AppDbContext(options, new FixedMunicipality(lgu));
        var trail = await new AuditRepository(ctx, new FixedMunicipality(lgu))
            .GetAuditTrailAsync(null, null, null, null, null, null, 1, 25, false, CancellationToken.None);

        var details = Assert.Single(trail.Items).Details;
        Assert.Contains("Recorded a payment for", details);
        Assert.Contains("Juan Dela Cruz", details);
        Assert.Contains("Stall 12", details);
        Assert.Contains("New Public Market", details);
        Assert.Contains("Meat Area", details);
        Assert.Contains("July 2026", details);
        Assert.Contains("₱900.00", details);
        Assert.Contains("OR 564694", details);
    }

    [Fact]
    public void AnUpdate_NamesTheFieldsThatChanged()
    {
        var before = "{\"MonthlyRate\":900.00,\"Status\":\"Unpaid\"}";
        var after = "{\"MonthlyRate\":1200.00,\"Status\":\"Paid\"}";

        var changes = AuditDetailComposer.Changes(before, after);

        Assert.Contains(changes, c => c.StartsWith("Monthly rate") && c.Contains("900") && c.Contains("1200"));
        Assert.Contains(changes, c => c == "Status Unpaid → Paid");
    }

    [Fact]
    public void AnUnknownEntity_StillReadsAsEnglish()
    {
        var lookup = new AuditDetailComposer.Lookup(
            new Dictionary<Guid, AuditDetailComposer.StallRef>(),
            new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>());

        var details = AuditDetailComposer.Describe("Created", "TenantBackup", Guid.NewGuid(), null, null, lookup);

        Assert.Equal("Created tenant backup", details);
    }
}
