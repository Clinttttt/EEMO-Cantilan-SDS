using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Per-facility readiness for the monthly-rental family (NCC, TCC, BBQ, ICE). Each facility has its own rate, contract, report row and
/// classification; nothing here activates a facility. A facility posts only when its OWN options entry exists.
/// </summary>
public sealed partial class EcfCollectionWorkflowTests
{
    private static string RentRowKey(FacilityCode facility) => facility == FacilityCode.ICE ? "ICE_PLANT" : "RENT_" + facility;

    [SkippableTheory]
    [InlineData(FacilityCode.NCC, 1200)]
    [InlineData(FacilityCode.TCC, 2400)]
    [InlineData(FacilityCode.BBQ, 900)]
    [InlineData(FacilityCode.ICE, 1500)]
    public async Task Monthly_rent_Head_reversal_reopens_only_that_facilitys_source_and_report_row(FacilityCode facility, decimal rate)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync(); var seed = await SeedEcfAsync(canonical: true); var period = seed.Period;
        var rent = await SeedRentSourcesAsync(seed, [period], true, facilityCode: facility, monthlyRate: rate, sourceOwned: true);
        var half = rate / 2;
        var actor = new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"); var tenant = new FixedTenant(seed.TenantId);
        await using var context = db.CreateContext(seed.TenantId);
        var flow = new CollectionComposerWorkflow(context, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, facility));

        var quote = (await flow.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
        Assert.Equal((rate, rate, SettlementAuthority.Canonical, RevenueInstrumentType.OfficialReceipt), (quote.AssessedRentalAmount, quote.ContractRate, quote.SettlementAuthority, quote.Instrument));
        var classification = await context.RevenueClassifications.SingleAsync(x => x.Id == quote.RevenueClassificationId);
        Assert.Equal(RevenueClassificationCodes.ForMonthlyRental(facility), classification.SemanticCode);   // ICE resolves ICE_PLANT, not Permanent Stall Rent
        var first = await flow.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, half, quote.SourceVersion, PhilippineTime.Today, rent.ContractId));
        Assert.True(first.IsSuccess, first.Error);
        var rest = (await flow.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!; Assert.Equal(half, rest.OutstandingAmount);
        var second = await flow.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, half, rest.SourceVersion, PhilippineTime.Today, rent.ContractId));
        Assert.True(second.IsSuccess, second.Error); Assert.NotEqual(first.Value!.ReferenceCode, second.Value!.ReferenceCode);

        var head = new HeadCollectionCorrectionWorkflow(context, new CollectionSessionStore(context), actor, tenant, new MutableTestClock(PhilippineTime.Today));
        var reversed = await head.ReverseAsync(new(Guid.NewGuid(), second.Value.CollectionId, "Synthetic reversal"));
        Assert.True(reversed.IsSuccess, reversed.Error);

        await using var read = db.CreateContext(seed.TenantId);
        var record = await read.PaymentRecords.AsNoTracking().SingleAsync(x => x.Id == rent.PaymentRecordIds[period]);
        Assert.Equal((PaymentStatus.Partial, half), (record.Status, record.PartialAmount));
        var reopened = (await new CollectionComposerWorkflow(read, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, facility))
            .GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
        Assert.Equal(half, reopened.OutstandingAmount);
        Assert.Equal(2, await read.Collections.CountAsync()); Assert.Equal(rate, await read.Collections.SumAsync(x => x.TotalAmount));   // originals kept
        var cells = (await new GetOfficialMonthlyIncomeQueryHandler(read, new LegacyMonthlyIncomeReader(read), actor, tenant, new ProofClock())
            .Handle(new(period.Year, period.Month), default)).Value!.Groups.SelectMany(g => g.Rows)
            .ToDictionary(r => r.Key, r => r.Months[period.Month - 1].Canonical);
        Assert.Equal(half, cells[RentRowKey(facility)]);                                             // this facility: received - reversed
        var otherRentalRows = cells.Where(x => x.Key != RentRowKey(facility) && (x.Key.StartsWith("RENT_") || x.Key == "ICE_PLANT"));
        Assert.All(otherRentalRows, x => Assert.Equal(0m, x.Value));                                   // no other rental row moved
    }

    [SkippableTheory]
    [InlineData(FacilityCode.TCC)]
    [InlineData(FacilityCode.BBQ)]
    [InlineData(FacilityCode.ICE)]
    public async Task Enabling_NCC_does_not_enable_another_facility_but_enabling_it_explicitly_does(FacilityCode other)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync(); var seed = await SeedEcfAsync(canonical: true); var period = seed.Period;
        var rent = await SeedRentSourcesAsync(seed, [period], true, facilityCode: other, monthlyRate: 1000, sourceOwned: true);
        var actor = new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"); var tenant = new FixedTenant(seed.TenantId);
        await using var context = db.CreateContext(seed.TenantId);

        var ncc = new CollectionComposerWorkflow(context, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, FacilityCode.NCC));
        var blocked = (await ncc.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
        Assert.True(blocked.CanPostCanonical); Assert.False(blocked.CanPostOfficeReceipt);
        var refused = await ncc.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, 500, blocked.SourceVersion, PhilippineTime.Today, rent.ContractId));
        Assert.False(refused.IsSuccess); Assert.Contains("remain disabled", refused.Error);
        Assert.Empty(await context.Collections.ToArrayAsync());

        var otherTenant = new CollectionComposerWorkflow(context, actor, tenant, rentOptions: OfficeReceipts(Guid.NewGuid(), other));
        Assert.False((await otherTenant.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!.CanPostOfficeReceipt);   // wrong tenant

        var enabled = new CollectionComposerWorkflow(context, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, other));
        var quote = (await enabled.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!; Assert.True(quote.CanPostOfficeReceipt);
        var wrongContract = await enabled.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, 500, quote.SourceVersion, PhilippineTime.Today, Guid.NewGuid()));
        Assert.False(wrongContract.IsSuccess); Assert.Empty(await context.Collections.ToArrayAsync());
        var posted = await enabled.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, 500, quote.SourceVersion, PhilippineTime.Today, rent.ContractId));
        Assert.True(posted.IsSuccess, posted.Error);
        var over = await enabled.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, 600, (await enabled.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!.SourceVersion, PhilippineTime.Today, rent.ContractId));
        Assert.False(over.IsSuccess);                                                                  // 600 > 500 outstanding
        Assert.Single(await context.Collections.ToArrayAsync());
    }

    [SkippableTheory]
    [InlineData(FacilityCode.NCC)]
    [InlineData(FacilityCode.TCC)]
    [InlineData(FacilityCode.BBQ)]
    [InlineData(FacilityCode.ICE)]
    public async Task Monthly_rent_legacy_partial_and_paid_months_refuse_receipts_and_are_blocked_from_cutover(FacilityCode facility)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync(); var seed = await SeedEcfAsync(canonical: true); var period = seed.Period;
        var rent = await SeedRentSourcesAsync(seed, [period], false, partialAmount: 600, facilityCode: facility, monthlyRate: 1200);
        var actor = new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"); var tenant = new FixedTenant(seed.TenantId);
        await using var context = db.CreateContext(seed.TenantId);
        var flow = new CollectionComposerWorkflow(context, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, facility));

        var quote = (await flow.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
        Assert.False(quote.CanPostCanonical); Assert.False(quote.CanPostOfficeReceipt); Assert.Equal(SettlementAuthority.Legacy, quote.SettlementAuthority);
        var refused = await flow.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, 600, quote.SourceVersion, PhilippineTime.Today, rent.ContractId));
        Assert.False(refused.IsSuccess);
        Assert.Empty(await context.Collections.ToArrayAsync());

        var record = await context.PaymentRecords.SingleAsync();
        record.MarkSettlementPendingCutover(); await context.SaveChangesAsync();
        var readiness = (await new SettlementCutoverWorkflow(context, actor, tenant).EvaluateReadinessAsync(new(new(CollectionSourceKind.PaymentRecord, record.Id, null)))).Value!;
        Assert.False(readiness.Ready); Assert.Contains(readiness.BlockingReasons, x => x.Contains("dated receipt/report evidence"));
        Assert.Equal(600, (await context.PaymentRecords.SingleAsync()).PartialAmount);                // legacy cash untouched
    }

    [SkippableTheory]
    [InlineData(FacilityCode.TCC)]
    [InlineData(FacilityCode.BBQ)]
    [InlineData(FacilityCode.ICE)]
    public async Task Monthly_rent_concurrent_final_receipts_settle_exactly_once(FacilityCode facility)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync(); var seed = await SeedEcfAsync(canonical: true); var period = seed.Period;
        var rent = await SeedRentSourcesAsync(seed, [period], true, facilityCode: facility, monthlyRate: 1000, sourceOwned: true);
        var actor = new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"); var tenant = new FixedTenant(seed.TenantId);
        await using (var setup = db.CreateContext(seed.TenantId))
        {
            var start = new CollectionComposerWorkflow(setup, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, facility));
            var opening = (await start.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
            Assert.True((await start.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, 800, opening.SourceVersion, PhilippineTime.Today, rent.ContractId))).IsSuccess);
        }
        await using var left = db.CreateContext(seed.TenantId); await using var right = db.CreateContext(seed.TenantId);
        var one = new CollectionComposerWorkflow(left, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, facility));
        var two = new CollectionComposerWorkflow(right, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, facility));
        var a = (await one.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
        var b = (await two.GetRentObligationAsync(rent.StallId, period.Year, period.Month)).Value!;
        Assert.Equal(200, a.OutstandingAmount);
        var results = await Task.WhenAll(
            one.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, 200, a.SourceVersion, PhilippineTime.Today, rent.ContractId)),
            two.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, period.Year, period.Month, 200, b.SourceVersion, PhilippineTime.Today, rent.ContractId)));
        Assert.Single(results, x => x.IsSuccess);
        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Equal(2, await verify.Collections.CountAsync());                                       // the 800 receipt plus exactly one final 200
        var record = await verify.PaymentRecords.SingleAsync();
        Assert.Equal((PaymentStatus.Paid, 0m), (record.Status, record.BalanceDue));
    }
}
