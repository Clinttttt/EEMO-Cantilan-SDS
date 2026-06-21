using EEMOCantilanSDS.Domain.Entities.Audit;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

public class AuditRepositoryTests
{
    private static AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static AuditLog Log(
        string action,
        string entityType = "PaymentRecord",
        string actor = "Maria Santos",
        string role = "Admin",
        string? notes = null) =>
        AuditLog.Create("actor-1", actor, role, action, entityType, Guid.NewGuid(), null, null, null, notes);

    [Fact]
    public async Task ActionFilter_ScopesItems_But_Breakdown_Covers_WholeScope()
    {
        using var ctx = NewContext();
        ctx.AuditLogs.AddRange(
            Log("Created"), Log("Created"), Log("Created"),
            Log("Updated"), Log("Updated"),
            Log("Deleted"));
        await ctx.SaveChangesAsync();

        var repo = new AuditRepository(ctx);
        var result = await repo.GetAuditTrailAsync(
            search: null, action: "Created", entityType: null, actor: null,
            fromUtc: null, toUtc: null, page: 1, pageSize: 25, includeOptions: true, ct: default);

        // Action filter scopes the listed rows + pagination total...
        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal("Created", i.Action));

        // ...but the summary breakdown reflects the whole (non-action) scope.
        Assert.Equal(3, result.CreatedCount);
        Assert.Equal(2, result.UpdatedCount);
        Assert.Equal(1, result.DeletedCount);
        Assert.Equal(6, result.TotalEvents);
    }

    [Fact]
    public async Task Paginates_With_TotalPages()
    {
        using var ctx = NewContext();
        for (var i = 0; i < 23; i++) ctx.AuditLogs.Add(Log("Created"));
        await ctx.SaveChangesAsync();

        var repo = new AuditRepository(ctx);
        var page2 = await repo.GetAuditTrailAsync(
            null, null, null, null, null, null, page: 2, pageSize: 10, includeOptions: false, ct: default);

        Assert.Equal(23, page2.TotalCount);
        Assert.Equal(3, page2.TotalPages);
        Assert.Equal(2, page2.Page);
        Assert.Equal(10, page2.Items.Count);

        // includeOptions:false skips the dropdown scans.
        Assert.Empty(page2.Actors);
        Assert.Empty(page2.EntityTypes);
    }

    [Fact]
    public async Task Filters_By_Entity_Actor_And_Search()
    {
        using var ctx = NewContext();
        ctx.AuditLogs.AddRange(
            Log("Created", entityType: "PaymentRecord", actor: "Maria Santos"),
            Log("Updated", entityType: "SlaughterTransaction", actor: "Juan Dela Cruz"),
            Log("Deleted", entityType: "PaymentRecord", actor: "Juan Dela Cruz", notes: "Removed duplicate receipt"));
        await ctx.SaveChangesAsync();

        var repo = new AuditRepository(ctx);

        var byEntity = await repo.GetAuditTrailAsync(null, null, "PaymentRecord", null, null, null, 1, 25, true, default);
        Assert.Equal(2, byEntity.TotalCount);
        Assert.All(byEntity.Items, i => Assert.Equal("PaymentRecord", i.EntityType));

        var byActor = await repo.GetAuditTrailAsync(null, null, null, "Juan Dela Cruz", null, null, 1, 25, true, default);
        Assert.Equal(2, byActor.TotalCount);
        Assert.All(byActor.Items, i => Assert.Equal("Juan Dela Cruz", i.ActorName));

        // Search hits the Notes column (case-insensitive).
        var bySearch = await repo.GetAuditTrailAsync("duplicate", null, null, null, null, null, 1, 25, true, default);
        Assert.Equal(1, bySearch.TotalCount);

        // Distinct filter options surfaced for the dropdowns (Actors are value/label options).
        Assert.Contains(byEntity.Actors, o => o.Value == "Maria Santos");
        Assert.Contains("SlaughterTransaction", byActor.EntityTypes);
    }

    [Fact]
    public async Task Numeric_Payor_Actors_Excluded_From_Actor_Options()
    {
        using var ctx = NewContext();
        ctx.AuditLogs.AddRange(
            Log("Created", actor: "09384326762"),   // payor self-service (mobile-number username)
            Log("Updated", actor: "admin"),
            Log("Created", actor: "personalsingko"));
        await ctx.SaveChangesAsync();

        var repo = new AuditRepository(ctx);
        var result = await repo.GetAuditTrailAsync(null, null, null, null, null, null, 1, 25, true, default);

        Assert.DoesNotContain(result.Actors, o => o.Value == "09384326762");
        Assert.Contains(result.Actors, o => o.Value == "admin");
        Assert.Contains(result.Actors, o => o.Value == "personalsingko");
    }

    [Fact]
    public async Task Resolves_Staff_Username_To_Full_Name()
    {
        using var ctx = NewContext();
        ctx.AdminUsers.Add(AdminUser.Create("Juan Dela Cruz", "admin", "admin@eemo.gov.ph", "P@ssw0rd!", AdminRole.SuperAdmin));
        ctx.AuditLogs.Add(Log("Created", actor: "admin", role: "SuperAdmin"));
        await ctx.SaveChangesAsync();

        var repo = new AuditRepository(ctx);
        var result = await repo.GetAuditTrailAsync(null, null, null, null, null, null, 1, 25, true, default);

        // Row shows the resolved full name (not the username).
        Assert.Equal("Juan Dela Cruz", Assert.Single(result.Items).ActorDisplayName);
        // The dropdown option keeps the username as the value but labels it with the full name.
        Assert.Contains(result.Actors, o => o.Value == "admin" && o.Label == "Juan Dela Cruz");
    }

    [Fact]
    public async Task Search_Matches_Staff_Full_Name_Not_Just_Username()
    {
        using var ctx = NewContext();
        ctx.AdminUsers.Add(AdminUser.Create("Clint Villanueva", "admin", "clint@eemo.gov.ph", "P@ssw0rd!", AdminRole.SuperAdmin));
        ctx.AuditLogs.AddRange(
            Log("Created", actor: "admin"),                 // Clint
            Log("Created", actor: "personalsingko"));       // someone else
        await ctx.SaveChangesAsync();

        var repo = new AuditRepository(ctx);

        // Searching the display name finds the row even though the table stores "admin".
        var byName = await repo.GetAuditTrailAsync("Clint", null, null, null, null, null, 1, 25, true, default);
        Assert.Equal(1, byName.TotalCount);
        Assert.Equal("admin", Assert.Single(byName.Items).ActorName);

        // Username search still works too.
        var byUsername = await repo.GetAuditTrailAsync("personalsingko", null, null, null, null, null, 1, 25, true, default);
        Assert.Equal(1, byUsername.TotalCount);
    }
}
