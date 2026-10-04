using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Vegetable / Fruit space rental on PostgreSQL (IA-046): the collector chooses the BUSINESS MODE, never the instrument. A monthly
/// rental resolves the Official Receipt policy and a daily transaction the Cash Ticket policy; each is one Collection with an SRC,
/// no typed serial, counted under the one Vegetable / Fruit income row. A disabled service writes nothing.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class VegetableFruitModesTests(PostgresFixture db)
{
    private static readonly DateOnly Today = PhilippineTime.Today;
    private static readonly DateOnly Effective = new(2000, 1, 1);

    private sealed record World(Guid TenantId, Guid CollectorId);

    private sealed class Caller(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "mobile-collector" : "head";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "veg-modes";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private GovernedServiceWorkflow As(AppDbContext ctx, Guid tenantId, Guid userId, string role) =>
        new(ctx, new Caller(userId, tenantId, role), new FixedTenant(tenantId), new Clock());

    private async Task<World> SeedAsync(bool enabled = true)
    {
        var tenant = Municipality.Create($"vg-{Guid.NewGuid():N}"[..12], "Vegetable", "Province", MunicipalityStatus.Active,
            tenantCode: $"veg-{Guid.NewGuid():N}"[..28].ToLowerInvariant());
        var collector = CollectorUser.Create("Mobile Collector", "MC-01", $"vg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, collector);
            await setup.SaveChangesAsync();
        }
        await using var ctx = db.CreateContext(tenant.Id);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.VegetableFruitSpaceRental, tenant.Id);
        ctx.Add(classification);
        ctx.Add(RevenueClassificationPolicy.Create(classification.Id, Effective, "Vegetable / Fruit monthly rental",
            RevenueInstrumentType.OfficialReceipt, tenant.Id, businessContext: RevenuePolicyContext.VegetableWholePayment));
        ctx.Add(RevenueClassificationPolicy.Create(classification.Id, Effective, "Vegetable / Fruit daily transaction",
            RevenueInstrumentType.CashTicket, tenant.Id, businessContext: RevenuePolicyContext.VegetableDailyTransaction));
        var service = GovernedService.Create(tenant.Id, CollectorOperationCodes.VegetableFruitSpaceRental, "head");
        ctx.Add(service);
        ctx.Add(GovernedServiceSetting.Create(tenant.Id, service.Id, Effective, GovernedServiceBasis.DirectApprovedAmount,
            null, 1000m, enabled, enabled, "head"));
        ctx.Add(CollectorOperationAssignment.Assign(tenant.Id, collector.Id, CollectorOperationCodes.VegetableFruitSpaceRental, "head"));
        await ctx.SaveChangesAsync();
        return new World(tenant.Id, collector.Id);
    }

    private static GovernedServicePostRequest Post(GovernedServiceMode mode, decimal amount, Guid? op = null) =>
        new(1, op ?? Guid.NewGuid(), CollectorOperationCodes.VegetableFruitSpaceRental, Today, amount, mode, "Pedro Vendor", null);

    [SkippableFact]
    public async Task Monthly_IsAnOfficialReceiptCollection_AndDaily_IsACashTicketCollection_EachWithAnSrcAndNoSerial()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var workflow = As(ctx, w.TenantId, w.CollectorId, "Collector");
            var monthly = await workflow.PostMobileAsync(Post(GovernedServiceMode.WholePayment, 900m));
            Assert.True(monthly.IsSuccess, monthly.Error);
            Assert.Equal(RevenueInstrumentType.OfficialReceipt, monthly.Value!.Instrument);
            Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", monthly.Value.ReferenceCode);

            var daily = await workflow.PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 50m));
            Assert.True(daily.IsSuccess, daily.Error);
            Assert.Equal(RevenueInstrumentType.CashTicket, daily.Value!.Instrument);
            Assert.NotEqual(monthly.Value.ReferenceCode, daily.Value.ReferenceCode);
        }
        await using var verify = db.CreateContext(w.TenantId);
        var collections = await verify.Collections.Include(x => x.Lines).ToListAsync();
        Assert.Equal(2, collections.Count);                                           // one Collection per transaction
        Assert.Equal(950m, collections.Sum(x => x.TotalAmount));
        Assert.All(collections, c => Assert.Equal(w.CollectorId, c.CollectorId));
        Assert.Empty(await verify.AccountableDocuments.ToListAsync());                 // no physical form, no typed serial
        var rows = await As(verify, w.TenantId, w.CollectorId, "Collector").GetCollectorRecordsAsync(Today, Today);
        Assert.Equal(2, rows.Value!.Count);                                           // each is readable under its own SRC
    }

    [SkippableFact]
    public async Task Replay_ReturnsTheSameCollectionAndSrc_AndADifferentIntentOnTheSameIdConflicts()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var op = Guid.NewGuid();
        string src;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var first = await As(ctx, w.TenantId, w.CollectorId, "Collector").PostMobileAsync(Post(GovernedServiceMode.WholePayment, 900m, op));
            Assert.True(first.IsSuccess, first.Error);
            src = first.Value!.ReferenceCode;
        }
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var workflow = As(ctx, w.TenantId, w.CollectorId, "Collector");
            var replay = await workflow.PostMobileAsync(Post(GovernedServiceMode.WholePayment, 900m, op));
            Assert.True(replay.IsSuccess, replay.Error);
            Assert.Equal((src, true), (replay.Value!.ReferenceCode, replay.Value.ExistingOutcome));
            var conflict = await workflow.PostMobileAsync(Post(GovernedServiceMode.DailyTransaction, 50m, op));
            Assert.False(conflict.IsSuccess);
            Assert.Equal(1, await ctx.Collections.CountAsync());
        }
    }

    [SkippableFact]
    public async Task ADisabledService_AnOverCeilingAmount_AnUnassignedCollector_AndAnotherTenant_WriteNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var off = await SeedAsync(enabled: false);
        await using (var ctx = db.CreateContext(off.TenantId))
        {
            Assert.False((await As(ctx, off.TenantId, off.CollectorId, "Collector").PostMobileAsync(Post(GovernedServiceMode.WholePayment, 900m))).IsSuccess);
            Assert.Empty(await ctx.Collections.ToListAsync());
        }

        await db.ResetAsync();
        var a = await SeedAsync();
        var b = await SeedAsync();
        await using (var ctx = db.CreateContext(a.TenantId))
        {
            var workflow = As(ctx, a.TenantId, a.CollectorId, "Collector");
            Assert.False((await workflow.PostMobileAsync(Post(GovernedServiceMode.WholePayment, 1000.01m))).IsSuccess);          // ceiling
            Assert.False((await As(ctx, a.TenantId, Guid.NewGuid(), "Collector")
                .PostMobileAsync(Post(GovernedServiceMode.WholePayment, 900m))).IsSuccess);                                   // unassigned
            Assert.Empty(await ctx.Collections.ToListAsync());
        }
        await using (var ctx = db.CreateContext(b.TenantId))   // tenant A's collector acting inside tenant B
        {
            Assert.False((await As(ctx, b.TenantId, a.CollectorId, "Collector")
                .PostMobileAsync(Post(GovernedServiceMode.WholePayment, 900m))).IsSuccess);
            Assert.Empty(await ctx.Collections.ToListAsync());
        }
    }

    [SkippableFact]
    public async Task TurningTheServiceOn_AppendsAVersion_AndNeverRewritesThePolicyOrAnEarlierCollection()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using (var ctx = db.CreateContext(w.TenantId))
            Assert.True((await As(ctx, w.TenantId, w.CollectorId, "Collector").PostMobileAsync(Post(GovernedServiceMode.WholePayment, 900m))).IsSuccess);

        int policies;
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            policies = await ctx.RevenueClassificationPolicies.CountAsync();
            var head = As(ctx, w.TenantId, Guid.NewGuid(), "SuperAdmin");
            // Enabling reuses the existing policy versions (no duplicate classification/date row is ever needed for it).
            var saved = await head.ConfigureAsync(CollectorOperationCodes.VegetableFruitSpaceRental,
                new ConfigureGovernedServiceRequest(Today, GovernedServiceBasis.DirectApprovedAmount, null, 1200m, true, true));
            Assert.True(saved.IsSuccess, saved.Error);
        }
        await using var verify = db.CreateContext(w.TenantId);
        Assert.Equal(policies, await verify.RevenueClassificationPolicies.CountAsync());           // no policy rows added or changed
        Assert.Equal(2, await verify.GovernedServiceSettings.CountAsync());                        // history kept; the new version appended
        Assert.Equal(900m, (await verify.Collections.SingleAsync()).TotalAmount);                  // the earlier Collection is untouched
    }
}
