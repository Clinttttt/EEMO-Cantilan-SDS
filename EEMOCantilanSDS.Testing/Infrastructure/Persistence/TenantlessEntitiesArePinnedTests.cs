using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// An entity that is not <see cref="IMunicipalityOwned"/> gets no municipality filter at all, so any handler loading it by id
/// is cross-tenant. TenantFilterCoverageTests proves owned entities are filtered; this proves the set of entities that
/// escape that rule is only the platform's own (ST-11). A new table must be tenant-owned or be added here on purpose.
/// </summary>
public class TenantlessEntitiesArePinnedTests
{
    private sealed class NoTenant : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => Guid.Empty;
        public void Set(Guid municipalityId) { }
    }

    [Fact]
    public void OnlyPlatformLevelEntitiesAreWithoutAMunicipality()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"pin-{Guid.NewGuid()}").Options;
        using var context = new AppDbContext(options, new NoTenant());

        var tenantless = context.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && !t.IsOwned() && !typeof(IMunicipalityOwned).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        // Municipality is the tenant itself; AssessmentRequest and OnboardingDraft are the platform operator's intake queue.
        Assert.Equal(new[] { "AssessmentRequest", "Municipality", "OnboardingDraft" }, tenantless);
    }
}
