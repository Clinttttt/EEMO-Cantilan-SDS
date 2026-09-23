using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Phase1;

public sealed class RevenueClassificationTenantFilterTests
{
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    [Fact]
    public async Task ClassificationAndPolicyAreVisibleOnlyToTheirTenantAndFailClosedWhenUnresolved()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"revenue-tenant-{Guid.NewGuid()}")
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;

        await using (var asA = new AppDbContext(options, new FixedTenant(tenantA)))
        {
            var classification = RevenueClassification.Create("MARKET_FEES");
            asA.RevenueClassifications.Add(classification);
            asA.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
                classification.Id, new DateOnly(2026, 9, 1), "Tenant A market fees", RevenueInstrumentType.CashTicket));
            await asA.SaveChangesAsync();
        }

        await using (var asB = new AppDbContext(options, new FixedTenant(tenantB)))
        {
            var classification = RevenueClassification.Create("MARKET_FEES");
            asB.RevenueClassifications.Add(classification);
            asB.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
                classification.Id, new DateOnly(2026, 9, 1), "Tenant B market fees", RevenueInstrumentType.OfficialReceipt));
            await asB.SaveChangesAsync();
        }

        await using (var asA = new AppDbContext(options, new FixedTenant(tenantA)))
        {
            Assert.Equal("MARKET_FEES", (await asA.RevenueClassifications.SingleAsync()).SemanticCode);
            Assert.Equal("Tenant A market fees", (await asA.RevenueClassificationPolicies.SingleAsync()).DisplayName);
        }

        await using (var asB = new AppDbContext(options, new FixedTenant(tenantB)))
        {
            Assert.Equal("MARKET_FEES", (await asB.RevenueClassifications.SingleAsync()).SemanticCode);
            Assert.Equal("Tenant B market fees", (await asB.RevenueClassificationPolicies.SingleAsync()).DisplayName);
        }

        await using var unresolved = new AppDbContext(options, new FixedTenant(Guid.Empty));
        Assert.Empty(await unresolved.RevenueClassifications.ToListAsync());
        Assert.Empty(await unresolved.RevenueClassificationPolicies.ToListAsync());
    }
}
