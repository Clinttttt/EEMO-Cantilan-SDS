using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Queries.Collections.GetCollectionActivity;
using EEMOCantilanSDS.Infrastructure.Repositories;
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
/// PostgreSQL proof of the StallTrack Reference Code (SRC, IA-062): every canonical Collection gets a server-generated,
/// globally unique, immutable <c>SRC-YYYY-NNNNNN</c> from a database sequence, with no physical OR/CT serial involved.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CollectionReferenceCodeTests(PostgresFixture db)
{
    private static readonly System.Text.RegularExpressions.Regex Shape = new(@"^SRC-[0-9]{4}-[0-9]{6,}$");

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
        public string? Username => "pg-src";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "pg-src";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed record World(Municipality Tenant, CollectorUser Ana, CollectorUser Ben);

    private async Task<World> SeedAsync()
    {
        var tenant = Municipality.Create($"src-{Guid.NewGuid():N}"[..12], "SRC test", "Province", MunicipalityStatus.Active,
            tenantCode: $"src-{Guid.NewGuid():N}"[..28].ToLowerInvariant());
        var ana = CollectorUser.Create("Ana Reyes", "C-01", $"pg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        var ben = CollectorUser.Create("Ben Cruz", "C-02", $"pg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, ana, ben);
            await setup.SaveChangesAsync();
        }
        var effective = PhilippineTime.Today.AddDays(-30);
        await using var ctx = db.CreateContext(tenant.Id);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.MarketFees, tenant.Id);
        ctx.Add(classification);
        ctx.Add(RevenueClassificationPolicy.Create(classification.Id, effective, "Market Fees", RevenueInstrumentType.CashTicket, tenant.Id));
        var service = GovernedService.Create(tenant.Id, CollectorOperationCodes.MarketFees, "head");
        ctx.Add(service);
        ctx.Add(GovernedServiceSetting.Create(tenant.Id, service.Id, effective, GovernedServiceBasis.FixedAmount, 30m, null, true, true, "head"));
        ctx.Add(CollectorOperationAssignment.Assign(tenant.Id, ana.Id, CollectorOperationCodes.MarketFees, "head"));
        ctx.Add(CollectorOperationAssignment.Assign(tenant.Id, ben.Id, CollectorOperationCodes.MarketFees, "head"));
        await ctx.SaveChangesAsync();
        return new World(tenant, ana, ben);
    }

    // No document identity at all: the request carries facts only.
    private static GovernedServicePostRequest Request(Guid? operationId = null) => new(
        1, operationId ?? Guid.NewGuid(), CollectorOperationCodes.MarketFees, PhilippineTime.Today, 30m, null,
        "Walk-up", "src test", null, null, null);

    private async Task<(bool Ok, string? Error, string? Src, Guid? CollectionId)> PostAsync(
        World w, CollectorUser collector, GovernedServicePostRequest request)
    {
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var workflow = new GovernedServiceWorkflow(ctx, new Caller(collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id));
        var result = await workflow.PostMobileAsync(request);
        return (result.IsSuccess, result.Error, result.Value?.ReferenceCode, result.Value?.CollectionId);
    }

    [SkippableFact]
    public async Task ACollectionPostsWithNoPhysicalSerial_AndGetsAServerGeneratedSrc()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        var posted = await PostAsync(w, w.Ana, Request());

        Assert.True(posted.Ok, posted.Error);
        Assert.Matches(Shape, posted.Src!);
        await using var verify = db.CreateContext(w.Tenant.Id);
        var collection = await verify.Collections.SingleAsync();
        Assert.Equal(posted.Src, collection.ReferenceCode);
        Assert.Equal(PhilippineTime.Today.Year, collection.ReferenceYear);
        Assert.True(collection.ReferenceNumber > 0);
        Assert.Equal($"SRC-{collection.ReferenceYear}-{collection.ReferenceNumber:D6}", collection.ReferenceCode);
        Assert.Empty(await verify.AccountableDocuments.ToListAsync());   // nothing physical was needed or created
    }

    [SkippableFact]
    public async Task ReplayingTheSameClientOperationReturnsTheSameCollectionAndTheSameSrc()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var request = Request();

        var first = await PostAsync(w, w.Ana, request);
        var replay = await PostAsync(w, w.Ana, request);

        Assert.True(first.Ok, first.Error);
        Assert.True(replay.Ok, replay.Error);
        Assert.Equal(first.CollectionId, replay.CollectionId);
        Assert.Equal(first.Src, replay.Src);
        await using var verify = db.CreateContext(w.Tenant.Id);
        Assert.Single(await verify.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task ConcurrentPostsAcrossCollectorsGetDistinctSrcs()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 24).Select(i =>
            PostAsync(w, i % 2 == 0 ? w.Ana : w.Ben, Request())));

        Assert.All(results, r => Assert.True(r.Ok, r.Error));
        Assert.Equal(24, results.Select(r => r.Src).Distinct().Count());
        await using var verify = db.CreateContext(w.Tenant.Id);
        var numbers = await verify.Collections.Select(x => x.ReferenceNumber).ToListAsync();
        Assert.Equal(24, numbers.Distinct().Count());
    }

    [SkippableFact]
    public async Task TheNumberRunsGloballyAndNeverRestartsByCollector()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        var a1 = await PostAsync(w, w.Ana, Request());
        var a2 = await PostAsync(w, w.Ana, Request());
        var b1 = await PostAsync(w, w.Ben, Request());
        var a3 = await PostAsync(w, w.Ana, Request());

        await using var verify = db.CreateContext(w.Tenant.Id);
        var byId = await verify.Collections.ToDictionaryAsync(x => x.Id, x => x.ReferenceNumber);
        var order = new[] { a1, a2, b1, a3 }.Select(x => byId[x.CollectionId!.Value]).ToArray();
        // Strictly increasing in posting order (gaps would be acceptable; a restart or a per-collector counter would not be).
        Assert.True(order.Zip(order.Skip(1), (x, y) => y > x).All(ok => ok), string.Join(",", order));
    }

    [SkippableFact]
    public async Task TheDatabaseRefusesADuplicateReferenceNumberOrCode_AndItsCodeIsComputedNotTyped()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        Assert.True((await PostAsync(w, w.Ana, Request())).Ok);
        Assert.True((await PostAsync(w, w.Ana, Request())).Ok);

        await using var raw = db.CreateContext(w.Tenant.Id);
        var ids = await raw.Collections.OrderBy(x => x.ReferenceNumber).Select(x => x.Id).ToListAsync();
        var firstNumber = await raw.Collections.Where(x => x.Id == ids[0]).Select(x => x.ReferenceNumber).SingleAsync();

        // Pointing the second Collection at the first one's number violates the global unique index.
        await Assert.ThrowsAnyAsync<Exception>(() => raw.Database.ExecuteSqlRawAsync(
            $"UPDATE \"Collections\" SET \"ReferenceNumber\" = {firstNumber} WHERE \"Id\" = '{ids[1]}'"));
        // The code is a stored computed column: it cannot be assigned directly.
        await Assert.ThrowsAnyAsync<Exception>(() => raw.Database.ExecuteSqlRawAsync(
            $"UPDATE \"Collections\" SET \"ReferenceCode\" = 'SRC-1999-000001' WHERE \"Id\" = '{ids[1]}'"));
    }

    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    [SkippableFact]
    public async Task ASrcLookupFindsTheCollectionOnAnotherDay_CaseInsensitively_AndNeverInAnotherTenant()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var posted = await PostAsync(w, w.Ana, Request());
        Assert.True(posted.Ok, posted.Error);

        async Task<EEMOCantilanSDS.Application.Dtos.Collections.CollectionActivityFeedDto?> Lookup(Guid tenant, string reference, DateOnly day)
        {
            await using var read = db.CreateContext(tenant);
            var handler = new GetCollectionActivityQueryHandler(new CollectionActivityReader(read),
                new Caller(Guid.NewGuid(), tenant, "Admin"), new FixedTenant(tenant), new Clock());
            return (await handler.Handle(new GetCollectionActivityQuery(day, day, Reference: reference), CancellationToken.None)).Value;
        }

        var unrelatedDay = PhilippineTime.Today.AddDays(-20);
        var found = await Lookup(w.Tenant.Id, posted.Src!.ToLowerInvariant(), unrelatedDay);
        var only = Assert.Single(found!.Events);
        Assert.Equal(posted.Src, only.ReferenceCode);
        Assert.Equal(PhilippineTime.Today, found.To);          // the page jumps to the Collection's own business date

        // A normal day query without a reference is unchanged: the unrelated day lists nothing.
        await using (var read = db.CreateContext(w.Tenant.Id))
        {
            var plain = await new GetCollectionActivityQueryHandler(new CollectionActivityReader(read),
                new Caller(Guid.NewGuid(), w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id), new Clock())
                .Handle(new GetCollectionActivityQuery(unrelatedDay, unrelatedDay), CancellationToken.None);
            Assert.Empty(plain.Value!.Events);
        }

        var other = await SeedAsync();
        Assert.Empty((await Lookup(other.Tenant.Id, posted.Src, unrelatedDay))!.Events);   // another tenant never sees it
    }
}
