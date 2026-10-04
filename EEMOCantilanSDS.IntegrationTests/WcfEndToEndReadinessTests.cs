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
/// WCF end to end for an operation-only collector (IA-053): the Head sets a direct approved Water amount on the one
/// stall/month UtilityBill, activates that single obligation for Mobile through the attested scoped cutover, and a collector
/// holding only the WCF assignment — no NPM facility — sees it Ready, collects it once on an assigned Cash Ticket, and the
/// obligation closes. Capability and writer enforce the same boundary; Electricity on the same bill is never touched.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WcfEndToEndReadinessTests(PostgresFixture db)
{
    private sealed class Actor(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "bobby" : "head";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "wcf-e2e";
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

    private sealed record World(Guid TenantId, Guid HeadId, Guid CollectorId, Guid StallId, Guid CtId, string CtNumber, DateOnly Period);

    private async Task<World> SeedAsync(UtilityBill? existingBill = null, Func<Guid, UtilityBill>? billFor = null)
    {
        var period = new DateOnly(PhilippineTime.Today.Year, PhilippineTime.Today.Month, 1);
        var municipality = Municipality.Create($"WE-{Guid.NewGuid():N}"[..20], "WCF E2E", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"we-{Guid.NewGuid():N}"[..30]);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }
        var tenantId = municipality.Id;
        var headId = Guid.NewGuid();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM",
            archetype: BillingArchetype.DailyStall, municipalityId: tenantId);
        var stall = Stall.Create(facility.Id, "12", 0m, ApplicableFees.Electricity | ApplicableFees.Water,
            MarketSection.FishSection, createdBy: "test", municipalityId: tenantId);
        var contract = Contract.Create(stall.Id, "Bobby Example", "Bobby Example", period.AddYears(-1), 5, 0m, createdBy: "test");
        var classification = RevenueClassification.Create(RevenueClassificationCodes.Wcf, tenantId);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1),
            "Water Consumption Fee", RevenueInstrumentType.CashTicket, tenantId);
        var book = AccountableFormBook.Receive(tenantId, RevenueInstrumentType.CashTicket, "CT", "CT", 1, 3, 6,
            DateTime.UtcNow.AddMinutes(-5), headId.ToString("N"), "test");
        var ct1 = AccountableDocument.Register(book, 1, "test");
        var ct2 = AccountableDocument.Register(book, 2, "test");
        var ct3 = AccountableDocument.Register(book, 3, "test");
        // Operation-only collector: the WCF assignment and Cash Tickets, deliberately no NPM facility assignment.
        var collector = CollectorUser.Create("Bobby Mercado", "C-WCF", "bobby-" + Guid.NewGuid().ToString("N")[..8],
            null, null, new HashedPassword("h"), tenantId);
        collector.OperationAssignments.Add(CollectorOperationAssignment.Assign(tenantId, collector.Id, CollectorOperationCodes.Wcf, "head"));
        ct2.AssignTo(collector.Id, "head");
        ct3.AssignTo(collector.Id, "head");
        var custody = AccountableFormAssignment.Assign(ct2, collector.Id, headId.ToString("N"), DateTime.UtcNow.AddMinutes(-4), "test");
        var custody3 = AccountableFormAssignment.Assign(ct3, collector.Id, headId.ToString("N"), DateTime.UtcNow.AddMinutes(-4), "test");

        await using (var setup = db.CreateContext(tenantId))
        {
            setup.AddRange(facility, stall, contract, classification, policy, book, ct1, ct2, ct3);
            setup.CollectorUsers.Add(collector);
            setup.AccountableFormAssignments.AddRange(custody, custody3);
            if (billFor is not null) setup.UtilityBills.Add(billFor(stall.Id));
            await setup.SaveChangesAsync();
        }
        return new World(tenantId, headId, collector.Id, stall.Id, ct2.Id, ct2.DocumentNumber, period);
    }

    private static WcfCollectionWorkflow Wcf(AppDbContext ctx, World w, string role) =>
        new(ctx, new Actor(role == "Collector" ? w.CollectorId : w.HeadId, w.TenantId, role), new FixedTenant(w.TenantId));

    private static WcfActivationWorkflow Activation(AppDbContext ctx, World w)
    {
        var head = new Actor(w.HeadId, w.TenantId, "Admin");
        return new(ctx, head, new FixedTenant(w.TenantId), new SettlementCutoverWorkflow(ctx, head, new FixedTenant(w.TenantId)));
    }

    private static async Task<CollectorOperationCapabilityDto> CapabilityAsync(AppDbContext ctx, World w) =>
        (await new GetCollectorOperationCapabilitiesQueryHandler(ctx, new Actor(w.CollectorId, w.TenantId, "Collector"),
                new FixedTenant(w.TenantId), new Clock())
            .Handle(new GetCollectorOperationCapabilitiesQuery(), CancellationToken.None))
        .Value!.Operations.Single(o => o.OperationCode == CollectorOperationCodes.Wcf);

    private static SettlementCutoverReconciliationEvidence Attested(Guid collectorId) => new(
        true, true, true, true, true, true, "Head checklist 2026-10-01",
        [new CutoverCollectorEvidence(collectorId, "3.0.0", 1, true, DateTime.UtcNow.AddMinutes(-1), "Device checked by the office")]);

    [SkippableFact]
    public async Task TheHeadEstablishesAndActivatesATenPesoObligation_AndAnOperationOnlyCollectorCollectsItOnce()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);

        // Head/Admin: the source is listed for the period, then the direct approved amount is set. The amount is the office's.
        var sources = (await Wcf(ctx, w, "Admin").GetSetupSourcesAsync(w.Period.Year, w.Period.Month)).Value!;
        var source = Assert.Single(sources);
        Assert.Equal(("12", "Bobby Example", true), (source.StallNo, source.PayerName, source.Editable));
        var set = await Wcf(ctx, w, "Admin").EstablishObligationAsync(new(w.StallId, w.Period.Year, w.Period.Month, 10m));
        Assert.True(set.IsSuccess, set.Error);
        var bill = await ctx.UtilityBills.AsNoTracking().SingleAsync();
        Assert.Equal((10m, UtilityCalculationBasis.DirectApproved, SettlementAuthority.Legacy),
            (bill.WaterCharge, bill.WaterCalculationBasis, bill.WaterSettlementAuthorityState));

        // Before activation the obligation is assessed and outstanding on the Web, and Mobile says why it is not ready.
        var web = Assert.Single((await Wcf(ctx, w, "Admin").GetObligationsAsync(w.Period.Year, w.Period.Month)).Value!);
        Assert.Equal((10m, 10m), (web.AssessedAmount, web.OutstandingAmount));
        Assert.Equal(CollectorOperationCapabilityStatus.PendingCutover, (await CapabilityAsync(ctx, w)).Status);

        // Activation without the attested checklist changes nothing.
        var refused = await Activation(ctx, w).ActivateAsync(new(bill.Id, bill.WaterSourceVersion,
            Attested(w.CollectorId) with { MobileQueuesDrained = false }));
        Assert.False(refused.IsSuccess);
        Assert.Equal(SettlementAuthority.Legacy, (await ctx.UtilityBills.AsNoTracking().SingleAsync()).WaterSettlementAuthorityState);

        var activated = await Activation(ctx, w).ActivateAsync(new(bill.Id, bill.WaterSourceVersion, Attested(w.CollectorId)));
        Assert.True(activated.IsSuccess, activated.Error);
        Assert.Equal(SettlementAuthority.Canonical, activated.Value!.Authority);
        Assert.Equal((10m, 0m, 10m), (activated.Value.OpeningAssessmentAmount, activated.Value.OpeningLegacySettledAmount, activated.Value.OpeningOutstandingAmount));

        // Capability and writer agree: no NPM facility, yet Ready, and the post is accepted.
        var capability = await CapabilityAsync(ctx, w);
        Assert.Equal(CollectorOperationCapabilityStatus.Ready, capability.Status);
        var collector = Wcf(ctx, w, "Collector");
        var quote = Assert.Single((await collector.GetObligationsAsync(w.Period.Year, w.Period.Month)).Value!);
        Assert.True(quote.CanCollectCanonical);
        Assert.Equal((10m, 0m, 10m, "DirectApproved"), (quote.AssessedAmount, quote.CumulativeSettledEvidence, quote.OutstandingAmount, quote.ChargeBasis));

        var request = new WcfCollectionPostRequest(1, Guid.NewGuid(), PhilippineTime.Today, quote.UtilityBillId, 10m,
            quote.WaterSourceVersion, w.CtId, w.CtNumber, DateTime.UtcNow.AddMinutes(-1));
        var posted = await collector.PostMobileAsync(request);
        Assert.True(posted.IsSuccess, posted.Error);
        var retry = await collector.PostMobileAsync(request);
        Assert.True(retry.Value!.ExistingOutcome);

        var collection = await ctx.Collections.Include(x => x.Lines).AsNoTracking().SingleAsync();
        Assert.Equal((10m, w.CollectorId), (collection.TotalAmount, collection.CollectorId!.Value));
        Assert.Equal(CollectionSourcePart.Water, Assert.Single(collection.Lines).SourcePart);
        Assert.Equal(AccountableDocumentState.Assigned, (await ctx.AccountableDocuments.AsNoTracking().SingleAsync(x => x.Id == w.CtId)).State);   // no ticket is consumed (IA-062)

        // Settled: nothing outstanding, yet the operation is still Ready — no debt is not an authorization failure.
        Assert.Empty((await collector.GetObligationsAsync(w.Period.Year, w.Period.Month)).Value!);
        Assert.Equal(CollectorOperationCapabilityStatus.Ready, (await CapabilityAsync(ctx, w)).Status);

        // The canonical obligation is frozen to this writer.
        var revise = await Wcf(ctx, w, "Admin").EstablishObligationAsync(new(w.StallId, w.Period.Year, w.Period.Month, 20m));
        Assert.Equal(ResultStatus.Conflict, revise.Status);
    }

    [SkippableFact]
    public async Task SettingWater_UsesTheSameBillAsElectricity_AndNeverChangesElectricity()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var period = new DateOnly(PhilippineTime.Today.Year, PhilippineTime.Today.Month, 1);
        var w = await SeedAsync(billFor: stallId =>
        {
            var (p, c, r) = UtilityBill.DirectApprovedReadings(50m);
            var b = UtilityBill.Create(stallId, period.Year, period.Month, p, c, r, 0m, 0m, 0m, "test");
            b.SetCalculationBasis(UtilityCalculationBasis.DirectApproved, UtilityCalculationBasis.DirectApproved);
            b.RecordPayment("OR-1", null, null, PaymentStatus.Partial, 20m, PaymentStatus.Unpaid, 0m, updatedBy: "test");
            return b;
        });
        await using var ctx = db.CreateContext(w.TenantId);
        var before = await ctx.UtilityBills.AsNoTracking().SingleAsync();

        var set = await Wcf(ctx, w, "Admin").EstablishObligationAsync(new(w.StallId, w.Period.Year, w.Period.Month, 10m));

        Assert.True(set.IsSuccess, set.Error);
        var after = await ctx.UtilityBills.AsNoTracking().SingleAsync();          // still one bill for the stall and month
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(10m, after.WaterCharge);
        Assert.Equal((before.ElecCharge, before.ElecAmountPaid, before.ElecStatus, before.ElecORNumber, before.ElectricitySourceVersion),
            (after.ElecCharge, after.ElecAmountPaid, after.ElecStatus, after.ElecORNumber, after.ElectricitySourceVersion));
    }

    [SkippableFact]
    public async Task OnlyTheOfficeSetsUpWater_AndNeverForAPeriodThatHasNotBegun()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);

        var asCollector = await Wcf(ctx, w, "Collector").EstablishObligationAsync(new(w.StallId, w.Period.Year, w.Period.Month, 10m));
        Assert.Equal(ResultStatus.Forbidden, asCollector.Status);
        var next = w.Period.AddMonths(1);
        var future = await Wcf(ctx, w, "Admin").EstablishObligationAsync(new(w.StallId, next.Year, next.Month, 10m));
        Assert.Equal(ResultStatus.Invalid, future.Status);
        Assert.Empty(await ctx.UtilityBills.AsNoTracking().ToListAsync());
    }
}
