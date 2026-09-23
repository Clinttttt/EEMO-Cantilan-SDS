using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class RevenueClassificationPersistenceTests(PostgresFixture db)
{
    private async Task<Guid> CreateMunicipalityAsync(string code)
    {
        var municipality = Municipality.Create(code, code, "Surigao del Sur", MunicipalityStatus.Active,
            tenantCode: code.ToLowerInvariant());
        await using var context = db.CreateContext(Guid.Empty);
        context.Municipalities.Add(municipality);
        await context.SaveChangesAsync();
        return municipality.Id;
    }

    [SkippableFact]
    public async Task ClassificationAndPolicyReadsAreTenantScopedAndUnresolvedTenantFailsClosed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenantA = await CreateMunicipalityAsync("REV-A");
        var tenantB = await CreateMunicipalityAsync("REV-B");

        Guid classificationAId;
        await using (var asA = db.CreateContext(tenantA))
        {
            var classification = RevenueClassification.Create("MARKET_FEES");
            classificationAId = classification.Id;
            asA.RevenueClassifications.Add(classification);
            asA.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
                classification.Id, new DateOnly(2026, 9, 1), "Tenant A market fees", RevenueInstrumentType.CashTicket));
            await asA.SaveChangesAsync();
        }

        await using (var asB = db.CreateContext(tenantB))
        {
            var classification = RevenueClassification.Create("MARKET_FEES");
            asB.RevenueClassifications.Add(classification);
            asB.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
                classification.Id, new DateOnly(2026, 9, 1), "Tenant B market receipts", RevenueInstrumentType.OfficialReceipt));
            await asB.SaveChangesAsync();
        }

        await using (var asA = db.CreateContext(tenantA))
        {
            var classification = await asA.RevenueClassifications.SingleAsync();
            var policy = await asA.RevenueClassificationPolicies.SingleAsync();
            Assert.Equal("MARKET_FEES", classification.SemanticCode);
            Assert.Equal("Tenant A market fees", policy.DisplayName);
            Assert.Equal(RevenueInstrumentType.CashTicket, policy.PermittedInstrumentType);
            Assert.Null(await asA.RevenueClassifications.FirstOrDefaultAsync(x => x.Id != classificationAId));
        }

        await using (var asB = db.CreateContext(tenantB))
        {
            Assert.Equal("Tenant B market receipts", (await asB.RevenueClassificationPolicies.SingleAsync()).DisplayName);
            Assert.Equal(RevenueInstrumentType.OfficialReceipt,
                (await asB.RevenueClassificationPolicies.SingleAsync()).PermittedInstrumentType);
        }

        await using var unresolved = db.CreateContext(Guid.Empty);
        Assert.Empty(await unresolved.RevenueClassifications.ToListAsync());
        Assert.Empty(await unresolved.RevenueClassificationPolicies.ToListAsync());
    }

    [SkippableFact]
    public async Task DatabaseRejectsDuplicateIdentityWithinTenantButAllowsItAcrossTenants()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenantA = await CreateMunicipalityAsync("REV-C");
        var tenantB = await CreateMunicipalityAsync("REV-D");

        await using (var asA = db.CreateContext(tenantA))
        {
            asA.RevenueClassifications.Add(RevenueClassification.Create("ECF"));
            await asA.SaveChangesAsync();
        }

        await using (var asB = db.CreateContext(tenantB))
        {
            asB.RevenueClassifications.Add(RevenueClassification.Create("ECF"));
            await asB.SaveChangesAsync();
        }

        await using var duplicate = db.CreateContext(tenantA);
        duplicate.RevenueClassifications.Add(RevenueClassification.Create("ECF"));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task DatabaseRejectsDuplicatePolicyEffectiveDateWithinTenant()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await CreateMunicipalityAsync("REV-G");
        var effectiveDate = new DateOnly(2026, 9, 1);
        Guid classificationId;

        await using (var context = db.CreateContext(tenant))
        {
            var classification = RevenueClassification.Create("ECF");
            classificationId = classification.Id;
            context.RevenueClassifications.Add(classification);
            context.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
                classification.Id, effectiveDate, "ECF", RevenueInstrumentType.OfficialReceipt));
            await context.SaveChangesAsync();
        }

        await using var duplicate = db.CreateContext(tenant);
        duplicate.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
            classificationId, effectiveDate, "Changed same-day policy", RevenueInstrumentType.CashTicket));
        await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
    }

    [SkippableFact]
    public async Task DatabaseRejectsPolicyLinkedAcrossTenantBoundary()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenantA = await CreateMunicipalityAsync("REV-E");
        var tenantB = await CreateMunicipalityAsync("REV-F");

        Guid tenantAClassificationId;
        await using (var asA = db.CreateContext(tenantA))
        {
            var classification = RevenueClassification.Create("ECF");
            tenantAClassificationId = classification.Id;
            asA.RevenueClassifications.Add(classification);
            await asA.SaveChangesAsync();
        }

        await using var asB = db.CreateContext(tenantB);
        asB.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
            tenantAClassificationId, new DateOnly(2026, 9, 1), "Cross tenant", RevenueInstrumentType.OfficialReceipt));
        await Assert.ThrowsAsync<DbUpdateException>(() => asB.SaveChangesAsync());
    }
}
