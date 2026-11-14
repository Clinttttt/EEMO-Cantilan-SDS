using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Application.Queries.Revenue.GetRevenueSourcePerformance;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>The office's canonical rent receipt and draft ask the billing rules. A collector's post is money already taken, so it does not.</summary>
public sealed partial class EcfCollectionWorkflowTests
{
    private static BillingRules BillingRulesFor(AppDbContext context, Guid tenant, Guid user) => new(
        new BillingPolicyWorkflow(context, new TestActor(user, tenant, "SuperAdmin"), new FixedTenant(tenant), new SystemClock()),
        new FacilityReportsRepository(context), context, new SystemClock());

    private async Task<(Seed Seed, RentSeed Rent, DateOnly Older)> ArrangeTwoOpenMonthsAsync()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var seed = await SeedEcfAsync(true);
        var older = seed.Period.AddMonths(-1);
        var rent = await SeedRentSourcesAsync(seed, [older, seed.Period], true, facilityCode: FacilityCode.NCC, monthlyRate: 1200, sourceOwned: true);
        await using var setup = db.CreateContext(seed.TenantId);
        setup.Add(BillingPolicyVersion.Create(seed.TenantId, PhilippineTime.Today, PhilippineTime.Today, BillingPolicy.StallTrackDefaults with { CountsMonthsFrom = older }, null, seed.UserId, DateTime.UtcNow));
        await setup.SaveChangesAsync();
        return (seed, rent, older);
    }

    [SkippableFact]
    public async Task Canonical_rent_from_a_prior_year_reports_on_Arrears_and_the_total_counts_it_once()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var seed = await SeedEcfAsync(true);
        var today = PhilippineTime.Today;
        var old = new DateOnly(today.Year - 1, 12, 1);
        var rent = await SeedRentSourcesAsync(seed, [old], true, facilityCode: FacilityCode.NCC, monthlyRate: 1200, sourceOwned: true);
        await using var context = db.CreateContext(seed.TenantId);
        var tenant = new FixedTenant(seed.TenantId);
        var actor = new TestActor(seed.UserId, seed.TenantId, "SuperAdmin");
        var report = new GetOfficialMonthlyIncomeQueryHandler(context, new LegacyMonthlyIncomeReader(context), actor, tenant, new ProofClock());
        var before = (await report.Handle(new(today.Year, today.Month), default)).Value!.GrandTotal.SystemAmount;
        var flow = new CollectionComposerWorkflow(context, actor, tenant, rentOptions: OfficeReceipts(seed.TenantId, FacilityCode.NCC));
        var quote = (await flow.GetRentObligationAsync(rent.StallId, old.Year, old.Month)).Value!;

        var posted = await flow.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, old.Year, old.Month, 1200m, quote.SourceVersion, today, rent.ContractId));

        Assert.True(posted.IsSuccess, posted.Error);
        var statement = (await report.Handle(new(today.Year, today.Month), default)).Value!;
        decimal Row(string key) => statement.Groups.SelectMany(g => g.Rows).Single(r => r.Key == key).Months[today.Month - 1].Canonical;
        Assert.Equal((1200m, 0m), (Row("ARREARS"), Row("RENT_NCC")));
        Assert.Equal(before + 1200m, statement.GrandTotal.SystemAmount);
        Assert.Equal(new RecoveredArrearsDto(FacilityCode.NCC, 1200m), Assert.Single(statement.RecoveredArrears!));
        // The receipt line keeps what it was: only where the report prints it moved.
        Assert.Equal(1200m, await context.CollectionLines.SumAsync(l => l.Amount));
        // The performance page reads the same row and counts the receipt where the money sits.
        var performance = (await new GetRevenueSourcePerformanceQueryHandler(report, context, tenant).Handle(new(today.Year, today.Month), default)).Value!;
        var arrears = performance.Rows.Single(r => r.Key == "ARREARS");
        Assert.Equal((1200m, (int?)1), (arrears.Collected, arrears.TransactionCount));
        Assert.NotEqual((int?)1, performance.Rows.Single(r => r.Key == "RENT_NCC").TransactionCount);
    }

    private static string MonthName(DateOnly month) => month.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

    [SkippableFact]
    public async Task The_offices_rent_receipt_takes_the_oldest_month_first()
    {
        var (seed, rent, older) = await ArrangeTwoOpenMonthsAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var flow = new CollectionComposerWorkflow(context, new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"), new FixedTenant(seed.TenantId),
            rentOptions: OfficeReceipts(seed.TenantId, FacilityCode.NCC), billingRules: BillingRulesFor(context, seed.TenantId, seed.UserId));
        var newerQuote = (await flow.GetRentObligationAsync(rent.StallId, seed.Period.Year, seed.Period.Month)).Value!;
        var olderQuote = (await flow.GetRentObligationAsync(rent.StallId, older.Year, older.Month)).Value!;

        var refused = await flow.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, seed.Period.Year, seed.Period.Month, 1200m, newerQuote.SourceVersion, PhilippineTime.Today, rent.ContractId));

        Assert.False(refused.IsSuccess);
        Assert.Contains(MonthName(older), refused.Error);
        Assert.Empty(await context.Collections.ToListAsync());

        var taken = await flow.PostOfficeRentAsync(new(Guid.NewGuid(), rent.StallId, older.Year, older.Month, 1200m, olderQuote.SourceVersion, PhilippineTime.Today, rent.ContractId));
        Assert.True(taken.IsSuccess, taken.Error);
    }

    [SkippableFact]
    public async Task A_draft_refuses_a_newer_month_until_the_older_one_is_on_the_same_receipt()
    {
        var (seed, rent, older) = await ArrangeTwoOpenMonthsAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var flow = new CollectionComposerWorkflow(context, new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"), new FixedTenant(seed.TenantId),
            billingRules: BillingRulesFor(context, seed.TenantId, seed.UserId));

        var refused = await flow.AddRentAllocationAsync(new AddRentDraftAllocationRequest(rent.StallId, seed.Period.Year, seed.Period.Month, 1200m));
        Assert.False(refused.IsSuccess);
        Assert.Contains(MonthName(older), refused.Error);

        var first = await flow.AddRentAllocationAsync(new AddRentDraftAllocationRequest(rent.StallId, older.Year, older.Month, 1200m));
        Assert.True(first.IsSuccess, first.Error);
        var second = await flow.AddRentAllocationAsync(new AddRentDraftAllocationRequest(rent.StallId, seed.Period.Year, seed.Period.Month, 1200m, first.Value!.Revision));
        Assert.True(second.IsSuccess, second.Error);
    }

    [SkippableFact]
    public async Task A_collectors_post_is_money_already_taken_and_is_not_refused()
    {
        var (seed, rent, _) = await ArrangeTwoOpenMonthsAsync();
        var collector = await SeedCollectorAsync(seed.TenantId, FacilityCode.NCC);
        await using var context = db.CreateContext(seed.TenantId);
        var flow = new CollectionComposerWorkflow(context, new CollectorActor(collector, seed.TenantId), new FixedTenant(seed.TenantId),
            billingRules: BillingRulesFor(context, seed.TenantId, seed.UserId));
        var quote = (await new CollectionComposerWorkflow(context, new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"), new FixedTenant(seed.TenantId))
            .GetRentObligationAsync(rent.StallId, seed.Period.Year, seed.Period.Month)).Value!;

        var posted = await flow.PostMobileRentAsync(new(Guid.NewGuid(), rent.StallId, seed.Period.Year, seed.Period.Month, 1200m, quote.SourceVersion, PhilippineTime.Today, rent.ContractId));

        Assert.True(posted.IsSuccess, posted.Error);
    }
}
