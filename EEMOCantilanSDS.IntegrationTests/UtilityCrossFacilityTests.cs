using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityReading;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// ECF and WCF are MEEDO utility operations, not parts of NPM rent. A verified service source of ANY facility is collectable by a collector
/// assigned to that facility - prepared (against the office's approved amount, never overpaid, never replaced by a direct entry) or direct
/// (an actual receipt that never manufactures an assessment) - while a space with no verified service, a facility the collector is not
/// assigned to, and another tenant are all refused. The register states the same money the canonical Collections hold.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UtilityCrossFacilityTests(PostgresFixture db)
{
    private static DateOnly Today => PhilippineTime.Today;

    private sealed class Actor(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "field-collector" : "head";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "utility-x";
        public Guid? MunicipalityId => tenantId;
    }
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor { public Guid MunicipalityId => id; public void Set(Guid municipalityId) { } }
    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private sealed record World(Guid TenantId, Guid HeadId, Guid TccCollector, Guid NpmOnlyCollector, Guid TccWater, Guid TccElectric, Guid TccBoth, Guid TccNone, Guid NpmStall);

    private async Task<World> SeedAsync()
    {
        var tenant = Municipality.Create($"ux-{Guid.NewGuid():N}"[..12], "Utility X", "Province", MunicipalityStatus.Active,
            tenantCode: $"utilx-{Guid.NewGuid():N}"[..24].ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Add(tenant); await setup.SaveChangesAsync(); }
        await using var ctx = db.CreateContext(tenant.Id);
        var npm = Facility.Create(FacilityCode.NPM, "Public Market", "NPM", archetype: BillingArchetype.DailyStall, municipalityId: tenant.Id);
        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC", municipalityId: tenant.Id);
        ctx.AddRange(npm, tcc);
        var from = new DateOnly(Today.Year, Today.Month, 1).AddYears(-1);
        Stall Space(Facility f, string no, ApplicableFees fees, string occupant)
        {
            var stall = Stall.Create(f.Id, no, 900m, fees, null, municipalityId: tenant.Id);
            ctx.Add(stall); ctx.Add(Contract.Create(stall.Id, occupant, occupant, from, 5, 900m, createdBy: "test")); return stall;
        }
        var water = Space(tcc, "W-1", ApplicableFees.BaseRental | ApplicableFees.Water, "Wilma Water");
        var electric = Space(tcc, "E-1", ApplicableFees.BaseRental | ApplicableFees.Electricity, "Eddie Electric");
        var both = Space(tcc, "B-1", ApplicableFees.BaseRental | ApplicableFees.Electricity | ApplicableFees.Water, "Bea Both");
        var none = Space(tcc, "N-1", ApplicableFees.BaseRental, "Nora None");
        var npmStall = Space(npm, "V-1", ApplicableFees.BaseRental, "Vic Vendor");
        var ecf = RevenueClassification.Create(RevenueClassificationCodes.Ecf, tenant.Id);
        var wcf = RevenueClassification.Create(RevenueClassificationCodes.Wcf, tenant.Id);
        ctx.AddRange(ecf, wcf,
            RevenueClassificationPolicy.Create(ecf.Id, new DateOnly(2000, 1, 1), "Electricity Consumption Fees", RevenueInstrumentType.OfficialReceipt, tenant.Id),
            RevenueClassificationPolicy.Create(wcf.Id, new DateOnly(2000, 1, 1), "Water Consumption Fees", RevenueInstrumentType.CashTicket, tenant.Id));
        var tccCollector = CollectorUser.Create("TCC Collector", "C-1", "tcc-" + Guid.NewGuid().ToString("N")[..8], null, null, new HashedPassword("h"), tenant.Id);
        tccCollector.OperationAssignments.Add(CollectorOperationAssignment.Assign(tenant.Id, tccCollector.Id, CollectorOperationCodes.Wcf, "head"));
        var npmCollector = CollectorUser.Create("NPM Collector", "C-2", "npm-" + Guid.NewGuid().ToString("N")[..8], null, null, new HashedPassword("h"), tenant.Id);
        npmCollector.OperationAssignments.Add(CollectorOperationAssignment.Assign(tenant.Id, npmCollector.Id, CollectorOperationCodes.Wcf, "head"));
        ctx.AddRange(tccCollector, npmCollector);
        ctx.Add(CollectorFacilityAssignment.Create(tccCollector.Id, tcc.Id, FacilityCode.TCC));
        ctx.Add(CollectorFacilityAssignment.Create(npmCollector.Id, npm.Id, FacilityCode.NPM));
        await ctx.SaveChangesAsync();
        return new(tenant.Id, Guid.NewGuid(), tccCollector.Id, npmCollector.Id, water.Id, electric.Id, both.Id, none.Id, npmStall.Id);
    }

    private static WcfCollectionWorkflow Wcf(AppDbContext ctx, World w, Guid userId, string role) =>
        new(ctx, new Actor(userId, w.TenantId, role), new FixedTenant(w.TenantId), clock: new Clock());
    private static WcfMobileCollectionWorkflow Office(AppDbContext ctx, World w) =>
        new(ctx, new Actor(w.HeadId, w.TenantId, "Admin"), new FixedTenant(w.TenantId), new Clock());
    private static CollectionComposerWorkflow Composer(AppDbContext ctx, World w, Guid userId, string role) =>
        new(ctx, new Actor(userId, w.TenantId, role), new FixedTenant(w.TenantId), new Clock());
    private static UtilityRegisterReader Register(AppDbContext ctx, World w) =>
        new(ctx, new Actor(w.HeadId, w.TenantId, "Admin"), new FixedTenant(w.TenantId), new Clock());

    private static WcfCollectionPostRequest DirectWater(Guid stall, decimal amount, Guid? op = null) =>
        new(1, op ?? Guid.NewGuid(), Today, Guid.Empty, amount, 0, StallId: stall, BillingYear: Today.Year, BillingMonth: Today.Month);

    [SkippableFact]
    public async Task ADirectWaterReceipt_AtANonNpmFacility_IsACashTicketCollection_WithNoInventedAssessment()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        Assert.True((await Office(ctx, w).EnableAsync()).IsSuccess);

        var sources = (await Wcf(ctx, w, w.TccCollector, "Collector").GetMobileSourcesAsync(Today.Year, Today.Month)).Value!;
        // The assigned facility's verified Water sources, plus the NPM stalls every WCF-assigned collector has always had (IA-054); never the flagless or the electric-only space.
        Assert.Equal(new[] { w.TccBoth, w.TccWater, w.NpmStall }.Order(), sources.Select(s => s.StallId).Order());

        var op = Guid.NewGuid();
        var posted = await Wcf(ctx, w, w.TccCollector, "Collector").PostMobileAsync(DirectWater(w.TccWater, 250m, op));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.True((await Wcf(ctx, w, w.TccCollector, "Collector").PostMobileAsync(DirectWater(w.TccWater, 250m, op))).Value!.ExistingOutcome);   // a retry is the same Collection
        Assert.Equal(1, await ctx.Collections.CountAsync());
        var collection = await ctx.Collections.Include(c => c.Lines).SingleAsync();
        Assert.Equal((250m, w.TccCollector), (collection.TotalAmount, collection.CollectorId!.Value));
        var line = Assert.Single(collection.Lines);
        Assert.Equal(CollectionSourcePart.Water, line.SourcePart);
        var policy = await ctx.RevenueClassificationPolicies.SingleAsync(p => p.Id == line.RevenueClassificationPolicyId);
        Assert.Equal(RevenueInstrumentType.CashTicket, policy.PermittedInstrumentType);

        var bill = await ctx.UtilityBills.SingleAsync(b => b.StallId == w.TccWater);
        Assert.True(bill.WaterDirectCollection);
        Assert.Equal(0m, bill.WaterCharge);                                                                  // no ₱250 monthly assessment was manufactured
        var row = (await Register(ctx, w).GetAsync(new(UtilityKind.Water, Today.Year, Today.Month, FacilityCode.TCC))).Value!.Rows.Single(r => r.StallId == w.TccWater);
        Assert.Equal(("Direct", "Direct", 250m, 0m, (decimal?)null), (row.Mode, row.Status, row.Collected, row.Outstanding, row.Approved));   // money collected, nothing owed
        Assert.Equal(250m, (await Register(ctx, w).GetAsync(new(UtilityKind.Water, Today.Year, Today.Month, FacilityCode.TCC))).Value!.Totals.DirectCollected);

        // A second, independent direct payment is a second Collection with its own SRC.
        var current = (await Wcf(ctx, w, w.TccCollector, "Collector").GetMobileSourcesAsync(Today.Year, Today.Month, selectedStallId: w.TccWater)).Value!.Single();
        Assert.True((await Wcf(ctx, w, w.TccCollector, "Collector").PostMobileAsync(DirectWater(w.TccWater, 100m) with { WaterSourceVersion = current.WaterSourceVersion })).IsSuccess);
        Assert.Equal(2, await ctx.Collections.CountAsync());
        Assert.Equal(350m, (await Register(ctx, w).GetAsync(new(UtilityKind.Water, Today.Year, Today.Month, FacilityCode.TCC))).Value!.Rows.Single(r => r.StallId == w.TccWater).Collected);
    }

    [SkippableFact]
    public async Task ASpaceWithoutVerifiedService_AFacilityTheCollectorIsNotAssignedTo_AndAnotherUtility_AreRefused()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        Assert.True((await Office(ctx, w).EnableAsync()).IsSuccess);

        // The flagless TCC space, and the TCC space that has Electricity only, are not Water sources.
        Assert.False((await Wcf(ctx, w, w.TccCollector, "Collector").PostMobileAsync(DirectWater(w.TccNone, 50m))).IsSuccess);
        Assert.False((await Wcf(ctx, w, w.TccCollector, "Collector").PostMobileAsync(DirectWater(w.TccElectric, 50m))).IsSuccess);
        // A collector assigned to NPM only cannot collect at TCC, and does not even see it.
        Assert.False((await Wcf(ctx, w, w.NpmOnlyCollector, "Collector").PostMobileAsync(DirectWater(w.TccWater, 50m))).IsSuccess);
        Assert.DoesNotContain(w.TccWater, (await Wcf(ctx, w, w.NpmOnlyCollector, "Collector").GetMobileSourcesAsync(Today.Year, Today.Month)).Value!.Select(s => s.StallId));
        Assert.Equal(0, await ctx.Collections.CountAsync());
        Assert.Empty(await ctx.UtilityBills.ToListAsync());                                                  // refusing creates no bill either

        // The same flagless space is refused for ECF; the TCC collector has no ECF source at the NPM.
        var ecfSources = (await Composer(ctx, w, w.TccCollector, "Collector").GetMobileEcfSourcesAsync()).Value!;
        Assert.DoesNotContain(ecfSources, s => s.StallId == w.TccNone || s.StallId == w.TccWater);
        Assert.DoesNotContain(ecfSources, s => s.StallId == w.NpmStall);
        Assert.Equal(new[] { w.TccBoth, w.TccElectric }.Order(), ecfSources.Select(s => s.StallId).Distinct().Order());
    }

    [SkippableFact]
    public async Task APreparedWaterAmount_IsPartiallyThenFullySettled_NeverOverpaid_AndTheModeCannotBeSwitched()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        Assert.True((await Office(ctx, w).EnableAsync()).IsSuccess);
        Assert.True((await Wcf(ctx, w, w.HeadId, "Admin").EstablishObligationAsync(new(w.TccWater, Today.Year, Today.Month, 500m))).IsSuccess);   // the Head prepares ₱500 at a non-NPM facility
        Assert.Equal(0, await ctx.Collections.CountAsync());                                                // preparing is not a collection
        var collector = Wcf(ctx, w, w.TccCollector, "Collector");
        var source = Assert.Single((await collector.GetMobileSourcesAsync(Today.Year, Today.Month, selectedStallId: w.TccWater)).Value!);
        Assert.Equal((WcfSourceState.Prepared, 500m), (source.State, source.OutstandingAmount));

        WcfCollectionPostRequest Pay(decimal amount) => new(1, Guid.NewGuid(), Today, source.UtilityBillId!.Value, amount, source.WaterSourceVersion);
        Assert.True((await collector.PostMobileAsync(Pay(200m))).IsSuccess);
        var partial = (await Register(ctx, w).GetAsync(new(UtilityKind.Water, Today.Year, Today.Month, FacilityCode.TCC))).Value!.Rows.Single(r => r.StallId == w.TccWater);
        Assert.Equal(("Partial", 500m, 200m, 300m), (partial.Status, partial.Approved, partial.Collected, partial.Outstanding));
        Assert.False(partial.CanPrepare);                                                                    // money is on it: the amount is frozen

        var fresh = Assert.Single((await collector.GetMobileSourcesAsync(Today.Year, Today.Month, selectedStallId: w.TccWater)).Value!);
        WcfCollectionPostRequest Next(decimal amount) => new(1, Guid.NewGuid(), Today, fresh.UtilityBillId!.Value, amount, fresh.WaterSourceVersion);
        Assert.False((await collector.PostMobileAsync(Next(300.01m))).IsSuccess);                           // overpayment is refused
        Assert.True((await collector.PostMobileAsync(Next(300m))).IsSuccess);
        var settled = (await Register(ctx, w).GetAsync(new(UtilityKind.Water, Today.Year, Today.Month, FacilityCode.TCC))).Value!.Rows.Single(r => r.StallId == w.TccWater);
        Assert.Equal("Settled", settled.Status);
        Assert.Equal((500m, 0m), (settled.Collected, settled.Outstanding));
        Assert.Equal(2, await ctx.Collections.CountAsync());                                                // each payment is its own Collection/SRC
        Assert.Equal(500m, await ctx.Collections.SumAsync(c => c.TotalAmount));
        Assert.False((await collector.PostMobileAsync(Next(1m))).IsSuccess);                                // nothing left to collect

        // A second source frozen as direct cannot later be given an approved amount.
        Assert.True((await Wcf(ctx, w, w.TccCollector, "Collector").PostMobileAsync(DirectWater(w.TccBoth, 40m))).IsSuccess);
        var prepare = await Wcf(ctx, w, w.HeadId, "Admin").EstablishObligationAsync(new(w.TccBoth, Today.Year, Today.Month, 900m));
        Assert.False(prepare.IsSuccess);
        Assert.Equal(0m, (await ctx.UtilityBills.AsNoTracking().SingleAsync(b => b.StallId == w.TccBoth)).WaterCharge);
    }

    [SkippableFact]
    public async Task OfficePreparationOfElectricity_AtANonNpmFacility_NeedsAVerifiedElectricityService()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var repo = new EEMOCantilanSDS.Infrastructure.Repositories.Payments.UtilityBillRepository(ctx);
        RecordUtilityReadingCommand Command(Guid stall, decimal amount) =>
            new(stall, Today.Year, Today.Month, 0, 0, 0, 0, 0, 0, null, ElecApprovedAmount: amount);
        var handler = new RecordUtilityReadingCommandHandler(repo, new EEMOCantilanSDS.Infrastructure.Repositories.StallRepository(ctx),
            new Actor(w.HeadId, w.TenantId, "Admin"), new UnitOfWork(ctx),
            new NoCache(), new FixedCode());

        Assert.True((await handler.Handle(Command(w.TccElectric, 20_000m), default)).IsSuccess);            // a verified ECF source at TCC
        Assert.False((await handler.Handle(Command(w.TccNone, 20_000m), default)).IsSuccess);              // no verified service
        Assert.False((await handler.Handle(Command(w.TccWater, 20_000m), default)).IsSuccess);              // Water only: no Electricity
        var row = (await Register(ctx, w).GetAsync(new(UtilityKind.Electricity, Today.Year, Today.Month, FacilityCode.TCC))).Value!.Rows.Single(r => r.StallId == w.TccElectric);
        Assert.Equal(("Prepared", "Unpaid", 20_000m, 0m, 20_000m), (row.Mode, row.Status, row.Approved, row.Collected, row.Outstanding));
        Assert.Equal(0, await ctx.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task ADirectElectricityReceipt_AtANonNpmFacility_IsAnOfficialReceiptCollection_AndOnlyForTheAssignedFacility()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        Guid Version(Guid stall) => ctx.UtilityBills.AsNoTracking().Where(b => b.StallId == stall && b.BillingMonth == Today.Month).Select(b => b.Id).SingleOrDefault();
        MobileEcfPostRequest Direct(Guid stall, decimal amount, Guid? op = null) =>
            new(op ?? Guid.NewGuid(), Guid.Empty, amount, 0, Today, stall, Today.Year, Today.Month);

        // A collector of the NPM only is refused at TCC; nothing is created.
        Assert.False((await Composer(ctx, w, w.NpmOnlyCollector, "Collector").PostMobileEcfAsync(Direct(w.TccElectric, 800m))).IsSuccess);
        // The TCC collector may not collect Electricity at a TCC space with no verified electricity service.
        Assert.False((await Composer(ctx, w, w.TccCollector, "Collector").PostMobileEcfAsync(Direct(w.TccNone, 800m))).IsSuccess);
        Assert.False((await Composer(ctx, w, w.TccCollector, "Collector").PostMobileEcfAsync(Direct(w.TccWater, 800m))).IsSuccess);
        Assert.Equal(0, await ctx.Collections.CountAsync());

        var op = Guid.NewGuid();
        var posted = await Composer(ctx, w, w.TccCollector, "Collector").PostMobileEcfAsync(Direct(w.TccElectric, 800m, op));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.True((await Composer(ctx, w, w.TccCollector, "Collector").PostMobileEcfAsync(Direct(w.TccElectric, 800m, op))).Value!.ReturnedExistingOutcome);
        var collection = await ctx.Collections.Include(c => c.Lines).SingleAsync();
        Assert.Equal((800m, w.TccCollector), (collection.TotalAmount, collection.CollectorId!.Value));
        var line = Assert.Single(collection.Lines);
        Assert.Equal(CollectionSourcePart.Electricity, line.SourcePart);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, (await ctx.RevenueClassificationPolicies.SingleAsync(p => p.Id == line.RevenueClassificationPolicyId)).PermittedInstrumentType);
        var row = (await Register(ctx, w).GetAsync(new(UtilityKind.Electricity, Today.Year, Today.Month, FacilityCode.TCC))).Value!.Rows.Single(r => r.StallId == w.TccElectric);
        Assert.Equal(("Direct", 800m, 0m), (row.Mode, row.Collected, row.Outstanding));                       // collected money, no manufactured balance
        // Electricity does not settle Water on the same space, and the Water register is untouched.
        Assert.Equal(0m, (await Register(ctx, w).GetAsync(new(UtilityKind.Water, Today.Year, Today.Month))).Value!.Totals.Collected);
    }

    private sealed class PrepareSender(RecordUtilityReadingCommandHandler handler) : MediatR.ISender
    {
        public async Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is not RecordUtilityReadingCommand command) throw new NotSupportedException();
            return (TResponse)(object)await handler.Handle(command, ct);
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : MediatR.IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private UtilityPreparationWorkflow Preparation(AppDbContext ctx, World w)
    {
        var actor = new Actor(w.HeadId, w.TenantId, "Admin");
        var handler = new RecordUtilityReadingCommandHandler(new EEMOCantilanSDS.Infrastructure.Repositories.Payments.UtilityBillRepository(ctx),
            new EEMOCantilanSDS.Infrastructure.Repositories.StallRepository(ctx), actor, new UnitOfWork(ctx), new NoCache(), new FixedCode());
        return new(new PrepareSender(handler), Wcf(ctx, w, w.HeadId, "Admin"), new EEMOCantilanSDS.Infrastructure.Repositories.Payments.UtilityBillRepository(ctx), actor);
    }

    [SkippableFact]
    public async Task BatchPreparation_GivesEachSourceItsOwnAmount_RefusesTheUnfitOnesWithAReason_AndCreatesNoCollection()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var year = Today.Year; var month = Today.Month;

        var result = (await Preparation(ctx, w).PrepareAsync(new(UtilityKind.Water, year, month, [
            new(w.TccWater, 500m), new(w.TccBoth, 700m), new(w.TccNone, 300m), new(w.NpmStall, 0m)]))).Value!;

        Assert.Equal((2, 2), (result.PreparedCount, result.RefusedCount));
        Assert.True(result.Outcomes.Single(o => o.StallId == w.TccWater).Prepared);
        Assert.True(result.Outcomes.Single(o => o.StallId == w.TccBoth).Prepared);
        Assert.Contains("verified Water", result.Outcomes.Single(o => o.StallId == w.TccNone).Reason);       // no verified service: refused, not silently skipped
        Assert.Contains("positive", result.Outcomes.Single(o => o.StallId == w.NpmStall).Reason);
        Assert.Equal(new[] { 500m, 700m }.Order(), (await ctx.UtilityBills.AsNoTracking().ToListAsync()).Select(b => b.WaterCharge).Order());   // independent amounts, nothing split
        Assert.Equal(0, await ctx.Collections.CountAsync());

        // The same request again changes nothing and creates no duplicate bill.
        var again = (await Preparation(ctx, w).PrepareAsync(new(UtilityKind.Water, year, month, [new(w.TccWater, 500m)]))).Value!;
        Assert.Equal(1, again.PreparedCount);
        Assert.Equal(2, await ctx.UtilityBills.CountAsync());

        // A stale version is refused; a duplicate source in one batch is refused as a whole.
        var stale = (await Preparation(ctx, w).PrepareAsync(new(UtilityKind.Water, year, month, [new(w.TccWater, 600m, ExpectedVersion: 999)]))).Value!;
        Assert.Contains("changed after it was shown", stale.Outcomes.Single().Reason);
        Assert.Equal(ResultStatus.Invalid, (await Preparation(ctx, w).PrepareAsync(new(UtilityKind.Water, year, month, [new(w.TccWater, 1m), new(w.TccWater, 2m)]))).Status);
        Assert.Equal(ResultStatus.Forbidden, (await new UtilityPreparationWorkflow(new PrepareSender(null!), Wcf(ctx, w, w.TccCollector, "Collector"),
            new EEMOCantilanSDS.Infrastructure.Repositories.Payments.UtilityBillRepository(ctx), new Actor(w.TccCollector, w.TenantId, "Collector"))
            .PrepareAsync(new(UtilityKind.Water, year, month, [new(w.TccWater, 1m)]))).Status);       // a collector never prepares
    }

    [SkippableFact]
    public async Task ElectricityAndWaterPreparation_AreIndependent_AndCollectedMoneyFreezesAnAmount()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);
        var year = Today.Year; var month = Today.Month;
        Assert.True((await Office(ctx, w).EnableAsync()).IsSuccess);

        Assert.Equal(1, (await Preparation(ctx, w).PrepareAsync(new(UtilityKind.Water, year, month, [new(w.TccBoth, 500m)]))).Value!.PreparedCount);
        Assert.Equal(1, (await Preparation(ctx, w).PrepareAsync(new(UtilityKind.Electricity, year, month, [new(w.TccBoth, 20_000m)]))).Value!.PreparedCount);
        var bill = await ctx.UtilityBills.AsNoTracking().SingleAsync(b => b.StallId == w.TccBoth);
        Assert.Equal((500m, 20_000m), (bill.WaterCharge, bill.ElecCharge));                                   // preparing Electricity never overwrote the Water amount

        // Water collected on the prepared amount freezes it; Electricity on the same space can still be revised.
        var source = (await Wcf(ctx, w, w.TccCollector, "Collector").GetMobileSourcesAsync(year, month, selectedStallId: w.TccBoth)).Value!.Single();
        Assert.True((await Wcf(ctx, w, w.TccCollector, "Collector").PostMobileAsync(new(1, Guid.NewGuid(), Today, source.UtilityBillId!.Value, 100m, source.WaterSourceVersion))).IsSuccess);
        var frozen = (await Preparation(ctx, w).PrepareAsync(new(UtilityKind.Water, year, month, [new(w.TccBoth, 900m)]))).Value!;
        Assert.Equal(0, frozen.PreparedCount);
        Assert.Equal(1, (await Preparation(ctx, w).PrepareAsync(new(UtilityKind.Electricity, year, month, [new(w.TccBoth, 21_000m)]))).Value!.PreparedCount);
        var after = await ctx.UtilityBills.AsNoTracking().SingleAsync(b => b.StallId == w.TccBoth);
        Assert.Equal((500m, 21_000m), (after.WaterCharge, after.ElecCharge));
    }

    private sealed class NoCache : 
EEMOCantilanSDS.Application.Common.Caching.IEemoCacheInvalidator
    {
        public Task InvalidateRegionAsync(string region, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidatePeriodAsync(string tenantCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateFacilityPeriodAsync(string tenantCode, FacilityCode facilityCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidatePaymentAffectedViewsAsync(string tenantCode, FacilityCode? facilityCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateReferenceDataAsync(string tenantCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateTenantAsync(string tenantCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class FixedCode : ITenantContext { public string TenantCode => "utility-x"; }
}
