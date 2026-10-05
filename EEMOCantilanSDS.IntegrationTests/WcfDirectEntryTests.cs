using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// WCF direct Mobile collection (2026-10-01). WCF is enabled once per tenant; afterwards a collector with only the WCF
/// assignment collects any eligible source either against an office-prepared amount (which they cannot change) or by
/// entering the Water amount directly — one source, one Collection, one WCF line, one consumed Cash Ticket, atomically and
/// idempotently. Electricity on the same bill is never touched, and historical (legacy) Water money is never converted.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WcfDirectEntryTests(PostgresFixture db)
{
    private sealed class Actor(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "bobby" : "head";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "wcf-direct";
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

    private sealed record World(Guid TenantId, Guid HeadId, Guid CollectorId, Guid StallId, List<AccountableDocument> Tickets, DateOnly Period);

    private static DateOnly Today => PhilippineTime.Today;

    private async Task<World> SeedAsync(bool assignTickets = true, Func<Guid, UtilityBill>? billFor = null)
    {
        var period = new DateOnly(Today.Year, Today.Month, 1);
        var municipality = Municipality.Create($"WD-{Guid.NewGuid():N}"[..20], "WCF Direct", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"wd-{Guid.NewGuid():N}"[..30]);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }
        var tenantId = municipality.Id;
        var headId = Guid.NewGuid();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM",
            archetype: BillingArchetype.DailyStall, municipalityId: tenantId);
        var stall = Stall.Create(facility.Id, "1", 0m, ApplicableFees.Electricity | ApplicableFees.Water,
            MarketSection.FishSection, createdBy: "test", municipalityId: tenantId);
        var contract = Contract.Create(stall.Id, "Lisa Ilogans", "Lisa Ilogans", period.AddYears(-1), 5, 0m, createdBy: "test");
        var classification = RevenueClassification.Create(RevenueClassificationCodes.Wcf, tenantId);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1),
            "Water Consumption Fee", RevenueInstrumentType.CashTicket, tenantId);
        var book = AccountableFormBook.Receive(tenantId, RevenueInstrumentType.CashTicket, "CT", "CT", 1, 4, 6,
            DateTime.UtcNow.AddMinutes(-5), headId.ToString("N"), "test");
        var tickets = Enumerable.Range(1, 4).Select(i => AccountableDocument.Register(book, i, "test")).ToList();
        var collector = CollectorUser.Create("Bobby Mercado", "C-W", "bobby-" + Guid.NewGuid().ToString("N")[..8],
            null, null, new HashedPassword("h"), tenantId);
        collector.OperationAssignments.Add(CollectorOperationAssignment.Assign(tenantId, collector.Id, CollectorOperationCodes.Wcf, "head"));
        var custody = new List<AccountableFormAssignment>();
        if (assignTickets)
            foreach (var ticket in tickets.Skip(1))
            {
                ticket.AssignTo(collector.Id, "head");
                custody.Add(AccountableFormAssignment.Assign(ticket, collector.Id, headId.ToString("N"), DateTime.UtcNow.AddMinutes(-4), "test"));
            }

        await using (var setup = db.CreateContext(tenantId))
        {
            setup.AddRange(facility, stall, contract, classification, policy, book);
            setup.AddRange(tickets);
            setup.CollectorUsers.Add(collector);
            setup.AccountableFormAssignments.AddRange(custody);
            if (billFor is not null) setup.UtilityBills.Add(billFor(stall.Id));
            await setup.SaveChangesAsync();
        }
        return new World(tenantId, headId, collector.Id, stall.Id, tickets, period);
    }

    private static WcfCollectionWorkflow Wcf(AppDbContext ctx, World w, string role) =>
        new(ctx, new Actor(role == "Collector" ? w.CollectorId : w.HeadId, w.TenantId, role), new FixedTenant(w.TenantId), clock: new Clock());

    private static WcfMobileCollectionWorkflow Office(AppDbContext ctx, World w, string role = "Admin") =>
        new(ctx, new Actor(w.HeadId, w.TenantId, role), new FixedTenant(w.TenantId), new Clock());

    private static async Task<CollectorOperationCapabilityDto> CapabilityAsync(AppDbContext ctx, World w) =>
        (await new GetCollectorOperationCapabilitiesQueryHandler(ctx, new Actor(w.CollectorId, w.TenantId, "Collector"),
                new FixedTenant(w.TenantId), new Clock())
            .Handle(new GetCollectorOperationCapabilitiesQuery(), CancellationToken.None))
        .Value!.Operations.Single(o => o.OperationCode == CollectorOperationCodes.Wcf);

    private static WcfCollectionPostRequest Direct(World w, AccountableDocument ticket, decimal amount, Guid? operationId = null) =>
        new(1, operationId ?? Guid.NewGuid(), Today, Guid.Empty, amount, 0, ticket.Id, ticket.DocumentNumber,
            DateTime.UtcNow.AddMinutes(-1), StallId: w.StallId, BillingYear: w.Period.Year, BillingMonth: w.Period.Month);

    [SkippableFact]
    public async Task EnablingIsOneServerCheckedStep_Idempotent_AndNeverBlockedByCashTicketStock()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var noTickets = await SeedAsync(assignTickets: false);
        await using (var ctx = db.CreateContext(noTickets.TenantId))
        {
            var status = (await Office(ctx, noTickets).GetStatusAsync()).Value!;
            // IA-062: Cash Ticket stock is never a readiness blocker, so a tenant holding no tickets is still ready.
            Assert.True(status.ReadyToEnable);
            Assert.Empty(status.Blockers);
            Assert.Empty(await ctx.CollectorOperationActivations.ToListAsync());
        }

        var w = await SeedAsync();
        await using var c = db.CreateContext(w.TenantId);
        var ready = (await Office(c, w).GetStatusAsync()).Value!;
        Assert.True(ready.ReadyToEnable);
        Assert.Equal(CollectorOperationCapabilityStatus.PendingCutover, (await CapabilityAsync(c, w)).Status);

        var first = await Office(c, w).EnableAsync();
        var second = await Office(c, w).EnableAsync();
        Assert.True(first.Value!.Active);
        Assert.True(second.Value!.Active);
        Assert.Single(await c.CollectorOperationActivations.AsNoTracking().ToListAsync());
        Assert.Single(await c.AuditLogs.AsNoTracking().Where(x => x.Action == "WcfMobileCollectionEnabled").ToListAsync());
        Assert.Equal(CollectorOperationCapabilityStatus.Ready, (await CapabilityAsync(c, w)).Status);

        // A collector cannot enable it.
        Assert.Equal(ResultStatus.Forbidden, (await Office(c, w, "Collector").EnableAsync()).Status);
    }

    [SkippableFact]
    public async Task ADirectMobileAmount_PostsOnceAsWcf_NeedsNoTicket_AndKeepsDirectModeOpen()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        Assert.True((await Office(ctx, w).EnableAsync()).IsSuccess);

        var source = Assert.Single((await Wcf(ctx, w, "Collector").GetMobileSourcesAsync(w.Period.Year, w.Period.Month)).Value!);
        Assert.Equal((WcfSourceState.NoAmount, true, false, "Lisa Ilogans"), (source.State, source.CanEnterDirect, source.CanCollect, source.PayerName));

        var ticket = w.Tickets[1];
        var request = Direct(w, ticket, 40m);
        var posted = await Wcf(ctx, w, "Collector").PostMobileAsync(request);
        Assert.True(posted.IsSuccess, posted.Error);
        var retry = await Wcf(ctx, w, "Collector").PostMobileAsync(request);
        Assert.True(retry.Value!.ExistingOutcome);

        var bill = await ctx.UtilityBills.AsNoTracking().SingleAsync();
        Assert.True(bill.WaterDirectCollection);
        Assert.Equal((0m, UtilityCalculationBasis.DirectApproved, SettlementAuthority.Canonical),
            (bill.WaterCharge, bill.WaterCalculationBasis, bill.WaterSettlementAuthorityState));
        var collection = await ctx.Collections.Include(x => x.Lines).AsNoTracking().SingleAsync();
        Assert.Equal((40m, w.CollectorId), (collection.TotalAmount, collection.CollectorId!.Value));
        Assert.Equal(CollectionSourcePart.Water, Assert.Single(collection.Lines).SourcePart);
        Assert.Equal(AccountableDocumentState.Assigned, (await ctx.AccountableDocuments.AsNoTracking().SingleAsync(x => x.Id == ticket.Id)).State);   // no ticket is consumed (IA-062)
        Assert.StartsWith("SRC-", collection.ReferenceCode);
        var cutover = await ctx.CollectionSettlementCutovers.AsNoTracking().SingleAsync();
        Assert.Contains("CollectorDirectEntry", cutover.ReconciliationEvidence);
        Assert.Equal(0m, cutover.OpeningLegacySettledAmount);

        // Direct receipts never manufacture an assessment or close the source after its first payment.
        var after = Assert.Single((await Wcf(ctx, w, "Collector").GetMobileSourcesAsync(w.Period.Year, w.Period.Month)).Value!);
        Assert.Equal((WcfSourceState.NoAmount, 0m, true, (decimal?)null), (after.State, after.OutstandingAmount, after.CanEnterDirect, after.PreparedAmount));
        Assert.Equal(CollectorOperationCapabilityStatus.Ready, (await CapabilityAsync(ctx, w)).Status);

        // The same money is in the collector's facts (Position, Mobile report) and in the official Monthly Income, once.
        var facts = (await new RemittanceWorkflow(ctx, new Actor(w.CollectorId, w.TenantId, "Collector"), new FixedTenant(w.TenantId))
            .GetMyCollectionsAsync(w.Period, w.Period.AddMonths(1).AddDays(-1))).Value!;
        Assert.Equal(40m, Assert.Single(facts).NetAmount);
        var income = (await new EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome.GetOfficialMonthlyIncomeQueryHandler(
                ctx, new EEMOCantilanSDS.Infrastructure.Repositories.LegacyMonthlyIncomeReader(ctx),
                new Actor(w.HeadId, w.TenantId, "Admin"), new FixedTenant(w.TenantId), new Clock())
            .Handle(new(Today.Year, Today.Month), CancellationToken.None)).Value!;
        Assert.Equal(40m, income.Groups.SelectMany(g => g.Rows).Single(r => r.Key == "WCF").Months[Today.Month - 1].Total);
    }

    [SkippableFact]
    public async Task AnOfficePreparedAmount_IsCollectedAsIs_AndADirectEntryCannotReplaceIt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        Assert.True((await Office(ctx, w).EnableAsync()).IsSuccess);
        Assert.True((await Wcf(ctx, w, "Admin").EstablishObligationAsync(new(w.StallId, w.Period.Year, w.Period.Month, 50m))).IsSuccess);

        // A direct entry against a prepared source is refused: the ₱50 stays and nothing posts.
        var conflicting = await Wcf(ctx, w, "Collector").PostMobileAsync(Direct(w, w.Tickets[1], 40m));
        Assert.False(conflicting.IsSuccess);
        Assert.DoesNotContain("RECONCILIATION_REQUIRED", conflicting.Error);
        Assert.Equal(50m, (await ctx.UtilityBills.AsNoTracking().SingleAsync()).WaterCharge);
        Assert.Empty(await ctx.Collections.AsNoTracking().ToListAsync());
        Assert.Equal(AccountableDocumentState.Assigned,
            (await ctx.AccountableDocuments.AsNoTracking().SingleAsync(x => x.Id == w.Tickets[1].Id)).State);

        // Collected against the prepared amount: it becomes canonical at this first collection.
        var source = Assert.Single((await Wcf(ctx, w, "Collector").GetMobileSourcesAsync(w.Period.Year, w.Period.Month)).Value!);
        Assert.Equal((WcfSourceState.Prepared, true, 50m), (source.State, source.CanCollect, source.OutstandingAmount));
        var ticket = w.Tickets[2];
        var posted = await Wcf(ctx, w, "Collector").PostMobileAsync(new WcfCollectionPostRequest(1, Guid.NewGuid(), Today,
            source.UtilityBillId!.Value, 50m, source.WaterSourceVersion, ticket.Id, ticket.DocumentNumber, DateTime.UtcNow.AddMinutes(-1)));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Contains("OfficePrepared", (await ctx.CollectionSettlementCutovers.AsNoTracking().SingleAsync()).ReconciliationEvidence);
        Assert.Equal(50m, (await ctx.Collections.AsNoTracking().SingleAsync()).TotalAmount);
    }

    [SkippableFact]
    public async Task ADirectEntry_UsesTheBillThatCarriesElectricity_AndNeverChangesElectricity()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var period = new DateOnly(Today.Year, Today.Month, 1);
        var w = await SeedAsync(billFor: stallId =>
        {
            var (p, c, r) = UtilityBill.DirectApprovedReadings(80m);
            var bill = UtilityBill.Create(stallId, period.Year, period.Month, p, c, r, 0m, 0m, 0m, "test");
            bill.SetCalculationBasis(UtilityCalculationBasis.DirectApproved, UtilityCalculationBasis.DirectApproved);
            bill.RecordPayment("OR-E1", null, null, PaymentStatus.Partial, 30m, PaymentStatus.Unpaid, 0m, updatedBy: "test");
            return bill;
        });
        await using var ctx = db.CreateContext(w.TenantId);
        var before = await ctx.UtilityBills.AsNoTracking().SingleAsync();
        Assert.True((await Office(ctx, w).EnableAsync()).IsSuccess);

        var source = Assert.Single((await Wcf(ctx, w, "Collector")
            .GetMobileSourcesAsync(w.Period.Year, w.Period.Month)).Value!);
        var posted = await Wcf(ctx, w, "Collector").PostMobileAsync(
            Direct(w, w.Tickets[1], 40m) with { WaterSourceVersion = source.WaterSourceVersion });

        Assert.True(posted.IsSuccess, posted.Error);
        var after = await ctx.UtilityBills.AsNoTracking().SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal((before.ElecCharge, before.ElecAmountPaid, before.ElecStatus, before.ElecORNumber, before.ElectricitySettlementAuthorityState),
            (after.ElecCharge, after.ElecAmountPaid, after.ElecStatus, after.ElecORNumber, after.ElectricitySettlementAuthorityState));
        Assert.Equal(0m, after.WaterCharge);
        Assert.True(after.WaterDirectCollection);
    }

    [SkippableFact]
    public async Task BeforeWcfIsEnabled_ADirectEntryIsRefused_AndHistoricalLegacyMoneyIsNeverConverted()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var period = new DateOnly(Today.Year, Today.Month, 1);
        var w = await SeedAsync(billFor: stallId =>
        {
            var bill = UtilityBill.Create(stallId, period.Year, period.Month, 0m, 0m, 0m, 0m, 4m, 5m, "test");
            bill.RecordPayment(null, "LEGACY-W-1", null, PaymentStatus.Unpaid, 0m, PaymentStatus.Partial, 7m, updatedBy: "test");
            return bill;
        });
        await using var ctx = db.CreateContext(w.TenantId);

        var notEnabled = await Wcf(ctx, w, "Collector").PostMobileAsync(Direct(w, w.Tickets[1], 40m));
        Assert.False(notEnabled.IsSuccess);
        Assert.Empty(await ctx.Collections.AsNoTracking().ToListAsync());
        // A refused collection is a durable rejection, not an office-review item, so it blocks nothing.
        var blocked = (await Office(ctx, w).GetStatusAsync()).Value!;
        Assert.Empty(blocked.Blockers);

        // Historical legacy money is never converted, even once WCF is enabled (a fresh tenant with the same history).
        var period2 = new DateOnly(Today.Year, Today.Month, 1);
        w = await SeedAsync(billFor: stallId =>
        {
            var bill = UtilityBill.Create(stallId, period2.Year, period2.Month, 0m, 0m, 0m, 0m, 4m, 5m, "test");
            bill.RecordPayment(null, "LEGACY-W-2", null, PaymentStatus.Unpaid, 0m, PaymentStatus.Partial, 7m, updatedBy: "test");
            return bill;
        });
        await using var ctx2 = db.CreateContext(w.TenantId);
        await LegacyStaysLegacyAsync(ctx2, w);
    }

    private async Task LegacyStaysLegacyAsync(AppDbContext ctx, World w)
    {
        Assert.True((await Office(ctx, w).EnableAsync()).IsSuccess);
        var source = Assert.Single((await Wcf(ctx, w, "Collector").GetMobileSourcesAsync(w.Period.Year, w.Period.Month)).Value!);
        Assert.Equal((WcfSourceState.NeedsOffice, false, false), (source.State, source.CanCollect, source.CanEnterDirect));
        var legacy = await Wcf(ctx, w, "Collector").PostMobileAsync(new WcfCollectionPostRequest(1, Guid.NewGuid(), Today,
            source.UtilityBillId!.Value, 5m, source.WaterSourceVersion, w.Tickets[2].Id, w.Tickets[2].DocumentNumber, DateTime.UtcNow.AddMinutes(-1)));
        Assert.False(legacy.IsSuccess);
        Assert.Equal(SettlementAuthority.Legacy, (await ctx.UtilityBills.AsNoTracking().SingleAsync()).WaterSettlementAuthorityState);
        Assert.Empty(await ctx.Collections.AsNoTracking().ToListAsync());
        Assert.Empty(await ctx.CollectionSettlementCutovers.AsNoTracking().ToListAsync());
    }
}
