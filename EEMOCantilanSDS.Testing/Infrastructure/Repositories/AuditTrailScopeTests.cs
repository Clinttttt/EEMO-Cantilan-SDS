using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
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

    /// <summary>An empty lookup: nothing related is resolvable, so only the snapshot's own facts appear.</summary>
    private static AuditDetailComposer.Lookup NoLookup() => new(
        new Dictionary<Guid, AuditDetailComposer.StallRef>(),
        new Dictionary<Guid, string>(),
        new Dictionary<string, string>());

    /// <summary>A municipality with a known id, so a snapshot can reference it.</summary>
    private static void SeedMunicipality(AppDbContext ctx, Guid id, string code, string name)
    {
        var lgu = Municipality.Create(code, name, "Surigao del Sur", MunicipalityStatus.Active);
        ctx.Municipalities.Add(lgu);
        ctx.Entry(lgu).Property(nameof(Municipality.Id)).CurrentValue = id;
    }

    private static void SeedAdmin(AppDbContext ctx, Guid municipalityId, string username, string fullName)    {
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
    public void EnumColumns_ReadAsNamesNotNumbers()
    {
        // A snapshot stores an enum as its number, so "Status 1 → 3" was what an auditor would have read.
        var changes = AuditDetailComposer.Changes(
            "{\"Status\":1,\"Role\":2}",
            "{\"Status\":3,\"Role\":1}");

        Assert.Contains(changes, c => c == "Status Unpaid → Paid");
        Assert.Contains(changes, c => c == "Role Administrator → Head");
    }

    [Fact]
    public void ASlaughterCollection_NamesTheAnimalNotItsEnumNumber()
    {
        var lookup = NoLookup();

        var details = AuditDetailComposer.Describe(
            "Created", "SlaughterTransaction", Guid.NewGuid(),
            "{\"OwnerName\":\"Ana Reyes\",\"AnimalType\":1,\"NumberOfHeads\":2," +
            "\"TransactionDate\":\"2026-07-28\",\"SlaughterFee\":500.00,\"ORNumber\":\"98123\"}",
            null, lookup);

        Assert.Contains("Ana Reyes", details);
        Assert.Contains("Hog ×2", details);
        Assert.Contains("Jul 28, 2026", details);
        Assert.Contains("₱500.00", details);
        Assert.Contains("OR 98123", details);
    }

    [Fact]
    public async Task AnEventAboutAnotherMunicipalitysAccount_NeverAppearsHere_EvenWhenTheActorIsSystem()
    {
        // The leak that survived actor scoping: the actor was "system" (unattributable, therefore kept) while the
        // SUBJECT was another LGU's Head. Every snapshot carries the record's own MunicipalityId, so a row naming
        // a different municipality is excluded regardless of how it was stamped or who wrote it.
        var options = Options();
        var cantilan = Guid.NewGuid();
        var carrascal = Guid.NewGuid();

        using (var seed = new AppDbContext(options, new FixedMunicipality(cantilan)))
        {
            SeedMunicipality(seed, cantilan, "CANTILAN", "Cantilan");
            SeedMunicipality(seed, carrascal, "CARRASCAL", "Carrascal");

            var ours = AuditLog.Create("id", "system", "system", "Updated", "AdminUser", Guid.NewGuid(),
                null, $"{{\"FullName\":\"Juan Dela Cruz\",\"Username\":\"head\",\"Role\":1,\"MunicipalityId\":\"{cantilan}\"}}");
            var theirs = AuditLog.Create("id", "system", "system", "Updated", "AdminUser", Guid.NewGuid(),
                null, $"{{\"FullName\":\"Personal Unos\",\"Username\":\"carrascal.head\",\"Role\":1,\"MunicipalityId\":\"{carrascal}\"}}");

            seed.AuditLogs.AddRange(ours, theirs);
            Stamp(seed, ours, cantilan);
            Stamp(seed, theirs, cantilan);   // mis-stamped, exactly as in production
            await seed.SaveChangesAsync();
        }

        using var ctx = new AppDbContext(options, new FixedMunicipality(cantilan));
        var trail = await new AuditRepository(ctx, new FixedMunicipality(cantilan))
            .GetAuditTrailAsync(null, null, null, null, null, null, 1, 25, true, CancellationToken.None);

        var row = Assert.Single(trail.Items);
        Assert.Contains("Juan Dela Cruz", row.Details);
        Assert.DoesNotContain(trail.Items, i => i.Details.Contains("carrascal.head"));
        Assert.Equal(1, trail.TotalEvents);
    }

    [Fact]
    public void AnAccountEvent_ReadsAsASentence_NotADatabaseRow()
    {
        // It used to read "Updated the staff account of Juan Dela Cruz · head · Head".
        var lookup = NoLookup();

        var admin = AuditDetailComposer.Describe("Updated", "AdminUser", Guid.NewGuid(),
            "{\"FullName\":\"Juan Dela Cruz\",\"Username\":\"head\",\"Role\":1}", null, lookup);
        Assert.Equal("Updated the staff account of Juan Dela Cruz (Head)", admin);

        var collector = AuditDetailComposer.Describe("Created", "CollectorUser", Guid.NewGuid(),
            "{\"FullName\":\"Ana Reyes\",\"Username\":\"ana\"}", null, lookup);
        Assert.Equal("Created the collector account of Ana Reyes", collector);

        // No name recorded → the username stands in, rather than an empty sentence.
        var unnamed = AuditDetailComposer.Describe("Updated", "AdminUser", Guid.NewGuid(),
            "{\"Username\":\"madrid.head\"}", null, lookup);
        Assert.Equal("Updated the staff account of madrid.head", unnamed);
    }

    [Fact]
    public void FacilityAndSectionWording_ComesFromTheLguNotFromCantilan()
    {
        // Nothing in the trail's text may assume Cantilan's naming: another municipality's market or terminal is
        // called something else, and an office that renamed its sections must read its own labels.
        var lookup = new AuditDetailComposer.Lookup(
            new Dictionary<Guid, AuditDetailComposer.StallRef>(),
            new Dictionary<Guid, string>(),
            new Dictionary<string, string> { ["TRM"] = "Madrid Integrated Terminal" },
            new Dictionary<int, string> { [1] = "Gulayan", [3] = "Karne" });

        var trip = AuditDetailComposer.Describe("Created", "TrmTrip", Guid.NewGuid(),
            "{\"DriverName\":\"Dante Amas\",\"TripNumber\":2,\"Route\":\"Surigao-Cortes\",\"Fee\":30.00}",
            null, lookup);

        Assert.StartsWith("Recorded a trip for Madrid Integrated Terminal", trip);
        Assert.DoesNotContain("Tabo-an", trip);

        var changes = AuditDetailComposer.Changes("{\"Section\":1}", "{\"Section\":3}", lookup);
        Assert.Contains(changes, c => c == "Section Gulayan → Karne");
    }

    [Fact]
    public void AnUnknownEntity_StillReadsAsEnglish()
    {
        var lookup = NoLookup();

        var details = AuditDetailComposer.Describe("Created", "TenantBackup", Guid.NewGuid(), null, null, lookup);

        Assert.Equal("Created tenant backup", details);
    }
}
