using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// PostgreSQL proof for configured Market Fee definitions (approved fee options under the one MARKET_FEES classification):
/// the office defines several fee types, each with an effective-dated Fixed or Direct amount rule; a collector selects one
/// and the server prices it, on the assigned Cash Ticket, once. Monthly Income keeps one Market Fees row and counts each
/// line once; the drill-down by fee type adds up to that row.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MarketFeeDefinitionTests(PostgresFixture db)
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
        public string? Username => role == "Collector" ? "collector" : "head";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "pg-fees";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private static readonly DateOnly Today = PhilippineTime.Today;

    private sealed record World(Municipality Tenant, CollectorUser Collector, Guid HeadId, AccountableDocument[] Tickets,
        Guid ComfortRoom, Guid Parking, Guid Retired, Guid Storage);

    private GovernedServiceWorkflow Head(World w, AppDbContext ctx) =>
        new(ctx, new Caller(w.HeadId, w.Tenant.Id, "SuperAdmin"), new FixedTenant(w.Tenant.Id));

    private GovernedServiceWorkflow Collector(World w, AppDbContext ctx) =>
        new(ctx, new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id));

    /// <summary>
    /// A tenant whose Market Fees are collected by fee type (CT policy), with: Comfort Room fixed ₱5, Parking direct up to
    /// ₱100, a retired option, and an option whose only rule starts tomorrow. Five CTs are assigned to the collector.
    /// </summary>
    private async Task<World> SeedAsync()
    {
        var tenant = Municipality.Create($"fee-{Guid.NewGuid():N}"[..12], "Fee Test", "Province", MunicipalityStatus.Active,
            tenantCode: $"fees-{Guid.NewGuid():N}"[..28].ToLowerInvariant());
        var collector = CollectorUser.Create("Ana Reyes", "C-01", $"pg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, collector);
            await setup.SaveChangesAsync();
        }
        var head = Guid.NewGuid();
        var effective = Today.AddDays(-30);
        AccountableDocument[] tickets;
        await using (var ctx = db.CreateContext(tenant.Id))
        {
            var classification = RevenueClassification.Create(RevenueClassificationCodes.MarketFees, tenant.Id);
            ctx.Add(classification);
            ctx.Add(RevenueClassificationPolicy.Create(classification.Id, effective, "Market Fees", RevenueInstrumentType.CashTicket, tenant.Id));
            ctx.Add(CollectorOperationAssignment.Assign(tenant.Id, collector.Id, CollectorOperationCodes.MarketFees, "head"));
            await ctx.SaveChangesAsync();
            var custody = new AccountableFormCustodyWorkflow(ctx, new Caller(head, tenant.Id, "SuperAdmin"), new FixedTenant(tenant.Id));
            var book = (await custody.ReceiveAsync(new ReceiveAccountableFormBookRequest(RevenueInstrumentType.CashTicket, "CT", "CT-", 1, 5, 4))).Value!;
            Assert.True((await custody.AssignRangeAsync(new AssignAccountableFormRangeRequest(book.BookId, collector.Id, 1, 5))).IsSuccess);
            tickets = await ctx.AccountableDocuments.OrderBy(x => x.SerialNumber).ToArrayAsync();
        }

        var world = new World(tenant, collector, head, tickets, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);
        await using (var ctx = db.CreateContext(tenant.Id))
        {
            var head1 = Head(world, ctx);
            Assert.True((await head1.ConfigureAsync(CollectorOperationCodes.MarketFees, new ConfigureGovernedServiceRequest(
                effective, GovernedServiceBasis.ApprovedFeeOption, null, null, true, true))).IsSuccess);
            async Task<Guid> AddAsync(string name, string? location, DateOnly from, GovernedServiceBasis basis, decimal? amount, decimal? ceiling)
            {
                var added = await head1.AddFeeOptionAsync(CollectorOperationCodes.MarketFees,
                    new AddFeeOptionRequest(name, null, location, null, from, basis, amount, ceiling));
                Assert.True(added.IsSuccess, added.Error);
                return added.Value!.Single(x => x.DisplayName == name).Id;
            }
            var comfort = await AddAsync("Comfort Room", "Transport Terminal", effective, GovernedServiceBasis.FixedAmount, 5m, null);
            var parking = await AddAsync("Overnight Parking", null, effective, GovernedServiceBasis.DirectApprovedAmount, null, 100m);
            var retired = await AddAsync("Old Stall Sweeping", null, effective, GovernedServiceBasis.FixedAmount, 10m, null);
            var storage = await AddAsync("Cold Storage", null, Today.AddDays(1), GovernedServiceBasis.FixedAmount, 20m, null);
            Assert.True((await head1.RetireFeeOptionAsync(CollectorOperationCodes.MarketFees, retired, new RetireFeeOptionRequest(Today))).IsSuccess);
            world = world with { ComfortRoom = comfort, Parking = parking, Retired = retired, Storage = storage };
        }
        return world;
    }

    private static GovernedServicePostRequest Post(World w, int ticket, decimal amount, Guid? option, string? payer = null) => new(
        1, Guid.NewGuid(), CollectorOperationCodes.MarketFees, Today, amount, null, payer, null,
        w.Tickets[ticket].Id, w.Tickets[ticket].DocumentNumber, DateTime.UtcNow.AddMinutes(-1), FeeOptionId: option);

    [SkippableFact]
    public async Task SeveralDefinitionsSitUnderMarketFees_AndTheCollectorIsOfferedOnlyThoseInForceToday()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        await using var ctx = db.CreateContext(w.Tenant.Id);
        var definitions = (await Head(w, ctx).GetFeeOptionsAsync(CollectorOperationCodes.MarketFees)).Value!;
        Assert.Equal(4, definitions.Count);
        Assert.Equal("Active", definitions.Single(x => x.Id == w.ComfortRoom).Status);
        Assert.Equal("Scheduled", definitions.Single(x => x.Id == w.Storage).Status);
        Assert.Equal("Retired", definitions.Single(x => x.Id == w.Retired).Status);

        var service = Assert.Single(await ctx.GovernedServices.ToListAsync());
        Assert.Equal(CollectorOperationCodes.MarketFees, service.OperationCode);
        Assert.All(await ctx.GovernedServiceFeeOptions.ToListAsync(), o => Assert.Equal(service.Id, o.GovernedServiceId));

        var terms = (await Collector(w, ctx).GetTermsAsync(CollectorOperationCodes.MarketFees, null)).Value!;
        Assert.Equal(GovernedServiceBasis.ApprovedFeeOption, terms.Basis);
        Assert.Equal(RevenueInstrumentType.CashTicket, terms.Instrument);
        var offered = terms.FeeOptions!;
        Assert.Equal(new[] { w.ComfortRoom, w.Parking }.Order(), offered.Select(x => x.Id).Order());
        var comfort = offered.Single(x => x.Id == w.ComfortRoom);
        Assert.Equal((GovernedServiceBasis.FixedAmount, (decimal?)5m), (comfort.Basis, comfort.Amount));
        Assert.Equal("Transport Terminal", comfort.Location);
        Assert.Equal((GovernedServiceBasis.DirectApprovedAmount, (decimal?)100m), (offered.Single(x => x.Id == w.Parking).Basis, offered.Single(x => x.Id == w.Parking).MaximumAmount));

        // An office definition does not activate itself as a new Monthly Income row or classification.
        Assert.Single(await ctx.RevenueClassifications.ToListAsync());
    }

    [SkippableFact]
    public async Task AFixedFeeIsPricedByTheServer_OnTheAssignedCashTicket_AndCannotBeOverridden()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            var ok = await Collector(w, ctx).PostMobileAsync(Post(w, 0, 5m, w.ComfortRoom));
            Assert.True(ok.IsSuccess, ok.Error);
            Assert.Equal(RevenueInstrumentType.CashTicket, ok.Value!.Instrument);
            Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", ok.Value.ReferenceCode);
        }
        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            // A typed amount on a fixed fee is never accepted; the collection is not recorded.
            var overridden = await Collector(w, ctx).PostMobileAsync(Post(w, 1, 7m, w.ComfortRoom));
            Assert.False(overridden.IsSuccess);
            Assert.DoesNotContain("RECONCILIATION_REQUIRED", overridden.Error);
        }

        await using var verify = db.CreateContext(w.Tenant.Id);
        var line = await verify.CollectionLines.SingleAsync();
        Assert.Equal(5m, line.Amount);
        Assert.Contains("Comfort Room", line.CalculationSnapshot);
        Assert.Equal(AccountableDocumentState.Assigned, (await verify.AccountableDocuments.SingleAsync(x => x.Id == w.Tickets[0].Id)).State);   // no physical form is consumed (IA-062)
        Assert.Equal(AccountableDocumentState.Assigned, (await verify.AccountableDocuments.SingleAsync(x => x.Id == w.Tickets[1].Id)).State);
        Assert.Single(await verify.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task ADirectAmountIsTakenOnlyWhereConfigured_WithinItsCeiling()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        await using (var ctx = db.CreateContext(w.Tenant.Id))
            Assert.True((await Collector(w, ctx).PostMobileAsync(Post(w, 0, 37.50m, w.Parking))).IsSuccess);
        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            var above = await Collector(w, ctx).PostMobileAsync(Post(w, 1, 150m, w.Parking));
            Assert.False(above.IsSuccess);
            Assert.Contains("ceiling", above.Error, StringComparison.OrdinalIgnoreCase);
        }

        await using var verify = db.CreateContext(w.Tenant.Id);
        Assert.Equal(37.50m, (await verify.CollectionLines.SingleAsync()).Amount);
    }

    [SkippableFact]
    public async Task ARetiredUnscheduledUnknownOrMissingFeeType_IsRefused()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        var codes = new List<string?>();
        var cases = new (int Ticket, Guid? Option, decimal Amount)[]
        {
            (0, w.Retired, 10m), (1, w.Storage, 20m), (2, Guid.NewGuid(), 5m), (3, null, 5m)
        };
        foreach (var (ticket, option, amount) in cases)
        {
            await using var ctx = db.CreateContext(w.Tenant.Id);
            var refused = await Collector(w, ctx).PostMobileAsync(Post(w, ticket, amount, option));
            Assert.False(refused.IsSuccess);
        }

        await using var verify = db.CreateContext(w.Tenant.Id);
        Assert.Empty(await verify.Collections.ToListAsync());
        var outcomes = await verify.PostingOperations.ToListAsync();
        Assert.Equal(4, outcomes.Count);
        Assert.All(outcomes, o => Assert.Equal(PostingOperationStatus.Rejected, o.Status));
    }

    [SkippableFact]
    public async Task ARescheduledAmountAppliesFromItsDate_AndHistoryAndPostedAmountsAreKept()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();

        await using (var ctx = db.CreateContext(w.Tenant.Id))
            Assert.True((await Collector(w, ctx).PostMobileAsync(Post(w, 0, 5m, w.ComfortRoom))).IsSuccess);
        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            var head = Head(w, ctx);
            var past = await head.ScheduleFeeOptionRateAsync(CollectorOperationCodes.MarketFees, w.ComfortRoom,
                new ScheduleFeeOptionRateRequest(Today.AddDays(-1), GovernedServiceBasis.FixedAmount, 8m, null));
            Assert.Equal(ResultStatus.Invalid, past.Status);   // never retroactive
            var scheduled = await head.ScheduleFeeOptionRateAsync(CollectorOperationCodes.MarketFees, w.ComfortRoom,
                new ScheduleFeeOptionRateRequest(Today.AddDays(1), GovernedServiceBasis.FixedAmount, 8m, null));
            Assert.True(scheduled.IsSuccess, scheduled.Error);
            var comfort = scheduled.Value!.Single(x => x.Id == w.ComfortRoom);
            Assert.Equal(5m, comfort.Amount);                  // today's rule is unchanged
            Assert.Equal(new decimal?[] { 8m, 5m }, comfort.History.Select(x => x.FixedAmount));
        }
        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            Assert.True((await Head(w, ctx).RetireFeeOptionAsync(CollectorOperationCodes.MarketFees, w.Parking,
                new RetireFeeOptionRequest(Today))).IsSuccess);
            var history = (await Head(w, ctx).GetFeeOptionsAsync(CollectorOperationCodes.MarketFees)).Value!;
            Assert.Equal("Retired", history.Single(x => x.Id == w.Parking).Status);
            Assert.Single(history.Single(x => x.Id == w.Parking).History);
        }

        await using var verify = db.CreateContext(w.Tenant.Id);
        Assert.Equal(5m, (await verify.CollectionLines.SingleAsync()).Amount);
        Assert.Equal(4, await verify.GovernedServiceFeeOptions.CountAsync());   // nothing is deleted
    }

    [SkippableFact]
    public async Task AWalkUpSaleNeedsNoPayor_AndAReplayIsTheSameSale_WhileANewOperationIsANewSale()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var sale = Post(w, 0, 5m, w.ComfortRoom, payer: "Juan (walk-up)");

        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            var walkUp = await Collector(w, ctx).PostMobileAsync(sale);
            Assert.True(walkUp.IsSuccess, walkUp.Error);
        }
        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            // The same operation replayed is the same outcome and the same SRC.
            var replay = await Collector(w, ctx).PostMobileAsync(sale);
            Assert.True(replay.IsSuccess, replay.Error);
            Assert.True(replay.Value!.ExistingOutcome);
        }
        await using (var ctx = db.CreateContext(w.Tenant.Id))
            Assert.True((await Collector(w, ctx).PostMobileAsync(Post(w, 0, 5m, w.ComfortRoom))).IsSuccess);   // a different operation is a different sale
        await using (var ctx = db.CreateContext(w.Tenant.Id))
            Assert.True((await Collector(w, ctx).PostMobileAsync(Post(w, 1, 5m, w.ComfortRoom, payer: null))).IsSuccess);

        await using var verify = db.CreateContext(w.Tenant.Id);
        Assert.Empty(await verify.Payors.ToListAsync());     // typed payer text never creates a Payor
        var collections = await verify.Collections.ToListAsync();
        Assert.Equal(3, collections.Count);
        Assert.Equal(3, collections.Select(c => c.ReferenceCode).Distinct().Count());
        Assert.All(collections, c => Assert.Null(c.PayorId));
        Assert.Contains(collections, c => c.PayerName == "Juan (walk-up)");
        Assert.Contains(collections, c => c.PayerName is null);
        Assert.Empty(await verify.AccountableDocuments.Where(x => x.State == AccountableDocumentState.Consumed).ToListAsync());   // no physical form is consumed (IA-062)
    }

    [SkippableFact]
    public async Task MonthlyIncomeKeepsOneMarketFeesRow_CountedOnce_AndTheDrillDownAddsUpToIt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        foreach (var (ticket, amount, option) in new[] { (0, 5m, w.ComfortRoom), (1, 5m, w.ComfortRoom), (2, 40m, w.Parking) })
        {
            await using var ctx = db.CreateContext(w.Tenant.Id);
            Assert.True((await Collector(w, ctx).PostMobileAsync(Post(w, ticket, amount, option))).IsSuccess);
        }

        await using var read = db.CreateContext(w.Tenant.Id);
        var statement = (await new GetOfficialMonthlyIncomeQueryHandler(read, new LegacyMonthlyIncomeReader(read),
                new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id), new Clock())
            .Handle(new GetOfficialMonthlyIncomeQuery(Today.Year, null), CancellationToken.None)).Value!;
        var rows = statement.Groups.SelectMany(g => g.Rows).Where(r => r.Total.Total != 0m).ToList();
        var market = Assert.Single(rows);
        Assert.Equal("MARKET_FEES", market.Key);
        Assert.Equal(50m, market.Total.Canonical);
        Assert.Equal(50m, statement.GrandTotal.Total);

        var period = new DateOnly(Today.Year, Today.Month, 1);
        var drill = (await Head(w, read).GetFeeOptionTotalsAsync(CollectorOperationCodes.MarketFees, period, period.AddMonths(1).AddDays(-1))).Value!;
        Assert.Equal(2, drill.Count);
        Assert.Equal((2, 10m), (drill.Single(x => x.FeeOptionId == w.ComfortRoom).Collections, drill.Single(x => x.FeeOptionId == w.ComfortRoom).Amount));
        Assert.Equal((1, 40m), (drill.Single(x => x.FeeOptionId == w.Parking).Collections, drill.Single(x => x.FeeOptionId == w.Parking).Amount));
        Assert.Equal(market.Total.Total, drill.Sum(x => x.Amount));
        Assert.Contains(drill, x => x.Name == "Comfort Room — Transport Terminal");

        var activity = (await Head(w, read).GetActivityAsync(CollectorOperationCodes.MarketFees, period, period.AddMonths(1).AddDays(-1))).Value!;
        Assert.Equal(3, activity.Count);
        Assert.Equal(2, activity.Count(x => x.FeeOptionName == "Comfort Room — Transport Terminal"));

        // Collectors never read office configuration or the drill-down.
        Assert.Equal(ResultStatus.Forbidden, (await Collector(w, read).GetFeeOptionsAsync(CollectorOperationCodes.MarketFees)).Status);
        Assert.Equal(ResultStatus.Forbidden, (await Collector(w, read).AddFeeOptionAsync(CollectorOperationCodes.MarketFees,
            new AddFeeOptionRequest("X", null, null, null, Today, GovernedServiceBasis.FixedAmount, 1m, null))).Status);
    }

    [SkippableFact]
    public async Task TheDatabaseRefusesAnImpossibleRuleShape_AndAnOptionOrRuleCrossingTenants()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var other = Municipality.Create($"oth-{Guid.NewGuid():N}"[..12], "Other", "Province", MunicipalityStatus.Active,
            tenantCode: $"other-{Guid.NewGuid():N}"[..28].ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Add(other);
            await setup.SaveChangesAsync();
        }
        Guid serviceId;
        await using (var ctx = db.CreateContext(w.Tenant.Id))
            serviceId = (await ctx.GovernedServices.SingleAsync()).Id;

        await using var raw = db.CreateContext(w.Tenant.Id);
        // A rule may only be Fixed (with a positive amount, no ceiling) or Direct (no fixed amount).
        foreach (var (basis, fixedAmount, ceiling) in new[] { (1, "NULL", "NULL"), (2, "5.00", "NULL"), (1, "5.00", "9.00"), (3, "NULL", "NULL"), (4, "NULL", "NULL") })
        {
            var sql = $"""
                INSERT INTO "GovernedServiceFeeOptionRates"
                ("Id","MunicipalityId","FeeOptionId","EffectiveDate","Basis","FixedAmount","MaximumAmount","CreatedAtUtc","CreatedBy")
                VALUES ('{Guid.NewGuid()}','{w.Tenant.Id}','{w.ComfortRoom}','2026-01-01',{basis},{fixedAmount},{ceiling},now(),'sql')
                """;
            await Assert.ThrowsAnyAsync<Exception>(() => raw.Database.ExecuteSqlRawAsync(sql));
        }
        // Another tenant cannot hang an option on this tenant's service, nor a rule on this tenant's option.
        await Assert.ThrowsAnyAsync<Exception>(() => raw.Database.ExecuteSqlRawAsync($"""
            INSERT INTO "GovernedServiceFeeOptions" ("Id","MunicipalityId","GovernedServiceId","DisplayName","CreatedAtUtc","CreatedBy")
            VALUES ('{Guid.NewGuid()}','{other.Id}','{serviceId}','Stolen',now(),'sql')
            """));
        await Assert.ThrowsAnyAsync<Exception>(() => raw.Database.ExecuteSqlRawAsync($"""
            INSERT INTO "GovernedServiceFeeOptionRates" ("Id","MunicipalityId","FeeOptionId","EffectiveDate","Basis","FixedAmount","CreatedAtUtc","CreatedBy")
            VALUES ('{Guid.NewGuid()}','{other.Id}','{w.ComfortRoom}','2026-01-01',1,5.00,now(),'sql')
            """));

        // And the other tenant reads none of this tenant's definitions.
        await using var otherCtx = db.CreateContext(other.Id);
        Assert.Empty(await otherCtx.GovernedServiceFeeOptions.ToListAsync());
        Assert.Empty(await otherCtx.GovernedServiceFeeOptionRates.ToListAsync());
    }
}
