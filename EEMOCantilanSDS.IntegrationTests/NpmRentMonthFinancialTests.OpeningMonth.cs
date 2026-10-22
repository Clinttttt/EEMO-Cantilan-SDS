using System.Security.Cryptography;
using System.Text;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Payments;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class NpmRentMonthFinancialTests
{
    private sealed class OpeningTenantCode : ITenantContext { public string TenantCode => "synthetic-opening"; }
    private sealed class OpeningCache : IEemoCacheInvalidator
    {
        public Task InvalidateRegionAsync(string region, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePeriodAsync(string tenantCode, int year, int month, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateFacilityPeriodAsync(string tenantCode, FacilityCode facility, int year, int month, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidatePaymentAffectedViewsAsync(string tenantCode, FacilityCode? facility, int year, int month, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateReferenceDataAsync(string tenantCode, CancellationToken ct = default) => Task.CompletedTask;
        public Task InvalidateTenantAsync(string tenantCode, CancellationToken ct = default) => Task.CompletedTask;
    }
    [Fact]
    public void Opening_assessment_endpoints_are_Head_only_and_do_not_accept_money_or_tenant_claims()
    {
        foreach (var method in new[] { "ReviewNpmOpeningMonth", "PrepareNpmOpeningMonth", "ActivateNpmOpeningMonth" })
        {
            var action = typeof(EEMOCantilanSDS.Api.Controllers.DailyCollectionsController).GetMethod(method)!;
            var guard = Assert.Single(action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>());
            Assert.Equal("SuperAdmin", guard.Roles);
        }
        Assert.Equal(new[] { "ContractId", "PeriodStart", "StallId" }, typeof(NpmOpeningMonthPreparationRequest)
            .GetProperties().Select(x => x.Name).Order().ToArray());
    }

    private sealed class OpeningClock(DateOnly day) : IClock
    {
        public DateOnly PhilippineToday => day;
        public DateTime PhilippineNow => day.ToDateTime(new TimeOnly(20, 0));
        public DateTime UtcNow => DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc);
    }

    private sealed record OpeningWorld(World World, Guid Contract, Guid Facility, NpmOpeningMonthPolicy Approval, DateOnly Start);

    private async Task<OpeningWorld> SeedOpening(DateOnly start, decimal rent = 900, decimal daily = 30,
        bool monthlyAuthority = true, NpmMonthBasis basis = NpmMonthBasis.RentGoal)
    {
        var municipality = Municipality.Create("OP-" + Guid.NewGuid().ToString("N")[..8], "Synthetic opening", "Fixture",
            MunicipalityStatus.Active, tenantCode: "op-" + Guid.NewGuid().ToString("N"));
        await using (var setup = fixture.Create(Guid.Empty)) { setup.Add(municipality); await setup.SaveChangesAsync(); }
        await using var db = fixture.Create(municipality.Id);
        var facility = Facility.Create(FacilityCode.NPM, "Synthetic opening market", "NPM", municipalityId: municipality.Id);
        if (basis != NpmMonthBasis.RentGoal) facility.SetMonthBasis(basis);
        // Deliberately disagree with the approved source: form fields must not become financial authority.
        var stall = Stall.Create(facility.Id, "OPEN-3", 4321, ApplicableFees.DailyRental,
            section: MarketSection.FishSection, municipalityId: municipality.Id);
        var contract = Contract.Create(stall.Id, "Synthetic Morloc", "Fixture contract", start, 3, 4567);
        db.AddRange(facility, stall, contract);
        db.Entry(contract).Property(x => x.CreatedAt).CurrentValue = DateTime.SpecifyKind(start.ToDateTime(new TimeOnly(1, 0)), DateTimeKind.Utc);
        db.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, daily, new(2020, 1, 1), municipality.Id, "Synthetic approved tariff"));
        if (monthlyAuthority) db.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, rent, new(2020, 1, 1), municipality.Id, "Synthetic approved tariff"));
        var classification = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, municipality.Id);
        db.RevenueClassifications.Add(classification);
        db.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(classification.Id, new(2020, 1, 1), "Rent", RevenueInstrumentType.OfficialReceipt, municipality.Id));
        await db.SaveChangesAsync();
        var world = new World(municipality.Id, Guid.NewGuid(), stall.Id, Guid.Empty, daily);
        var approval = new NpmOpeningMonthPolicy(municipality.Id, facility.Id, new(2026, 1, 1), world.Actor,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "SYNTHETIC-ONLY opening month approval", true);
        return new(world, contract.Id, facility.Id, approval, start);
    }

    private NpmRentMonthWorkflow OpeningWorkflow(AppDbContext db, OpeningWorld w, DateOnly? day = null,
        bool approved = true, bool enabled = true, Guid? collectorId = null)
    {
        var clock = new OpeningClock(day ?? (w.Start > new DateOnly(2026, 10, 20) ? w.Start : new(2026, 10, 20)));
        var rates = new FeeRateResolver(db);
        var closures = new NpmMarketClosureRepository(db);
        return new(db, new Tenant(w.World.Tenant), new Actor(w.World, collectorId), new CollectorRepository(db), rates, clock,
            new(enabled, true, true, new HashSet<Guid> { w.World.Tenant }), new(true), closures,
            new NpmMonthSettlementService(new DailyCollectionRepository(db), closures, rates, clock),
            openingPolicies: new(approved ? [w.Approval] : []));
    }

    private async Task<NpmRentMonth> PrepareAndActivateOpening(AppDbContext db, OpeningWorld w, NpmRentMonthWorkflow workflow)
    {
        var period = new DateOnly(w.Start.Year, w.Start.Month, 1);
        var preview = await workflow.ReviewOpeningMonthAsync(w.World.Stall, w.Contract, period);
        Assert.True(preview.IsSuccess, preview.Error); Assert.Null(preview.Value!.MonthId);
        Assert.Equal(preview.Value.Authority.ApprovedMonthlyAmount, preview.Value.GrossObligation);
        Assert.Empty(await db.NpmRentMonths.ToListAsync()); Assert.Empty(await db.Collections.ToListAsync());
        var prepared = await workflow.PrepareOpeningMonthAsync(w.World.Stall, w.Contract, period);
        Assert.True(prepared.IsSuccess, prepared.Error);
        var empty = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("[]")));
        var activated = await workflow.ActivateOpeningMonthAsync(new(w.World.Stall, w.Contract, period, 1, "Synthetic source review only"));
        Assert.True(activated.IsSuccess, activated.Error);
        var reviewed = await workflow.ReviewOpeningMonthAsync(w.World.Stall, w.Contract, period);
        Assert.True(reviewed.IsSuccess, reviewed.Error); Assert.Equal(prepared.Value, reviewed.Value!.MonthId);
        Assert.Equal(preview.Value.GrossObligation, reviewed.Value.GrossObligation);
        return await db.NpmRentMonths.SingleAsync(x => x.Id == prepared.Value);
    }

    [SkippableTheory]
    [InlineData(2026, 10, 1)] [InlineData(2026, 10, 20)] [InlineData(2026, 10, 31)]
    [InlineData(2026, 2, 20)] [InlineData(2028, 2, 20)] [InlineData(2026, 4, 20)]
    public async Task Prospective_opening_uses_full_approved_month_without_repricing_earned_days(int year, int month, int day)
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync();
        var w = await SeedOpening(new(year, month, day));
        await using var db = fixture.Create(w.World.Tenant);
        var date = w.Start;
        var workflow = OpeningWorkflow(db, w, date);
        var source = await PrepareAndActivateOpening(db, w, workflow);
        Assert.Equal(900, source.AssessedAmount);
        Assert.Contains("4321", source.RateEvidenceJson); Assert.Contains("4567", source.RateEvidenceJson);
        var whole = await workflow.QuoteAsync(w.World.Stall, source.PeriodStart, date, NpmRentPaymentScope.WholeMonth);
        Assert.True(whole.IsSuccess, whole.Error); Assert.Equal(900, whole.Value!.EligibleAmount);
        var report = await workflow.ReadReportPositionAsync(w.World.Stall, source.PeriodStart, date);
        Assert.Equal(900, report!.Position.Assessment); Assert.Equal(30, report.EarnedLiability);
        Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.DailyCollections.ToListAsync());
        Assert.Empty(await db.PostingOperations.ToListAsync()); Assert.Empty(await db.CollectionRemittances.ToListAsync());
    }

    [SkippableTheory]
    [InlineData(900, 30, 3, 810)] [InlineData(900, 35, 25, 25)] [InlineData(1000, 35, 28, 20)]
    public async Task Prospective_daily_and_whole_share_exact_source_and_real_SRCs(decimal rent, decimal daily, int count, decimal remainder)
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var start = new DateOnly(2026, 10, count == 3 ? 20 : 1);
        var w = await SeedOpening(start, rent, daily);
        await using var db = fixture.Create(w.World.Tenant);
        var date = start.AddDays(count - 1);
        var workflow = OpeningWorkflow(db, w, date);
        var source = await PrepareAndActivateOpening(db, w, workflow);
        for (var i = 0; i < count; i++)
        {
            var quote = await workflow.QuoteAsync(w.World.Stall, source.PeriodStart, date, NpmRentPaymentScope.DailyInstallment, installmentDate: start.AddDays(i));
            Assert.True(quote.IsSuccess, quote.Error); Assert.Equal(daily, quote.Value!.EligibleAmount);
            var request = new NpmRentMonthPayment(Guid.NewGuid(), w.World.Stall, source.PeriodStart, date,
                NpmRentPaymentScope.DailyInstallment, daily, quote.Value.Token, quote.Value.InstallmentDate);
            Assert.True((await workflow.PostAsync(request)).IsSuccess);
            Assert.True((await workflow.PostAsync(request)).Value!.Existing);
        }
        var whole = (await workflow.QuoteAsync(w.World.Stall, source.PeriodStart, date, NpmRentPaymentScope.WholeMonth)).Value!;
        Assert.Equal(remainder, whole.Outstanding); Assert.Equal(remainder, whole.EligibleAmount);
        Assert.True((await workflow.PostAsync(new(Guid.NewGuid(), w.World.Stall, source.PeriodStart, date,
            NpmRentPaymentScope.WholeMonth, remainder, whole.Token))).IsSuccess);
        Assert.Equal(rent, await db.CollectionAllocations.SumAsync(x => x.Amount));
        Assert.Equal(count + 1, await db.Collections.CountAsync());
        Assert.Equal(count + 1, await db.Collections.Select(x => x.ReferenceCode).Distinct().CountAsync());
        Assert.Empty(await db.DailyCollections.ToListAsync());
        Assert.Equal(0, (await workflow.QuoteAsync(w.World.Stall, source.PeriodStart, date, NpmRentPaymentScope.WholeMonth)).Value!.Outstanding);
    }

    [SkippableTheory]
    [InlineData(false, true, true, NpmMonthBasis.RentGoal)]
    [InlineData(true, false, true, NpmMonthBasis.RentGoal)]
    [InlineData(true, true, false, NpmMonthBasis.RentGoal)]
    [InlineData(true, true, true, NpmMonthBasis.PureDays)]
    public async Task Prospective_missing_authority_or_activation_cannot_assess(bool monthly, bool approved, bool enabled, NpmMonthBasis basis)
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await SeedOpening(new(2026, 10, 20), monthlyAuthority: monthly, basis: basis);
        await using var db = fixture.Create(w.World.Tenant); var workflow = OpeningWorkflow(db, w, approved: approved, enabled: enabled);
        Assert.False((await workflow.PrepareOpeningMonthAsync(w.World.Stall, w.Contract, new(2026, 10, 1))).IsSuccess);
        Assert.Empty(await db.NpmRentMonths.ToListAsync()); Assert.Empty(await db.Collections.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [SkippableFact]
    public async Task Prospective_partial_stale_quote_and_day31_use_one_obligation()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await SeedOpening(new(2026, 10, 20));
        await using var db = fixture.Create(w.World.Tenant); var date = new DateOnly(2026, 10, 25);
        var workflow = OpeningWorkflow(db, w, date); var source = await PrepareAndActivateOpening(db, w, workflow);
        var stale = (await workflow.QuoteAsync(w.World.Stall, source.PeriodStart, date, NpmRentPaymentScope.WholeMonth)).Value!;
        var daily = (await workflow.QuoteAsync(w.World.Stall, source.PeriodStart, date, NpmRentPaymentScope.DailyInstallment)).Value!;
        Assert.True((await workflow.PostAsync(new(Guid.NewGuid(), w.World.Stall, source.PeriodStart, date,
            daily.Scope, 30, daily.Token, daily.InstallmentDate))).IsSuccess);
        var partial = await workflow.QuoteAsync(w.World.Stall, source.PeriodStart, date, NpmRentPaymentScope.PartPayment);
        Assert.True(partial.IsSuccess, partial.Error); Assert.True(partial.Value!.EligibleAmount >= 100);
        Assert.True((await workflow.PostAsync(new(Guid.NewGuid(), w.World.Stall, source.PeriodStart, date,
            NpmRentPaymentScope.PartPayment, 100, partial.Value.Token))).IsSuccess);
        Assert.False((await workflow.PostAsync(new(Guid.NewGuid(), w.World.Stall, source.PeriodStart, date,
            stale.Scope, 900, stale.Token))).IsSuccess);
        var whole = (await workflow.QuoteAsync(w.World.Stall, source.PeriodStart, date, stale.Scope)).Value!;
        Assert.Equal(770, whole.EligibleAmount);
        Assert.True((await workflow.PostAsync(new(Guid.NewGuid(), w.World.Stall, source.PeriodStart, date,
            whole.Scope, 770, whole.Token))).IsSuccess);
        var day31 = await OpeningWorkflow(db, w, new(2026, 10, 31)).QuoteAsync(w.World.Stall, source.PeriodStart,
            new(2026, 10, 31), NpmRentPaymentScope.DailyInstallment);
        Assert.True(day31.IsSuccess, day31.Error); Assert.Equal(0, day31.Value!.EligibleAmount);
        Assert.Equal(900, await db.Collections.SumAsync(x => x.TotalAmount)); Assert.Equal(3, await db.Collections.CountAsync());
        Assert.Empty(await db.DailyCollections.ToListAsync());
    }

    [SkippableFact]
    public async Task Prospective_frozen_rate_survives_later_tariff_changes_and_cannot_be_reassessed()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await SeedOpening(new(2026, 10, 20));
        await using var db = fixture.Create(w.World.Tenant); var workflow = OpeningWorkflow(db, w);
        var source = await PrepareAndActivateOpening(db, w, workflow);
        db.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMonthlyStall, 1200, new(2026, 10, 21), w.World.Tenant, "Later synthetic approval"));
        db.FacilityRates.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 40, new(2026, 10, 21), w.World.Tenant, "Later synthetic approval"));
        await db.SaveChangesAsync();
        var later = OpeningWorkflow(db, w, new(2026, 10, 25));
        Assert.Equal(900, (await later.QuoteAsync(w.World.Stall, source.PeriodStart, new(2026, 10, 25), NpmRentPaymentScope.WholeMonth)).Value!.Assessment);
        Assert.Equal(30, (await later.QuoteAsync(w.World.Stall, source.PeriodStart, new(2026, 10, 25), NpmRentPaymentScope.DailyInstallment)).Value!.EligibleAmount);
        Assert.Equal(180, (await later.ReadReportPositionAsync(w.World.Stall, source.PeriodStart, new(2026, 10, 25)))!.EarnedLiability);
        var old = (await db.FacilityRates.Where(x => x.RateKey == FeeRateKey.NpmMonthlyStall).OrderBy(x => x.EffectiveDate).FirstAsync());
        old.UpdateAmount(1500, "Later edit"); await db.SaveChangesAsync();
        Assert.Equal(900, (await later.QuoteAsync(w.World.Stall, source.PeriodStart, new(2026, 10, 25), NpmRentPaymentScope.WholeMonth)).Value!.Assessment);
        var roster = await new StallRepository(db, new FeeRateResolver(db), new OpeningClock(new(2026, 10, 25)), npmRentMonths: later)
            .GetStallHoldersListAsync(FacilityCode.NPM, null, null, 2026, default);
        var row = Assert.Single(roster.Sections.SelectMany(x => x.Rows));
        Assert.Equal(900, row.MonthlyRentalRate); Assert.Equal(30, row.DailyRate);
        Assert.Equal(10800, row.WholeYearRental); // Annualized rate, not twelve posted assessments or receipts.
        var report = await new FacilityReportsRepository(db, new FeeRateResolver(db), new OpeningClock(new(2026, 10, 25)), later)
            .GetFacilityReportsAsync(FacilityCode.NPM, ReportPeriod.Monthly, 2026, 10, null, default);
        Assert.Equal(900, Assert.Single(report.StallCompliance).FullPeriodRentObligation);
        Assert.Equal(0, report.TotalRevenue);
        Assert.False((await workflow.PrepareOpeningMonthAsync(w.World.Stall, w.Contract, source.PeriodStart)).IsSuccess);
        Assert.Equal(900, (await db.NpmRentMonths.AsNoTracking().SingleAsync()).AssessedAmount);
    }

    [SkippableTheory]
    [InlineData("existing-day")] [InlineData("handover")] [InlineData("future-policy")]
    [InlineData("late-approval")] [InlineData("wrong-tenant")]
    public async Task Prospective_unsafe_ownership_and_history_fail_closed(string conflict)
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await SeedOpening(new(2026, 10, 20));
        await using var db = fixture.Create(w.World.Tenant);
        if (conflict == "existing-day") db.DailyCollections.Add(DailyCollection.Create(w.World.Stall, w.Start, "Historical evidence", 30));
        if (conflict == "handover") db.Contracts.Add(Contract.Create(w.World.Stall, "Previous holder", "Historical contract", new(2026, 1, 1), 1, 900));
        await db.SaveChangesAsync();
        var approval = conflict switch
        {
            "future-policy" => w.Approval with { EffectiveFrom = new(2026, 11, 1) },
            "late-approval" => w.Approval with { ApprovedAtUtc = new(2026, 10, 20, 2, 0, 0, DateTimeKind.Utc) },
            "wrong-tenant" => w.Approval with { MunicipalityId = Guid.NewGuid() },
            _ => w.Approval
        };
        var workflow = OpeningWorkflow(db, w with { Approval = approval });
        var result = await workflow.PrepareOpeningMonthAsync(w.World.Stall, w.Contract, new(2026, 10, 1));
        Assert.False(result.IsSuccess); Assert.Contains("OFFICE_REVIEW_REQUIRED", result.Error);
        Assert.Empty(await db.NpmRentMonths.ToListAsync()); Assert.Empty(await db.Collections.ToListAsync());
        Assert.Equal(conflict == "existing-day" ? 1 : 0, await db.DailyCollections.CountAsync());
    }

    [SkippableFact]
    public async Task Prospective_concurrent_preparation_and_final_payments_are_once_only()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await SeedOpening(new(2026, 10, 20)); var period = new DateOnly(2026, 10, 1);
        await using var a = fixture.Create(w.World.Tenant); await using var b = fixture.Create(w.World.Tenant);
        var preparations = await Task.WhenAll(OpeningWorkflow(a, w).PrepareOpeningMonthAsync(w.World.Stall, w.Contract, period),
            OpeningWorkflow(b, w).PrepareOpeningMonthAsync(w.World.Stall, w.Contract, period));
        Assert.Single(preparations, x => x.IsSuccess);
        await using var setup = fixture.Create(w.World.Tenant); var workflow = OpeningWorkflow(setup, w);
        var empty = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("[]")));
        Assert.True((await workflow.ReviewAndActivateAsync(w.World.Stall, period, 1, new(1, [], [], empty), "Synthetic concurrency review")).IsSuccess);
        var q = (await workflow.QuoteAsync(w.World.Stall, period, new(2026, 10, 20), NpmRentPaymentScope.WholeMonth)).Value!;
        var request = new NpmRentMonthPayment(Guid.NewGuid(), w.World.Stall, period, new(2026, 10, 20), q.Scope, 900, q.Token);
        var payments = await Task.WhenAll(OpeningWorkflow(a, w).PostAsync(request),
            OpeningWorkflow(b, w).PostAsync(request with { ClientOperationId = Guid.NewGuid() }));
        Assert.Single(payments, x => x.IsSuccess);
        await using var verify = fixture.Create(w.World.Tenant);
        Assert.Single(await verify.NpmRentMonths.ToListAsync()); Assert.Single(await verify.Collections.ToListAsync());
        Assert.Equal(900, await verify.CollectionAllocations.SumAsync(x => x.Amount));
    }

    [SkippableFact]
    public async Task Prospective_rate_change_before_review_requires_reconciliation()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await SeedOpening(new(2026, 10, 20)); var period = new DateOnly(2026, 10, 1);
        await using (var prepare = fixture.Create(w.World.Tenant))
            Assert.True((await OpeningWorkflow(prepare, w).PrepareOpeningMonthAsync(w.World.Stall, w.Contract, period)).IsSuccess);
        await using var db = fixture.Create(w.World.Tenant);
        (await db.FacilityRates.SingleAsync(x => x.RateKey == FeeRateKey.NpmMonthlyStall)).UpdateAmount(1200, "New synthetic decision");
        await db.SaveChangesAsync();
        var empty = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("[]")));
        var result = await OpeningWorkflow(db, w).ReviewAndActivateAsync(w.World.Stall, period, 1, new(1, [], [], empty), "Synthetic review");
        Assert.False(result.IsSuccess);
        var source = await db.NpmRentMonths.AsNoTracking().SingleAsync();
        Assert.Equal(900, source.AssessedAmount); Assert.False(source.CanAcceptCollections);
        Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.PostingOperations.ToListAsync());
    }

    [SkippableFact]
    public async Task Prospective_absence_credit_preserves_gross_assessment_and_original_cash()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await SeedOpening(new(2026, 10, 20));
        await using var db = fixture.Create(w.World.Tenant); var workflow = OpeningWorkflow(db, w, new(2026, 10, 31));
        var source = await PrepareAndActivateOpening(db, w, workflow);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            for (var day = w.Start; day <= new DateOnly(2026, 10, 31); day = day.AddDays(1))
            {
                var absent = DailyCollection.Create(w.World.Stall, day, "Synthetic Head", 30);
                absent.MarkAbsent("Synthetic approved absence"); db.DailyCollections.Add(absent);
            }
            await db.SaveChangesAsync();
            await workflow.SynchronizeOperatingCreditsAsync(source.PeriodStart, "Synthetic complete approved absence");
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        var quote = await workflow.QuoteAsync(w.World.Stall, source.PeriodStart, new(2026, 10, 31), NpmRentPaymentScope.WholeMonth);
        Assert.True(quote.IsSuccess, quote.Error); Assert.Equal(900, quote.Value!.Assessment);
        Assert.Equal(900, quote.Value.Credits); Assert.Equal(0, quote.Value.Outstanding);
        Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.CollectionAllocations.ToListAsync());
    }

    [SkippableFact]
    public async Task Prospective_Head_Mobile_native_discovery_and_offline_replay_reconcile_real_postgres_cash()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var date = new DateOnly(2026, 10, 20); var w = await SeedOpening(date);
        await using var db = fixture.Create(w.World.Tenant);
        var headMonth = OpeningWorkflow(db, w); var source = await PrepareAndActivateOpening(db, w, headMonth);
        var collector = EEMOCantilanSDS.Domain.Entities.Users.CollectorUser.Create("Synthetic collector", "OPEN-TEST", "opening-collector", null, null,
            new EEMOCantilanSDS.Domain.Common.HashedPassword("fixture-only"), w.World.Tenant);
        collector.FacilityAssignments.Add(EEMOCantilanSDS.Domain.Entities.Users.CollectorFacilityAssignment.Create(collector.Id, w.Facility, FacilityCode.NPM));
        var service = GovernedService.Create(w.World.Tenant, CollectorOperationCodes.NpmDaily, "Synthetic Head");
        db.AddRange(collector, service, GovernedServiceSetting.Create(w.World.Tenant, service.Id, date,
            GovernedServiceBasis.DirectApprovedAmount, null, null, true, true, "Synthetic Head"));
        await db.SaveChangesAsync();
        var mobileMonth = OpeningWorkflow(db, w, collectorId: collector.Id);
        var actor = new Actor(w.World, collector.Id); var clock = new OpeningClock(date); var tenant = new Tenant(w.World.Tenant);
        var daily = (await mobileMonth.QuoteAsync(w.World.Stall, source.PeriodStart, date, NpmRentPaymentScope.DailyInstallment)).Value!;
        // Legacy dispatcher/repository must never be reached by a reviewed activated-month replay.
        var sync = new SyncOfflineCollectionsCommandHandler(null!, null!, actor, monthly: mobileMonth);
        var intent = new SyncOfflineOperationDto(Guid.NewGuid(), OfflineOperationKind.NpmDaily, date,
            StallId: w.World.Stall, ReceivedAmount: 30, NpmQuoteToken: daily.Token, NpmInstallmentDate: date);
        var posted = Assert.Single((await sync.Handle(new([intent]), default)).Value!.Results);
        var replay = Assert.Single((await sync.Handle(new([intent]), default)).Value!.Results);
        Assert.Equal(SyncResultStatus.Synced, posted.Status); Assert.Equal(posted.CollectionId, replay.CollectionId);
        Assert.Equal(posted.ReferenceCode, replay.ReferenceCode);
        var rates = new FeeRateResolver(db); var closures = new NpmMarketClosureRepository(db);
        var legacy = new NpmMonthSettlementService(new DailyCollectionRepository(db), closures, rates, clock);
        var cache = new OpeningCache(); var code = new OpeningTenantCode();
        NpmWholePaymentWorkflow Whole(Actor caller, NpmRentMonthWorkflow month) => new(new StallRepository(db, rates, clock, npmRentMonths: month),
            new CollectorRepository(db), new DailyCollectionRepository(db), new NpmMonthSettlementBridge(legacy, month, clock),
            new NpmDailyCanonicalPoster(db, caller, tenant, new GovernedCanonicalAuthority(db, tenant), month), caller, clock, cache, code, month);
        var head = Whole(new Actor(w.World), headMonth); var mobile = Whole(actor, mobileMonth);
        var headQuote = await head.QuoteAsync(w.World.Stall, 2026, 10); var mobileQuote = await mobile.QuoteAsync(w.World.Stall, 2026, 10);
        Assert.True(headQuote.IsSuccess, headQuote.Error); Assert.True(mobileQuote.IsSuccess, mobileQuote.Error);
        Assert.Equal(900, headQuote.Value!.MonthlyObligation); Assert.Equal(870, headQuote.Value.Amount);
        Assert.Equal(headQuote.Value.Amount, mobileQuote.Value!.Amount); Assert.Equal(headQuote.Value.QuoteToken, mobileQuote.Value.QuoteToken);
        var native = new CollectionSessionSources(db, actor, tenant, clock, null!, null!, npmWhole: mobile, monthly: mobileMonth);
        var discovery = await native.DiscoverNativeAsync(new(SourceIdentityKind.Occupancy, w.Contract), date, default);
        var choice = Assert.Single(discovery.Operations.Single(x => x.Kind == CollectionSessionItemKind.NpmWholePayment).Choices!);
        Assert.Equal(870, choice.ServerAmount); Assert.Equal(w.Contract, choice.Identity.OccupancyId);
        Assert.True((await mobile.PostAsync(new(Guid.NewGuid(), w.World.Stall, 2026, 10, date, 870, mobileQuote.Value.QuoteToken))).IsSuccess);
        Assert.Equal(900, await db.Collections.SumAsync(x => x.TotalAmount));
        Assert.All(await db.Collections.ToListAsync(), x => Assert.Equal(collector.Id, x.CollectorId));
        Assert.Equal(2, await db.Collections.Select(x => x.ReferenceCode).Distinct().CountAsync());
        Assert.Empty(await db.DailyCollections.ToListAsync()); Assert.Empty(await db.CollectionRemittances.ToListAsync());
        Assert.Equal(0, (await headMonth.ReadPositionAsync(w.World.Stall, source.PeriodStart))!.Outstanding);
    }
}
