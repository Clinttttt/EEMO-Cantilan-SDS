using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// PostgreSQL proof for governed configurable services: the widened line-source constraint, tenant-consistent keys,
/// the amount-shape constraint, and that one physical document yields exactly one Collection under concurrency.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GovernedServicePersistenceTests(PostgresFixture db)
{
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Caller(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "pg-test";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "pg-governed";
        public Guid? MunicipalityId => tenantId;
    }

    private static Municipality NewTenant(string label) => Municipality.Create($"{label}-{Guid.NewGuid():N}"[..12], $"Governed {label}",
        "Province", MunicipalityStatus.Active, tenantCode: $"governed-{label}-{Guid.NewGuid():N}"[..28].ToLowerInvariant());

    [SkippableFact]
    public async Task ServiceIdentityIsUniquePerTenant_AndSettingsCannotPointAtAnotherTenantsService()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var a = NewTenant("a");
        var b = NewTenant("b");
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(a, b);
            await setup.SaveChangesAsync();
        }

        GovernedService serviceA;
        await using (var ctx = db.CreateContext(a.Id))
        {
            serviceA = GovernedService.Create(a.Id, CollectorOperationCodes.MarketFees, "head");
            ctx.Add(serviceA);
            ctx.Add(GovernedServiceSetting.Create(a.Id, serviceA.Id, new DateOnly(2026, 1, 1),
                GovernedServiceBasis.FixedAmount, 30m, null, true, true, "head"));
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = db.CreateContext(a.Id))
        {
            ctx.Add(GovernedService.Create(a.Id, CollectorOperationCodes.MarketFees, "head"));
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
        await using (var ctx = db.CreateContext(b.Id))
        {
            Assert.Empty(await ctx.GovernedServices.ToListAsync());
            Assert.Empty(await ctx.GovernedServiceSettings.ToListAsync());
            // Tenant B may configure the same operation, but cannot attach a setting to tenant A's service identity.
            ctx.Add(GovernedServiceSetting.Create(b.Id, serviceA.Id, new DateOnly(2026, 1, 1),
                GovernedServiceBasis.FixedAmount, 30m, null, true, true, "head"));
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        }
    }

    [SkippableFact]
    public async Task DatabaseRefusesAnImpossibleAmountShape_EvenIfTheDomainFactoryWereBypassed()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = NewTenant("shape");
        GovernedService service;
        await using (var ctx = db.CreateContext(Guid.Empty))
        {
            ctx.Add(tenant);
            await ctx.SaveChangesAsync();
        }
        await using (var ctx = db.CreateContext(tenant.Id))
        {
            service = GovernedService.Create(tenant.Id, CollectorOperationCodes.LandingBerthing, "head");
            ctx.Add(service);
            await ctx.SaveChangesAsync();
        }

        // A fixed-amount version without an amount, and a direct version carrying a fixed amount, both violate the shape.
        await using var raw = db.CreateContext(tenant.Id);
        foreach (var (basis, fixedAmount) in new[] { (1, "NULL"), (2, "30.00"), (1, "0"), (3, "NULL") })
        {
            var sql = $"""
                INSERT INTO "GovernedServiceSettings"
                ("Id","MunicipalityId","GovernedServiceId","EffectiveDate","Basis","FixedAmount","MaximumAmount","IsEnabled","MobileEnabled","CreatedAtUtc","CreatedBy")
                VALUES ('{Guid.NewGuid()}','{tenant.Id}','{service.Id}','2026-01-01',{basis},{fixedAmount},NULL,true,true,now(),'sql')
                """;
            await Assert.ThrowsAnyAsync<Exception>(() => raw.Database.ExecuteSqlRawAsync(sql));
        }
    }

    private sealed record Fixture(Municipality Tenant, CollectorUser Collector, AccountableDocument Document, Guid HeadId);

    private async Task<Fixture> SeedPostableAsync()
    {
        var tenant = NewTenant("post");
        var collector = CollectorUser.Create("Ana Reyes", "C-01", $"pg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        var effective = PhilippineTime.Today.AddDays(-30);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, collector);
            await setup.SaveChangesAsync();
        }
        var headId = Guid.NewGuid();
        AccountableDocument document;
        await using (var ctx = db.CreateContext(tenant.Id))
        {
            var classification = RevenueClassification.Create(RevenueClassificationCodes.MarketFees, tenant.Id);
            ctx.Add(classification);
            ctx.Add(RevenueClassificationPolicy.Create(classification.Id, effective, "Market Fees", RevenueInstrumentType.CashTicket, tenant.Id));
            var service = GovernedService.Create(tenant.Id, CollectorOperationCodes.MarketFees, "head");
            ctx.Add(service);
            ctx.Add(GovernedServiceSetting.Create(tenant.Id, service.Id, effective, GovernedServiceBasis.FixedAmount, 30m, null, true, true, "head"));
            ctx.Add(CollectorOperationAssignment.Assign(tenant.Id, collector.Id, CollectorOperationCodes.MarketFees, "head"));
            var custody = new AccountableFormCustodyWorkflow(ctx, new Caller(headId, tenant.Id, "SuperAdmin"), new FixedTenant(tenant.Id));
            await ctx.SaveChangesAsync();
            var book = (await custody.ReceiveAsync(new ReceiveAccountableFormBookRequest(
                RevenueInstrumentType.CashTicket, "CT book", "CT", 1, 3, 6))).Value!;
            Assert.True((await custody.AssignRangeAsync(new AssignAccountableFormRangeRequest(book.BookId, collector.Id, 1, 3))).IsSuccess);
            document = await ctx.AccountableDocuments.OrderBy(x => x.SerialNumber).FirstAsync();
        }
        return new Fixture(tenant, collector, document, headId);
    }

    private GovernedServiceWorkflow CollectorWorkflow(Fixture f, out EEMOCantilanSDS.Infrastructure.Persistence.AppDbContext ctx)
    {
        ctx = db.CreateContext(f.Tenant.Id);
        return new GovernedServiceWorkflow(ctx, new Caller(f.Collector.Id, f.Tenant.Id, "Collector"), new FixedTenant(f.Tenant.Id));
    }

    private static GovernedServicePostRequest Request(Fixture f, Guid? operationId = null) => new(
        1, operationId ?? Guid.NewGuid(), CollectorOperationCodes.MarketFees, PhilippineTime.Today, 30m, null,
        "Walk-up", "PG test", f.Document.Id, f.Document.DocumentNumber, DateTime.UtcNow.AddMinutes(-1));

    [SkippableFact]
    public async Task GovernedLine_IsAcceptedByTheWidenedSourceConstraint_AndConsumesTheDocumentOnce()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var f = await SeedPostableAsync();

        var workflow = CollectorWorkflow(f, out var ctx);
        await using (ctx)
        {
            var result = await workflow.PostMobileAsync(Request(f));
            Assert.True(result.IsSuccess, result.Error);
        }

        await using var verify = db.CreateContext(f.Tenant.Id);
        var line = await verify.CollectionLines.SingleAsync();
        Assert.Equal(CollectionSourceKind.GovernedService, line.SourceKind);
        Assert.Equal(30m, line.Amount);
        var stored = await verify.AccountableDocuments.SingleAsync(x => x.Id == f.Document.Id);
        Assert.Equal(AccountableDocumentState.Consumed, stored.State);
        Assert.Single(await verify.PostingOperations.ToListAsync());
    }

    [SkippableFact]
    public async Task TwoConcurrentPostsOnTheSameDocument_ProduceExactlyOneCollection()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var f = await SeedPostableAsync();
        var first = CollectorWorkflow(f, out var ctx1);
        var second = CollectorWorkflow(f, out var ctx2);
        await using var _1 = ctx1;
        await using var _2 = ctx2;

        var results = await Task.WhenAll(
            first.PostMobileAsync(Request(f)),
            second.PostMobileAsync(Request(f)));

        Assert.Equal(1, results.Count(x => x.IsSuccess));
        await using var verify = db.CreateContext(f.Tenant.Id);
        Assert.Single(await verify.Collections.ToListAsync());
        Assert.Equal(30m, (await verify.CollectionLines.SingleAsync()).Amount);
        Assert.Equal(AccountableDocumentState.Consumed, (await verify.AccountableDocuments.SingleAsync(x => x.Id == f.Document.Id)).State);
    }

    [SkippableFact]
    public async Task ConcurrentRetryOfTheSameOperation_ResolvesToOneOutcome()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var f = await SeedPostableAsync();
        var request = Request(f);
        var first = CollectorWorkflow(f, out var ctx1);
        var second = CollectorWorkflow(f, out var ctx2);
        await using var _1 = ctx1;
        await using var _2 = ctx2;

        var results = await Task.WhenAll(first.PostMobileAsync(request), second.PostMobileAsync(request));

        Assert.All(results, r => Assert.True(r.IsSuccess, r.Error));
        Assert.Equal(results[0].Value!.CollectionId, results[1].Value!.CollectionId);
        await using var verify = db.CreateContext(f.Tenant.Id);
        Assert.Single(await verify.Collections.ToListAsync());
    }
}
