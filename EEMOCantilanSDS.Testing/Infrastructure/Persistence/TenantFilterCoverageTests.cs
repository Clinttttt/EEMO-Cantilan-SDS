using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Every tenant-owned table must be filtered, without anyone remembering to arrange it.
///
/// <para>
/// The isolation tests elsewhere prove the filter WORKS on the entities it is applied to. This one asks the different and
/// more dangerous question: is it applied to all of them? Filters are attached by walking the model, so a new tenant-owned
/// entity is covered by construction today — but "by construction" is a property of one method that anyone may edit, and the
/// failure mode is silent. An unfiltered table does not throw; it just answers with every LGU's rows.
/// </para>
///
/// <para>
/// The second test guards a hole the first cannot see. Filters are attached to TPH ROOTS only, which is correct because a
/// derived type inherits its root's filter. It also means a tenant-owned type whose root is NOT tenant-owned would receive
/// no filter at all, from either end. Nothing like that exists today and nothing prevents it.
/// </para>
/// </summary>
public class TenantFilterCoverageTests
{
    private sealed class NoTenant : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => Guid.Empty;
        public void Set(Guid municipalityId) { }
    }

    /// <summary>The real model, built as production builds it.</summary>
    private static IModel Model()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"filter-coverage-{Guid.NewGuid()}")
            .Options;

        using var context = new AppDbContext(options, new NoTenant());
        return context.Model;
    }

    private static bool IsTenantOwned(Type clr) => typeof(IMunicipalityOwned).IsAssignableFrom(clr);

    /// <summary>
    /// Whether the entity's filter actually mentions the municipality.
    ///
    /// <para>
    /// Asking only whether a filter EXISTS is not enough, and this test was written that way at first and passed while a
    /// tenant-owned entity was deliberately excluded from the tenant clause. Soft-deletable entities always receive a
    /// filter — <c>!IsDeleted</c> — so "has a filter" is true of them whether or not they are isolated by LGU. The filter
    /// has to be read.
    /// </para>
    /// </summary>
    private static bool FiltersByMunicipality(IEntityType type) =>
        type.GetQueryFilter()?.ToString().Contains(nameof(IMunicipalityOwned.MunicipalityId)) == true;

    [Fact]
    public void EveryTenantOwnedEntityIsFiltered()
    {
        var unfiltered = Model().GetEntityTypes()
            .Where(t => t.BaseType is null && IsTenantOwned(t.ClrType))
            .Where(t => !FiltersByMunicipality(t))
            .Select(t => t.ClrType.Name)
            .OrderBy(n => n)
            .ToList();

        Assert.True(unfiltered.Count == 0,
            "These tenant-owned entities are not filtered by municipality, so they would answer with every LGU's rows: "
            + string.Join(", ", unfiltered));
    }

    [Fact]
    public void NoTenantOwnedEntityHidesUnderANonTenantOwnedRoot()
    {
        var orphans = Model().GetEntityTypes()
            .Where(t => IsTenantOwned(t.ClrType))
            .Where(t =>
            {
                var root = t;
                while (root.BaseType is not null) root = root.BaseType;
                return !IsTenantOwned(root.ClrType);
            })
            .Select(t => t.ClrType.Name)
            .OrderBy(n => n)
            .ToList();

        Assert.True(orphans.Count == 0,
            "These tenant-owned entities inherit from a root that is not tenant-owned, so no filter is applied at either "
            + "level: " + string.Join(", ", orphans));
    }

    [Fact]
    public void TheCheckWouldNoticeAnUnfilteredTenantOwnedEntity()
    {
        // Guards the guard. Both tests above pass trivially if the model contains nothing tenant-owned, or if the
        // IMunicipalityOwned lookup silently stops matching — so assert that the model really is full of such entities.
        var owned = Model().GetEntityTypes()
            .Where(t => t.BaseType is null && IsTenantOwned(t.ClrType))
            .ToList();

        Assert.True(owned.Count >= 10,
            $"Expected the model to be largely tenant-owned; found only {owned.Count} such root entities.");
    }
}
