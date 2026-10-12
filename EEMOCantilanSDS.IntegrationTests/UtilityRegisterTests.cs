using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The ECF / WCF source register on PostgreSQL: a source is a market space of ANY facility with a verified service (an NPM stall as always),
/// never a name; same-looking spaces of different facilities stay separate; the server filters, counts and pages exactly and stably; a prepared
/// amount shows Unpaid/Partial/Settled while a direct period shows only money collected and never an invented balance.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UtilityRegisterTests(PostgresFixture db)
{
    private static readonly DateOnly Today = PhilippineTime.Today;

    private sealed class Actor(Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "head";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "util";
        public Guid? MunicipalityId => tenantId;
    }
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor { public Guid MunicipalityId => id; public void Set(Guid municipalityId) { } }
    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private sealed record World(Guid TenantId, Guid NpmStallA, Guid NpmStallB, Guid NccElectric, Guid NccWater, Guid NccNone, Guid TccElectric, Guid[] Many);

    private async Task<World> SeedAsync(int many = 0)
    {
        var tenant = Municipality.Create($"ut-{Guid.NewGuid():N}"[..12], "Utility", "Province", MunicipalityStatus.Active,
            tenantCode: $"util-{Guid.NewGuid():N}"[..24].ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Add(tenant); await setup.SaveChangesAsync(); }
        await using var ctx = db.CreateContext(tenant.Id);
        var npm = Facility.Create(FacilityCode.NPM, "Public Market", "NPM", municipalityId: tenant.Id);
        var ncc = Facility.Create(FacilityCode.NCC, "New Commercial Center", "NCC", municipalityId: tenant.Id);
        var tcc = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC", municipalityId: tenant.Id);
        ctx.AddRange(npm, ncc, tcc);
        var from = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-6);
        Stall Space(Facility facility, string no, ApplicableFees fees, string occupant, MarketSection? section = null)
        {
            var stall = Stall.Create(facility.Id, no, 900m, fees, section, municipalityId: tenant.Id);
            ctx.Add(stall);
            ctx.Add(Contract.Create(stall.Id, occupant, occupant, from, 5, 900m, createdBy: "test"));
            return stall;
        }
        var a = Space(npm, "V-01", ApplicableFees.BaseRental, "Ana Reyes", MarketSection.VegetableArea);   // NPM: a source without any flag, as always
        var b = Space(npm, "V-02", ApplicableFees.BaseRental, "Ben Cruz", MarketSection.VegetableArea);
        var e = Space(ncc, "1", ApplicableFees.BaseRental | ApplicableFees.Electricity, "Ana Reyes");           // the same payor name at another facility
        var w = Space(ncc, "2", ApplicableFees.BaseRental | ApplicableFees.Water, "Carla Diaz");
        var none = Space(ncc, "3", ApplicableFees.BaseRental, "Dino Lim");                                       // no service: never a source
        var t = Space(tcc, "1", ApplicableFees.BaseRental | ApplicableFees.Electricity | ApplicableFees.Water, "Eva Tan");   // "Stall 1" again, another facility
        var bulk = new List<Guid>();
        for (var i = 0; i < many; i++) bulk.Add(Space(ncc, $"B-{i:000}", ApplicableFees.BaseRental | ApplicableFees.Water, $"Bulk Payor {i:000}").Id);
        await ctx.SaveChangesAsync();
        return new(tenant.Id, a.Id, b.Id, e.Id, w.Id, none.Id, t.Id, bulk.ToArray());
    }

    private UtilityRegisterReader Reader(AppDbContext ctx, World w, string role = "Admin") => new(ctx, new Actor(w.TenantId, role), new FixedTenant(w.TenantId), new Clock());

    private static UtilityRegisterRequest Req(UtilityKind kind, string? status = null, string? search = null, FacilityCode? facility = null, int skip = 0, int take = 25) =>
        new(kind, Today.Year, Today.Month, facility, null, status, search, skip, take);

    private async Task PrepareAsync(World w, Guid stallId, UtilityKind kind, decimal approved)
    {
        await using var ctx = db.CreateContext(w.TenantId);
        var (previous, current, rate) = UtilityBill.DirectApprovedReadings(approved);
        var bill = UtilityBill.Create(stallId, Today.Year, Today.Month,
            kind == UtilityKind.Electricity ? previous : 0m, kind == UtilityKind.Electricity ? current : 0m, kind == UtilityKind.Electricity ? rate : 0m,
            kind == UtilityKind.Water ? previous : 0m, kind == UtilityKind.Water ? current : 0m, kind == UtilityKind.Water ? rate : 0m, "head");
        bill.SetCalculationBasis(kind == UtilityKind.Electricity ? UtilityCalculationBasis.DirectApproved : UtilityCalculationBasis.Metered,
            kind == UtilityKind.Water ? UtilityCalculationBasis.DirectApproved : UtilityCalculationBasis.Metered);
        ctx.UtilityBills.Add(bill);
        await ctx.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task OnlyVerifiedServiceSourcesAppear_AcrossFacilities_AndSameNamesAndNumbersStaySeparate()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);

        var ecf = (await Reader(ctx, w).GetAsync(Req(UtilityKind.Electricity))).Value!;
        var wcf = (await Reader(ctx, w).GetAsync(Req(UtilityKind.Water))).Value!;

        // Electricity: both NPM stalls (as always), the NCC space with the flag, the TCC space with the flag. Not the NCC space with Water only or none.
        Assert.Equal(new[] { w.NccElectric, w.NpmStallA, w.NpmStallB, w.TccElectric }.Order(), ecf.Rows.Select(r => r.StallId).Order());
        // Water: both NPM stalls, the NCC space with Water, the TCC space. Not the electric-only NCC space, and never the flagless one.
        Assert.Equal(new[] { w.NccWater, w.NpmStallA, w.NpmStallB, w.TccElectric }.Order(), wcf.Rows.Select(r => r.StallId).Order());
        Assert.DoesNotContain(w.NccNone, ecf.Rows.Select(r => r.StallId).Concat(wcf.Rows.Select(r => r.StallId)));
        // "Ana Reyes" holds an NPM stall and an NCC space: two sources, each under its own facility.
        var anas = ecf.Rows.Where(r => r.Payer == "Ana Reyes").ToList();
        Assert.Equal(2, anas.Count);
        Assert.Equal(2, anas.Select(r => r.FacilityName).Distinct().Count());
        // "Stall 1" at NCC and at TCC are different sources.
        Assert.Equal(2, ecf.Rows.Count(r => r.StallNo == "1"));
        Assert.Equal(new[] { "New Commercial Center", "Public Market", "Tampak Commercial Center" }, ecf.Facilities.Select(f => f.Name).Order());
        Assert.All(ecf.Rows, r => Assert.Equal(("None", "Unprepared"), (r.Mode, r.Status)));    // nothing is assessed, and nothing is invented
        Assert.Equal(0m, ecf.Totals.Assessed); Assert.Equal(0m, ecf.Totals.Outstanding);
    }

    [SkippableFact]
    public async Task APreparedAmountShowsUnpaid_AndIsNeverRevenue_WhileADirectPeriodShowsNoInventedBalance()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await PrepareAsync(w, w.NpmStallA, UtilityKind.Electricity, 20_000m);
        await PrepareAsync(w, w.NccElectric, UtilityKind.Electricity, 300m);
        await using (var ctx = db.CreateContext(w.TenantId))      // a direct-collection period: frozen as direct, no amount
        {
            var direct = UtilityBill.Create(w.TccElectric, Today.Year, Today.Month, 0, 0, 0, 0, 0, 0, "collector");
            direct.FreezeDirectCollection(CollectionSourcePart.Electricity);
            ctx.UtilityBills.Add(direct);
            await ctx.SaveChangesAsync();
        }
        await using var verify = db.CreateContext(w.TenantId);

        var ecf = (await Reader(verify, w).GetAsync(Req(UtilityKind.Electricity))).Value!;

        var npm = ecf.Rows.Single(r => r.StallId == w.NpmStallA);
        Assert.Equal(("Prepared", "Unpaid", 20_000m, 0m, 20_000m), (npm.Mode, npm.Status, npm.Approved, npm.Collected, npm.Outstanding));
        Assert.True(npm.CanPrepare);                              // nothing collected yet, so the amount may still be corrected
        var direct2 = ecf.Rows.Single(r => r.StallId == w.TccElectric);
        Assert.Equal(("Direct", null, 0m, 0m), (direct2.Mode, direct2.Approved, direct2.Outstanding, direct2.Collected));
        Assert.False(direct2.CanPrepare);                         // a direct period cannot be switched to prepared by editing a field
        Assert.NotNull(direct2.PrepareLock);
        Assert.Equal(20_300m, ecf.Totals.Assessed);               // approved amounts only
        Assert.Equal(20_300m, ecf.Totals.Outstanding);
        Assert.Equal(0m, ecf.Totals.Collected);                   // an approved amount is never revenue
        Assert.Equal(0, await verify.Collections.CountAsync());   // and reading creates nothing
    }

    [SkippableFact]
    public async Task LegacyPaymentOnAPreparedPart_IsCollected_PartialStaysOutstanding_AndAFrozenPartCannotBePrepared()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await PrepareAsync(w, w.NpmStallA, UtilityKind.Water, 500m);
        await PrepareAsync(w, w.NpmStallB, UtilityKind.Water, 400m);
        await using (var ctx = db.CreateContext(w.TenantId))
        {
            var a = await ctx.UtilityBills.SingleAsync(x => x.StallId == w.NpmStallA);
            a.RecordPayment(null, "WCF-1", null, PaymentStatus.Unpaid, null, PaymentStatus.Partial, 150m, null, "head");
            var b = await ctx.UtilityBills.SingleAsync(x => x.StallId == w.NpmStallB);
            b.RecordPayment(null, "WCF-2", null, PaymentStatus.Unpaid, null, PaymentStatus.Paid, null, null, "head");
            await ctx.SaveChangesAsync();
        }
        await using var verify = db.CreateContext(w.TenantId);

        var wcf = (await Reader(verify, w).GetAsync(Req(UtilityKind.Water))).Value!;

        var partial = wcf.Rows.Single(r => r.StallId == w.NpmStallA);
        Assert.Equal(("Partial", 500m, 150m, 350m), (partial.Status, partial.Approved, partial.Collected, partial.Outstanding));
        Assert.False(partial.CanPrepare);                         // a part with money recorded is frozen
        var settled = wcf.Rows.Single(r => r.StallId == w.NpmStallB);
        Assert.Equal(("Settled", 400m, 400m, 0m), (settled.Status, settled.Approved, settled.Collected, settled.Outstanding));
        Assert.Equal((900m, 550m, 350m), (wcf.Totals.Assessed, wcf.Totals.Collected, wcf.Totals.Outstanding));
        Assert.Equal(["Partial"], (await Reader(verify, w).GetAsync(Req(UtilityKind.Water, status: "Partial"))).Value!.Rows.Select(r => r.Status));
        Assert.Equal(4, (await Reader(verify, w).GetAsync(Req(UtilityKind.Water, status: "Partial"))).Value!.Totals.Sources);   // header totals ignore the status filter
    }

    [SkippableFact]
    public async Task SixtyPlusSources_PageExactlyAndStably_WithRealServerSideSearchAndFilters()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync(many: 70);
        await using var ctx = db.CreateContext(w.TenantId);
        var reader = Reader(ctx, w);

        var first = (await reader.GetAsync(Req(UtilityKind.Water, take: 25))).Value!;
        Assert.Equal(25, first.Rows.Count);
        Assert.Equal(74, first.Total);                            // 70 bulk + 2 NPM + the NCC and TCC Water spaces

        var walked = new List<Guid>();
        for (var skip = 0; skip < first.Total; skip += 25)
            walked.AddRange((await reader.GetAsync(Req(UtilityKind.Water, skip: skip, take: 25))).Value!.Rows.Select(r => r.StallId));
        Assert.Equal(first.Total, walked.Count);
        Assert.Equal(walked.Count, walked.Distinct().Count());    // no repeat, no gap across page boundaries
        Assert.Equal(walked, (await reader.GetAsync(Req(UtilityKind.Water, skip: 0, take: 100))).Value!.Rows.Select(r => r.StallId).ToList());   // the same order whatever the page size

        var found = (await reader.GetAsync(Req(UtilityKind.Water, search: "bulk payor 042"))).Value!;
        Assert.Equal("Bulk Payor 042", Assert.Single(found.Rows).Payer);
        Assert.Equal(1, found.Total);
        Assert.Equal(first.Totals, found.Totals);                 // search narrows the rows, never the header
        var byFacility = (await reader.GetAsync(Req(UtilityKind.Water, facility: FacilityCode.NPM))).Value!;
        Assert.All(byFacility.Rows, r => Assert.Equal(FacilityCode.NPM, r.Facility));
        Assert.Equal(2, byFacility.Total);
        var byStallNo = (await reader.GetAsync(Req(UtilityKind.Water, search: "B-007"))).Value!;
        Assert.Equal("B-007", Assert.Single(byStallNo.Rows).StallNo);
        Assert.True((await reader.GetAsync(Req(UtilityKind.Water, take: 5_000))).Value!.Rows.Count <= UtilityRegisterReader.MaxPage);   // the page is bounded whatever is asked
    }

    [SkippableFact]
    public async Task TheRegisterIsOfficeOnly_AndNeverCrossesTenants()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var other = await SeedAsync();
        await using var ctx = db.CreateContext(w.TenantId);

        Assert.Equal(ResultStatus.Forbidden, (await Reader(ctx, w, "Collector").GetAsync(Req(UtilityKind.Electricity))).Status);
        var own = (await Reader(ctx, w).GetAsync(Req(UtilityKind.Electricity))).Value!;
        Assert.DoesNotContain(own.Rows, r => r.StallId == other.NpmStallA);
        await using var otherCtx = db.CreateContext(other.TenantId);
        var elsewhere = (await Reader(otherCtx, other).GetAsync(Req(UtilityKind.Electricity))).Value!;
        Assert.DoesNotContain(elsewhere.Rows, r => r.StallId == w.NpmStallA);
        Assert.Equal(ResultStatus.Invalid, (await Reader(ctx, w).GetAsync(new(UtilityKind.Electricity, 1999, 13))).Status);
    }
}
