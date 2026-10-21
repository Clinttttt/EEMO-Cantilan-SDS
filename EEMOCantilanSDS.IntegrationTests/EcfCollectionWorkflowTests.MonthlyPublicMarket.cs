using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Queries.Mobile.GetMobileMonthlyCollection;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// A Public Market whose LGU declared STANDARD MONTHLY RENTAL (NPM facility, stored archetype MonthlyRental) runs on the same monthly rent
/// source TCC/NCC use: one obligation per stall and month, paid in full or in installments, each with its own SRC, and no daily calendar.
/// The cross-channel suite (Head/Mobile concurrency, reversal, remittance, reports) also runs for it; these cover what is specific to the market.
/// </summary>
public sealed partial class EcfCollectionWorkflowTests
{
    [SkippableFact]
    public async Task Monthly_public_market_collects_one_obligation_in_installments_with_distinct_SRCs_and_no_daily_rows()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var c = await ArrangeChannelsAsync(rate: 1500m, facility: FacilityCode.NPM);

        var first = await HeadPostAsync(c, 600, await VersionAsync(c)); Assert.True(first.IsSuccess, first.Error);
        Assert.Equal((900m, PaymentStatus.Partial, 1, 600m), await StateAsync(c));
        var second = await HeadPostAsync(c, 900, await VersionAsync(c)); Assert.True(second.IsSuccess, second.Error);
        Assert.NotEqual(first.Value!.ReferenceCode, second.Value!.ReferenceCode);
        Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", second.Value.ReferenceCode);
        Assert.Equal((0m, PaymentStatus.Paid, 2, 1500m), await StateAsync(c));
        Assert.False((await HeadPostAsync(c, 1, await VersionAsync(c))).IsSuccess);          // nothing is owed any more

        await using var read = db.CreateContext(c.Seed.TenantId);
        Assert.Equal(0, await read.DailyCollections.CountAsync());                          // no daily assessment or collection exists
        var stall = await read.Stalls.AsNoTracking().SingleAsync(x => x.Id == c.Rent.StallId);
        Assert.Null(stall.DailyRate);                                                       // the rent is the stall's/contract's monthly amount
        var classifications = await read.CollectionLines.AsNoTracking().Select(x => x.RevenueClassificationId).Distinct().ToListAsync();
        Assert.Equal(c.Rent.ClassificationId, Assert.Single(classifications));              // one rent classification, never a fee or weighing line
        var cells = (await new GetOfficialMonthlyIncomeQueryHandler(read, new LegacyMonthlyIncomeReader(read), c.Head, c.Tenant, new ProofClock())
            .Handle(new(c.Seed.Period.Year, c.Seed.Period.Month), default)).Value!.Groups.SelectMany(g => g.Rows)
            .ToDictionary(r => r.Key, r => r.Months[c.Seed.Period.Month - 1].Canonical);
        Assert.Equal(1500m, cells["RENT_NPM"]);                                             // counted once, on the market's own rent row
        Assert.All(cells.Where(x => x.Key != "RENT_NPM" && (x.Key.StartsWith("RENT_") || x.Key == "ICE_PLANT")), x => Assert.Equal(0m, x.Value));
    }

    [SkippableFact]
    public async Task A_daily_collected_market_is_never_a_monthly_rent_source()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);                                      // Cantilan-style: the NPM archetype is DailyStall
        await using var context = db.CreateContext(seed.TenantId);
        var stall = await context.Stalls.AsNoTracking().SingleAsync();
        var adapter = new MonthlyRentCollectionSourceAdapter(context);
        Assert.Null(await adapter.LoadAsync(seed.TenantId, stall.Id, seed.Period.Year, seed.Period.Month, PhilippineTime.Today, false, false, "t", default));
        var facility = await context.Facilities.AsNoTracking().SingleAsync(x => x.Code == FacilityCode.NPM);
        Assert.False(facility.IsMonthlyRentMarket);
        Assert.False(MonthlyRentalFacilities.Bills(facility));
    }

    [SkippableFact]
    public async Task A_new_term_at_a_new_rent_prices_later_months_and_leaves_earlier_obligations_as_they_were()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true, BillingArchetype.MonthlyRental);
        var previous = seed.Period.AddMonths(-1);
        var rent = await SeedRentSourcesAsync(seed, [previous], true, facilityCode: FacilityCode.NPM, monthlyRate: 1500m, sourceOwned: true);
        await using (var write = db.CreateContext(seed.TenantId))
        {
            write.Contracts.Add(Contract.Create(rent.StallId, "Lisa ECF", "Lisa ECF", seed.Period, 5, 1800m, createdBy: "test"));   // renewed at a higher rent
            await write.SaveChangesAsync();
        }
        var actor = new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"); var tenant = new FixedTenant(seed.TenantId);
        await using var context = db.CreateContext(seed.TenantId);
        var flow = new CollectionComposerWorkflow(context, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, FacilityCode.NPM));

        var earlier = (await flow.GetRentObligationAsync(rent.StallId, previous.Year, previous.Month)).Value!;
        var later = (await flow.GetRentObligationAsync(rent.StallId, seed.Period.Year, seed.Period.Month)).Value!;
        Assert.Equal(1500m, earlier.AssessedRentalAmount);                                   // the term that answered for that month, at its own rent
        Assert.Equal(1800m, later.AssessedRentalAmount);
        Assert.NotEqual(earlier.ContractId, later.ContractId);
    }

    [SkippableFact]
    public async Task A_monthly_public_market_receipt_is_tenant_isolated_and_refused_to_an_unauthorised_actor()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var c = await ArrangeChannelsAsync(rate: 1500m, facility: FacilityCode.NPM);
        var version = await VersionAsync(c);

        // Another LGU's Head cannot read or post this market's rent.
        var foreign = Guid.NewGuid();
        await using (var other = db.CreateContext(foreign))
        {
            var theirs = new CollectionComposerWorkflow(other, new TestActor(Guid.NewGuid(), foreign, "SuperAdmin"), new FixedTenant(foreign),
                rentOptions: OfficeReceipts(foreign, FacilityCode.NPM));
            Assert.False((await theirs.GetRentObligationAsync(c.Rent.StallId, c.Seed.Period.Year, c.Seed.Period.Month)).IsSuccess);
            Assert.False((await theirs.PostOfficeRentAsync(new(Guid.NewGuid(), c.Rent.StallId, c.Seed.Period.Year, c.Seed.Period.Month, 600,
                version, PhilippineTime.Today, c.Rent.ContractId))).IsSuccess);
        }
        // A Collector cannot use the Head's office shortcut.
        await using (var collector = db.CreateContext(c.Seed.TenantId))
        {
            var asCollector = new CollectionComposerWorkflow(collector, new CollectorActor(c.Collector, c.Seed.TenantId), c.Tenant,
                rentOptions: OfficeReceipts(c.Seed.TenantId, FacilityCode.NPM));
            Assert.False((await asCollector.PostOfficeRentAsync(new(Guid.NewGuid(), c.Rent.StallId, c.Seed.Period.Year, c.Seed.Period.Month, 600,
                version, PhilippineTime.Today, c.Rent.ContractId))).IsSuccess);
        }
        Assert.Equal((1500m, PaymentStatus.Unpaid, 0, 0m), await StateAsync(c));             // nothing was recorded
    }

    [SkippableFact]
    public async Task The_payor_portal_shows_a_monthly_market_stall_its_monthly_rent_and_no_daily_balance()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true, BillingArchetype.MonthlyRental);
        var rent = await SeedRentSourcesAsync(seed, [seed.Period], false, facilityCode: FacilityCode.NPM, monthlyRate: 1500m, sourceOwned: true);
        var payor = EEMOCantilanSDS.Domain.Entities.Users.PayorUser.Create("Lisa Vendor", "09170000001", new EEMOCantilanSDS.Domain.Common.HashedPassword("hash"));
        await using (var write = db.CreateContext(seed.TenantId))
        {
            write.Add(payor);
            write.Add(EEMOCantilanSDS.Domain.Entities.Users.PayorStallLink.Create(payor.Id, rent.StallId));
            await write.SaveChangesAsync();
        }

        await using var context = db.CreateContext(seed.TenantId);
        var repo = new EEMOCantilanSDS.Infrastructure.Repositories.PayorRepository(context, null!);   // a daily balance would need the settlement service: it must not be asked
        var balance = Assert.Single(await repo.GetBalancesAsync(payor.Id), b => b.StallId == rent.StallId);
        Assert.Equal((1500m, false, 0m), (balance.OutstandingBalance, balance.IsDailyBilled, balance.DailyRate));
        var items = await repo.GetPayableItemsAsync(payor.Id);
        var item = Assert.Single(items, i => i.StallId == rent.StallId);
        Assert.Equal((1500m, EEMOCantilanSDS.Application.Dtos.Payors.PayorPayableKind.Monthly), (item.BalanceDue, item.Kind));
    }

    [SkippableFact]
    public async Task The_preview_activation_tool_accepts_a_monthly_market_and_refuses_a_daily_one()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var connection = db.ApiConnectionString;
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connection);
        var daily = await SeedEcfAsync(canonical: true);                                    // the NPM facility is DailyStall
        await using (var context = db.CreateContext(daily.TenantId))
        {
            var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => PreviewRentActivation.RunAsync(context,
                new PreviewMonthlyRentTarget(builder.Database!, builder.Port, daily.TenantId, FacilityCode.NPM), connection, daily.UserId,
                PhilippineTime.Today.Year, PhilippineTime.Today.Month, "PREVIEW-NPM-01"));
            Assert.Contains("collected daily", refused.Message);
        }

        await db.ResetAsync();
        var monthly = await SeedEcfAsync(canonical: true, BillingArchetype.MonthlyRental);
        await using var scoped = db.CreateContext(monthly.TenantId);
        var classification = EEMOCantilanSDS.Domain.Entities.Revenue.RevenueClassification.Create(EEMOCantilanSDS.Domain.Constants.RevenueClassificationCodes.PermanentStallRent, monthly.TenantId);
        scoped.AddRange(classification, EEMOCantilanSDS.Domain.Entities.Revenue.RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1),
            "Permanent Stall Rent", RevenueInstrumentType.OfficialReceipt, monthly.TenantId));
        await scoped.SaveChangesAsync();
        var outcome = await PreviewRentActivation.RunAsync(scoped,
            new PreviewMonthlyRentTarget(builder.Database!, builder.Port, monthly.TenantId, FacilityCode.NPM), connection, monthly.UserId,
            PhilippineTime.Today.Year, PhilippineTime.Today.Month, "PREVIEW-NPM-01");
        Assert.Equal(SettlementAuthority.Canonical, outcome.Authority);
    }
}
