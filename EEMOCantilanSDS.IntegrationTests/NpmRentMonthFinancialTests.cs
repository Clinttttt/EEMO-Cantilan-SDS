using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Configuration;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>Own disposable container only. Never accepts a connection string/environment override.</summary>
public sealed class NpmMonthFinancialFixture : IAsyncLifetime
{
    private readonly PostgresFixture database = new();
    public bool Available => database.Available;
    public string? UnavailableReason => database.UnavailableReason;
    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        if (!Available) return;
        await using var preview = Create(Guid.Empty);
        // Migration metadata belongs to AppDbContext, not the candidate model's derived context.
        await using var foundation = database.CreateContext(Guid.Empty);
        var registry = foundation.GetService<IMigrationsAssembly>();
        var baseline = preview.GetService<IModelRuntimeInitializer>().Initialize(registry.ModelSnapshot!.Model, true);
        var target = preview.GetService<IDesignTimeModel>().Model;
        var operations = preview.GetService<IMigrationsModelDiffer>().GetDifferences(baseline.GetRelationalModel(), target.GetRelationalModel());
        // The promoted migration must already provide exactly the reviewed mapping, without a fixture-only patch.
        Assert.Empty(operations);
        var financial = registry.CreateMigration(registry.Migrations["20261020050758_AddNpmRentMonthFinancialSource20261020"], preview.Database.ProviderName!);
        Assert.Equal(4, financial.UpOperations.OfType<AddColumnOperation>().Count());
        Assert.All(financial.UpOperations.OfType<AddColumnOperation>(), x => Assert.True(x.IsNullable));
        Assert.Equal(new[] { "MunicipalityId", "NpmRentMonthId" }, financial.UpOperations.OfType<AddForeignKeyOperation>().Single().Columns);
        var commands = preview.GetService<IMigrationsSqlGenerator>().Generate(operations, target);
        await using var transaction = await preview.Database.BeginTransactionAsync();
        foreach (var command in commands)
        {
            Assert.False(command.TransactionSuppressed);
            await preview.Database.ExecuteSqlRawAsync(command.CommandText);
        }
        await transaction.CommitAsync();
    }
    public AppDbContext Create(Guid tenant, IInterceptor? observer = null)
    {
        var connection = new NpgsqlConnectionStringBuilder(database.ApiConnectionString);
        Assert.Equal("stalltrack_tests", connection.Database);
        Assert.NotEqual(5432, connection.Port); // Refuse the owner's retained local database, including accidental reuse.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString,
                provider => provider.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .AddInterceptors(new MunicipalityStampInterceptor());
        if (observer is not null) options.AddInterceptors(observer);
        return new PreviewContext(options.Options, new Tenant(tenant));
    }
    public Task ResetAsync() => database.ResetAsync(); // Private container connection; never the municipal database.
    public Task DisposeAsync() => database.DisposeAsync();
    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor { public Guid MunicipalityId => id; public void Set(Guid id) => throw new NotSupportedException(); }
    private sealed class PreviewContext(DbContextOptions<AppDbContext> options, ICurrentMunicipalityAccessor tenant) : AppDbContext(options, tenant)
    {
        protected override void OnModelCreating(ModelBuilder builder) { base.OnModelCreating(builder); NpmRentMonthFinancialSchema.Configure(builder); }
    }
}

public sealed partial class NpmRentMonthFinancialTests(NpmMonthFinancialFixture fixture, ITestOutputHelper output) : IClassFixture<NpmMonthFinancialFixture>
{
    private static readonly DateOnly BusinessDate = new(2026, 10, 17);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record World(Guid Tenant, Guid Actor, Guid Stall, Guid Month, decimal Daily);
    private sealed class Actor(World w, Guid? collectorId = null) : ICurrentUserService
    {
        public bool IsAuthenticated => true; public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => collectorId ?? w.Actor; public string? Username => "Synthetic actor"; public string? Role => collectorId is null ? "SuperAdmin" : "Collector";
        public Guid? CollectorId => collectorId; public string? MunicipalityCode => "synthetic"; public Guid? MunicipalityId => w.Tenant;
    }
    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor { public Guid MunicipalityId => id; public void Set(Guid id) => throw new NotSupportedException(); }
    private sealed class Clock : IClock
    {
        public DateTime UtcNow => DateTime.SpecifyKind(BusinessDate.ToDateTime(new TimeOnly(4, 0)), DateTimeKind.Utc); public DateOnly PhilippineToday => BusinessDate;
        public DateTime PhilippineNow => BusinessDate.ToDateTime(TimeOnly.MinValue);
    }
    private sealed class Rates(decimal daily) : IFeeRateResolver
    {
        public Task<FeeRateSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(new FeeRateSnapshot(
            daily > 0 ? [new FeeRateEntry(FacilityCode.NPM, FeeRateKey.NpmDailyStall, daily, new(2020, 1, 1))] : []));
    }
    private NpmRentMonthWorkflow Workflow(AppDbContext context, World w, IClock? clock = null, Guid? collectorId = null) => new(context, new Tenant(w.Tenant), new Actor(w, collectorId),
        new CollectorRepository(context), new Rates(w.Daily), clock ?? new Clock(), new(true, true, true, new HashSet<Guid> { w.Tenant }),
        new(), new NpmMarketClosureRepository(context));

    private async Task<World> Seed(decimal rent = 900, decimal daily = 35, int paidDays = 0, DateOnly? assessmentPeriod = null)
    {
        var period = assessmentPeriod ?? new DateOnly(2025, 8, 1);
        var municipality = Municipality.Create("NM-" + Guid.NewGuid().ToString("N")[..8], "Synthetic NPM", "Fixture", MunicipalityStatus.Active,
            tenantCode: "nm-" + Guid.NewGuid().ToString("N"));
        await using (var setup = fixture.Create(Guid.Empty)) { setup.Add(municipality); await setup.SaveChangesAsync(); }
        await using var db = fixture.Create(municipality.Id);
        var facility = Facility.Create(FacilityCode.NPM, "Synthetic market", "NPM", municipalityId: municipality.Id);
        var stall = Stall.Create(facility.Id, "S-1", rent, ApplicableFees.DailyRental, municipalityId: municipality.Id);
        var contract = Contract.Create(stall.Id, "Synthetic occupant", "Synthetic contract", new(2025, 1, 1), 3, rent);
        db.AddRange(facility, stall, contract); await db.SaveChangesAsync();
        var rows = new List<DailyCollection>();
        for (var i = 1; i <= paidDays; i++) { var day = DailyCollection.Create(stall.Id, period.AddDays(i - 1), "Fixture", daily); day.MarkPaid("Synthetic historical evidence", null); rows.Add(day); }
        db.DailyCollections.AddRange(rows); await db.SaveChangesAsync();
        // Reconciliation fingerprints use persisted evidence, including PostgreSQL numeric precision.
        rows = await db.DailyCollections.AsNoTracking().Where(x => x.StallId == stall.Id).ToListAsync();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(rows.OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.CollectionDate, x.CollectedFee }).ToArray(), Json))));
        var actor = Guid.NewGuid(); var utc = new Clock().UtcNow.AddMinutes(-1);
        var month = NpmRentMonth.Prepare(facility, stall, contract, period, BusinessDate,
            new(rent, 0, NpmMonthBasis.RentGoal, period.AddMonths(1).AddDays(-1), "Synthetic approved policy",
                JsonSerializer.Serialize(new { schemaVersion = 1, dailyInstallmentsApproved = daily > 0 }), "{\"approved\":true,\"credit\":0}"),
            actor, "Synthetic Head", "SuperAdmin", utc).Month;
        month.Reconcile(1, actor, "Synthetic Head", "SuperAdmin", utc,
            JsonSerializer.Serialize(new NpmRentMonthOpeningEvidence(1, rows.Select(x => x.Id).ToArray(), [], hash), Json));
        month.ActivateSettlement(2, actor, "Synthetic Head", "SuperAdmin", utc, "Explicit disposable-fixture activation");
        db.NpmRentMonths.Add(month);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, municipality.Id);
        db.RevenueClassifications.Add(classification);
        db.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(classification.Id, new(2020, 1, 1), "Rent", RevenueInstrumentType.OfficialReceipt, municipality.Id));
        await db.SaveChangesAsync(); return new(municipality.Id, actor, stall.Id, month.Id, daily);
    }
    private static NpmRentMonthPayment Payment(World w, NpmRentMonthQuote q) => new(Guid.NewGuid(), w.Stall, q.PeriodStart, BusinessDate, q.Scope, q.EligibleAmount, q.Token, q.InstallmentDate);

    [SkippableFact]
    public async Task Monthly_policy_routes_only_activated_or_prospectively_approved_stall_periods()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync();
        var w = await Seed();
        await using var db = fixture.Create(w.Tenant);
        var existing = Workflow(db, w);
        Assert.True(await existing.MustUseMonthlyAuthorityAsync(w.Stall, new(2025, 8, 1)));

        var facility = await db.Stalls.Where(s => s.Id == w.Stall).Select(s => s.FacilityId).SingleAsync();
        var oldStall = Stall.Create(facility, "LEGACY", 900m, ApplicableFees.DailyRental, municipalityId: w.Tenant);
        var oldContract = Contract.Create(oldStall.Id, "Legacy vendor", null, new(2026, 10, 1), 1, 900m);
        var newStall = Stall.Create(facility, "NEW", 900m, ApplicableFees.DailyRental, municipalityId: w.Tenant);
        var newContract = Contract.Create(newStall.Id, "New vendor", null, new(2026, 10, 1), 1, 900m);
        db.AddRange(oldStall, oldContract, newStall, newContract);
        await db.SaveChangesAsync();
        var approval = new NpmOpeningMonthPolicy(w.Tenant, facility, new(2026, 10, 1),
            w.Actor, DateTime.UtcNow.AddMinutes(-1), "TEST-APPROVED");
        var configured = new NpmRentMonthWorkflow(db, new Tenant(w.Tenant), new Actor(w),
            new CollectorRepository(db), new Rates(w.Daily), new Clock(),
            new(true, true, true, new HashSet<Guid> { w.Tenant }), new(), new NpmMarketClosureRepository(db),
            openingPolicies: new NpmOpeningMonthPolicies([approval]));
        // A legacy occupancy has no monthly source, but a prospectively approved
        // and unactivated registration must not bypass the Office review.
        Assert.True(await configured.MustUseMonthlyAuthorityAsync(newStall.Id, new(2026, 10, 1)));
        Assert.True(await configured.MustUseMonthlyAuthorityAsync(w.Stall, new(2025, 8, 1)));
        // An older period of that same stall cannot be silently converted.
        Assert.False(await configured.MustUseMonthlyAuthorityAsync(newStall.Id, new(2026, 9, 1)));
        Assert.False(await existing.MustUseMonthlyAuthorityAsync(oldStall.Id, new(2026, 10, 1)));
    }

    [SkippableTheory]
    [InlineData(900, 25, 25)] [InlineData(1000, 28, 20)] [InlineData(1900, 0, 1900)]
    [InlineData(900.25, 25, 25.25)] [InlineData(1000.05, 28, 20.05)]
    public async Task Exact_month_cash_SRC_replay_and_reversal_are_once_only(decimal rent, int days, decimal terminal)
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed(rent, days == 0 ? 0 : 35, days);
        await using var db = fixture.Create(w.Tenant); var workflow = Workflow(db, w);
        var quoted = await workflow.QuoteAsync(w.Stall, new(2025, 8, 1), BusinessDate, NpmRentPaymentScope.WholeMonth);
        Assert.True(quoted.IsSuccess, quoted.Error);
        var q = quoted.Value!;
        Assert.Equal(terminal, q.EligibleAmount); var request = Payment(w, q);
        var result = await workflow.PostAsync(request); Assert.True(result.IsSuccess, result.Error);
        Assert.StartsWith("SRC-2026-", result.Value!.ReferenceCode);
        Assert.True((await workflow.PostAsync(request)).Value!.Existing);
        Assert.Equal(1, await db.Collections.CountAsync()); Assert.Equal(terminal, await db.CollectionAllocations.SumAsync(x => x.Amount));
        Assert.Equal(days, await db.DailyCollections.CountAsync()); Assert.Empty(await db.CollectionRemittances.ToListAsync());
        Assert.Equal(0, (await workflow.QuoteAsync(w.Stall, q.PeriodStart, BusinessDate, q.Scope)).Value!.Outstanding);
        var correctionId = Guid.NewGuid();
        Assert.True((await workflow.ReverseAsync(result.Value.CollectionId, correctionId, "Synthetic reversal")).IsSuccess);
        Assert.True((await workflow.ReverseAsync(result.Value.CollectionId, correctionId, "Synthetic reversal")).Value!.Existing);
        Assert.Equal(terminal, (await workflow.QuoteAsync(w.Stall, q.PeriodStart, BusinessDate, q.Scope)).Value!.Outstanding);
        Assert.Equal(1, await db.Collections.CountAsync()); Assert.Equal(1, await db.CollectionCorrections.CountAsync());
    }

    [SkippableFact]
    public async Task Reviewed_replacement_links_cash_once_and_preserves_original_SRC()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed(900, 35, 25);
        await using var db = fixture.Create(w.Tenant); var workflow = Workflow(db, w);
        var q = (await workflow.QuoteAsync(w.Stall, new(2025, 8, 1), BusinessDate, NpmRentPaymentScope.WholeMonth)).Value!;
        var original = await workflow.PostAsync(Payment(w, q)); Assert.True(original.IsSuccess, original.Error);
        var request = new NpmRentMonthReplacementRequest(Guid.NewGuid(), original.Value!.CollectionId,
            (await db.NpmRentMonths.SingleAsync()).SettlementVersion, 25, "Synthetic review", "Verified same-day correction");
        var replaced = await workflow.ReplaceReviewedAsync(request); Assert.True(replaced.IsSuccess, replaced.Error);
        Assert.True((await workflow.ReplaceReviewedAsync(request)).Value!.Existing);
        await using var verify = fixture.Create(w.Tenant);
        Assert.Equal(2, await verify.Collections.CountAsync());
        Assert.Equal(original.Value.ReferenceCode, (await verify.Collections.SingleAsync(x => x.Id == original.Value.CollectionId)).ReferenceCode);
        Assert.Equal(replaced.Value!.CollectionId, (await verify.CollectionCorrections.SingleAsync()).ReplacementCollectionId);
        Assert.Empty(await verify.CollectionRemittances.ToListAsync());
        Assert.Equal(0, (await Workflow(verify, w).QuoteAsync(w.Stall, q.PeriodStart, BusinessDate, q.Scope)).Value!.Outstanding);
    }

    [SkippableFact]
    public async Task Failed_replacement_rolls_back_its_reversal_and_month_version()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed(900, 35, 25);
        await using var db = fixture.Create(w.Tenant); var workflow = Workflow(db, w);
        var q = (await workflow.QuoteAsync(w.Stall, new(2025, 8, 1), BusinessDate, NpmRentPaymentScope.WholeMonth)).Value!;
        var original = await workflow.PostAsync(Payment(w, q)); Assert.True(original.IsSuccess, original.Error);
        var version = (await db.NpmRentMonths.SingleAsync()).SettlementVersion;
        // Fail the replacement save after the reversal save; retain the immutable original policy FK.
        var failure = new FailReplacementSave();
        await using var failed = fixture.Create(w.Tenant, failure);
        var replaced = await Workflow(failed, w).ReplaceReviewedAsync(new(Guid.NewGuid(), original.Value!.CollectionId,
            version, 25, "Synthetic review", "Rollback proof"));
        Assert.False(replaced.IsSuccess);
        Assert.True(failure.ReversalSeen);
        await using var verify = fixture.Create(w.Tenant);
        Assert.Single(await verify.Collections.ToListAsync()); Assert.Empty(await verify.CollectionCorrections.ToListAsync());
        Assert.Single(await verify.PostingOperations.ToListAsync());
        Assert.Equal(version, (await verify.NpmRentMonths.SingleAsync()).SettlementVersion);
    }

    [SkippableFact]
    public async Task Concurrent_credit_reviews_preserve_one_version_and_no_cash()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed();
        await using var first = fixture.Create(w.Tenant); await using var second = fixture.Create(w.Tenant);
        var a = new NpmRentMonthCreditRequest(Guid.NewGuid(), w.Stall, new(2025, 8, 1), 3, 25, "Synthetic decision", "Credit evidence");
        var results = await Task.WhenAll(Workflow(first, w).ApplyReviewedCreditAsync(a),
            Workflow(second, w).ApplyReviewedCreditAsync(a with { ClientOperationId = Guid.NewGuid(), ApprovedCredits = 20 }));
        Assert.Single(results.Where(x => x.IsSuccess));
        await using var verify = fixture.Create(w.Tenant);
        Assert.Equal(4, (await verify.NpmRentMonths.SingleAsync()).SettlementVersion);
        Assert.Single(await verify.AuditLogs.Where(x => x.Action == "ReviseNpmRentMonthCredits").ToListAsync());
        Assert.Empty(await verify.Collections.ToListAsync()); Assert.Empty(await verify.PostingOperations.ToListAsync());
    }

    [SkippableFact]
    public async Task Concurrent_whole_payments_cannot_consume_one_balance_twice()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed();
        await using var first = fixture.Create(w.Tenant); await using var second = fixture.Create(w.Tenant);
        var a = Workflow(first, w); var b = Workflow(second, w);
        var qa = (await a.QuoteAsync(w.Stall, new(2025, 8, 1), BusinessDate, NpmRentPaymentScope.WholeMonth)).Value!;
        var qb = (await b.QuoteAsync(w.Stall, qa.PeriodStart, BusinessDate, qa.Scope)).Value!;
        var results = await Task.WhenAll(a.PostAsync(Payment(w, qa)), b.PostAsync(Payment(w, qb)));
        Assert.Single(results.Where(x => x.IsSuccess));
        await using var verify = fixture.Create(w.Tenant);
        Assert.Equal(1, await verify.Collections.CountAsync()); Assert.Equal(900, await verify.CollectionAllocations.SumAsync(x => x.Amount));
        Assert.Equal(4, (await verify.NpmRentMonths.SingleAsync()).SettlementVersion);
    }

    [SkippableFact]
    public async Task Rolled_back_post_leaves_no_cash_SRC_operation_or_month_version_change()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed(900, 35, 25);
        await using (var db = fixture.Create(w.Tenant))
        {
            var workflow = Workflow(db, w);
            var quote = (await workflow.QuoteAsync(w.Stall, new(2025, 8, 1), BusinessDate, NpmRentPaymentScope.WholeMonth)).Value!;
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            Assert.True((await workflow.PostAsync(Payment(w, quote))).IsSuccess);
            Assert.Single(await db.Collections.ToListAsync());
            await transaction.RollbackAsync();
        }
        await using var verify = fixture.Create(w.Tenant);
        Assert.Empty(await verify.Collections.ToListAsync()); Assert.Empty(await verify.CollectionAllocations.ToListAsync());
        Assert.Empty(await verify.PostingOperations.ToListAsync()); Assert.Empty(await verify.CollectionRemittances.ToListAsync());
        Assert.Equal(3, (await verify.NpmRentMonths.SingleAsync()).SettlementVersion);
        Assert.Equal(25, (await Workflow(verify, w).ReadPositionAsync(w.Stall, new(2025, 8, 1)))!.Outstanding);
    }

    [SkippableFact]
    public async Task Stale_credit_revision_cannot_overwrite_a_committed_month_version()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var w = await Seed();
        await using var first = fixture.Create(w.Tenant); await using var second = fixture.Create(w.Tenant);
        var a = await first.NpmRentMonths.SingleAsync(); var b = await second.NpmRentMonths.SingleAsync();
        a.ReviseCredits(10, "{\"reference\":\"fixture-first\"}", 3, w.Actor, "Synthetic Head", "SuperAdmin", new Clock().UtcNow, "Synthetic credit");
        await first.SaveChangesAsync();
        b.ReviseCredits(20, "{\"reference\":\"fixture-stale\"}", 3, w.Actor, "Synthetic Head", "SuperAdmin", new Clock().UtcNow, "Synthetic stale credit");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using var verify = fixture.Create(w.Tenant);
        Assert.Equal(10, (await verify.NpmRentMonths.SingleAsync()).CreditedAmount);
        Assert.Empty(await verify.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Typed_allocation_FK_rejects_another_tenants_month()
    {
        Skip.IfNot(fixture.Available, fixture.UnavailableReason ?? "Docker unavailable");
        await fixture.ResetAsync(); var a = await Seed(); var b = await Seed();
        await using var db = fixture.Create(a.Tenant);
        var classification = await db.RevenueClassifications.SingleAsync(); var policy = await db.RevenueClassificationPolicies.SingleAsync();
        var invalid = Collection.Post(BusinessDate, DateTime.UtcNow, a.Actor.ToString(), "Fixture", "SuperAdmin",
            [new CollectionLineDraft(classification, policy, 25, CollectionSourceKind.NpmRentMonth, b.Month, null,
                Allocations: [new(CollectionSourceKind.NpmRentMonth, b.Month, 25)])]);
        db.Collections.Add(invalid);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23503", Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
}
