using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Moq;

namespace EEMOCantilanSDS.Testing;

/// <summary>Relational MODEL/SQL proofs only; no PostgreSQL connection or constraint execution.</summary>
public sealed class NpmRentMonthModelTests
{
    private static AppDbContext RelationalModel() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql("Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused").Options);

    [Fact]
    public void One_unique_stall_month_excludes_contract_and_version_from_uniqueness()
    {
        using var context = RelationalModel();
        var model = context.GetService<IDesignTimeModel>().Model;
        var entity = model.FindEntityType(typeof(NpmRentMonth))!;
        Assert.Single(entity.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(p => p.Name).SequenceEqual(new[] { "MunicipalityId", "StallId", "PeriodStart" }));
        Assert.DoesNotContain(entity.GetIndexes().Where(x => x.IsUnique), index => index.Properties.Any(p => p.Name == "ContractId"));
        Assert.True(entity.FindProperty(nameof(NpmRentMonth.SettlementVersion))!.IsConcurrencyToken);
        Assert.Contains(entity.GetKeys(), key => key.Properties.Select(p => p.Name).SequenceEqual(new[] { "MunicipalityId", "Id" }));
        // Database uniqueness/races still need PostgreSQL tests; two constructed objects alone cannot prove prevention.
    }

    [Theory]
    [InlineData(typeof(Facility), "MunicipalityId,FacilityId", "MunicipalityId,Id")]
    [InlineData(typeof(Stall), "MunicipalityId,StallId,FacilityId", "MunicipalityId,Id,FacilityId")]
    [InlineData(typeof(Contract), "MunicipalityId,ContractId,StallId", "MunicipalityId,Id,StallId")]
    public void Foreign_keys_bind_same_tenant_facility_stall_and_responsible_contract(Type principal, string dependent, string key)
    {
        using var context = RelationalModel();
        var entity = context.Model.FindEntityType(typeof(NpmRentMonth))!;
        var fk = Assert.Single(entity.GetForeignKeys(), x => x.PrincipalEntityType.ClrType == principal);
        Assert.Equal(dependent, string.Join(',', fk.Properties.Select(p => p.Name)));
        Assert.Equal(key, string.Join(',', fk.PrincipalKey.Properties.Select(p => p.Name)));
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public void Relational_create_script_contains_only_preparation_states_and_required_constraints()
    {
        using var context = RelationalModel();
        var sql = context.Database.GenerateCreateScript(); // Generates text only. Does not create/connect to a database.
        Assert.Contains("CREATE TABLE \"NpmRentMonths\"", sql);
        Assert.Contains("CK_NpmRentMonths_Period", sql);
        Assert.Contains("CK_NpmRentMonths_Money", sql);
        Assert.Contains("CK_NpmRentMonths_Review", sql);
        Assert.Contains("\"ReconciledAtUtc\" IS NOT NULL", sql);
        Assert.Contains("CK_NpmRentMonths_Evidence", sql);
        Assert.Contains("IX_NpmRentMonths_MunicipalityId_StallId_PeriodStart", sql);
        var model = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(NpmRentMonth))!;
        Assert.Equal(18, model.FindProperty("AssessedAmount")!.GetPrecision());
        Assert.Equal(2, model.FindProperty("AssessedAmount")!.GetScale());
        Assert.Null(model.FindProperty("CanAcceptCollections"));
        Assert.Null(model.FindProperty("OutstandingAmount"));
        Assert.Null(model.FindProperty("CollectedAmount"));
        Assert.Null(model.FindProperty("ReferenceCode"));
        Assert.DoesNotContain(model.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(Collection));
    }

    [Fact]
    public void Offline_schema_diff_is_additive_and_creates_only_the_preparation_table()
    {
        using var context = RelationalModel();
        var registry = context.GetService<IMigrationsAssembly>();
        var snapshot = context.GetService<IModelRuntimeInitializer>().Initialize(registry.ModelSnapshot!.Model, designTime: true);
        var current = context.GetService<IDesignTimeModel>().Model;
        Assert.False(context.GetService<IMigrationsModelDiffer>()
            .HasDifferences(snapshot.GetRelationalModel(), current.GetRelationalModel()));
        var operations = registry.CreateMigration(registry.Migrations["20261017040000_AddNpmRentMonthFoundation"],
            context.Database.ProviderName!).UpOperations;
        Assert.Equal(7, operations.Count);
        Assert.Equal("NpmRentMonths", Assert.Single(operations.OfType<CreateTableOperation>()).Name);
        Assert.All(operations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation
            or AddUniqueConstraintOperation or AddForeignKeyOperation,
            $"Unexpected operation requires separate review: {operation.GetType().Name}"));
        Assert.DoesNotContain(operations, operation => operation is SqlOperation or InsertDataOperation
            or UpdateDataOperation or DeleteDataOperation or DropTableOperation or DropColumnOperation);
        // Verifies the model diff, not execution, existing database state or concurrent constraint enforcement.
    }

    [Fact]
    public void Tracking_preparation_and_audit_creates_no_cash_receipt_allocation_daily_row_or_posting()
    {
        using var context = RelationalModel();
        var (facility, stall, contract) = NpmRentMonthTests.Source();
        var prepared = NpmRentMonth.Prepare(facility, stall, contract, new(2025, 8, 1), new(2026, 10, 16),
            NpmRentMonthTests.Evidence(), NpmRentMonthTests.Reviewer, "Reviewer", "SuperAdmin", NpmRentMonthTests.Utc);
        context.NpmRentMonths.Add(prepared.Month); context.AuditLogs.Add(prepared.Audit);
        Assert.Equal(2, context.ChangeTracker.Entries().Count());
        Assert.Empty(context.Collections.Local); Assert.Empty(context.CollectionAllocations.Local);
        Assert.Empty(context.PostingOperations.Local); Assert.Empty(context.DailyCollections.Local);
        Assert.Empty(context.CollectionRemittances.Local); Assert.Empty(context.AccountableDocuments.Local);
        // Nothing is saved; no API, coordinator or real database is called.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dormant_month_source_cannot_bypass_the_coordinated_writer_for_either_tenant(bool otherTenant)
    {
        var month = NpmRentMonthTests.Month();
        var tenant = otherTenant ? Guid.NewGuid() : month.MunicipalityId;
        var classification = RevenueClassification.Create("STALL_RENTAL", tenant);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new(2025, 8, 1), "Fixture",
            RevenueInstrumentType.OfficialReceipt, tenant);
        Assert.True(Enum.IsDefined(CollectionSourceKind.NpmRentMonth));
        Assert.False(new EEMOCantilanSDS.Application.Common.Payments.NpmRentMonthRuntime().Allows(tenant));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EEMOCantilanSDS.Application.Common.Revenue.CanonicalCollectionPostingCoordinator(Mock.Of<EEMOCantilanSDS.Application.Common.Interface.Persistence.IAppDbContext>())
                .PostAsync(tenant, Guid.NewGuid(), 1, "unpermitted", "OtherWriter", "Actor", "Actor", "SuperAdmin", new(2025, 8, 31), "Actor",
                    [new CollectionLineDraft(classification, policy, 25m, CollectionSourceKind.NpmRentMonth, month.Id, null,
                        Allocations: [new(CollectionSourceKind.NpmRentMonth, month.Id, 25m)])]));
        // Enum registration alone cannot enable a writer. The inactive EF model does not query uninstalled columns.
        var undefined = (CollectionSourceKind)(Enum.GetValues<CollectionSourceKind>().Max(x => (int)x) + 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => Collection.Post(new(2025, 8, 31), NpmRentMonthTests.Utc,
            "Reviewer", "Reviewer", "SuperAdmin",
            [new CollectionLineDraft(classification, policy, 25m, undefined, month.Id, null,
                Allocations: [new CollectionAllocationDraft(undefined, month.Id, 25m)])]));
    }

    [Fact]
    public async Task Existing_global_filter_covers_the_new_month_and_fails_closed_without_resolved_tenant()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"npm-month-filter-{Guid.NewGuid()}").Options;
        var first = NpmRentMonthTests.Month(); var second = NpmRentMonthTests.Month();
        await using (var seed = new AppDbContext(options))
        {
            seed.NpmRentMonths.AddRange(first, second); await seed.SaveChangesAsync();
        }
        await using var own = new AppDbContext(options, new Tenant(first.MunicipalityId));
        Assert.Equal(first.Id, Assert.Single(await own.NpmRentMonths.ToListAsync()).Id);
        await using var unresolved = new AppDbContext(options, new Tenant(Guid.Empty));
        Assert.Empty(await unresolved.NpmRentMonths.ToListAsync());
        // InMemory proves filter execution only, never real FK/unique/concurrency enforcement.
    }

    [Fact]
    public void New_month_evidence_is_in_existing_backup_inventory()
    {
        Assert.Contains("NpmRentMonths", TenantDataTables.Restorable);
        Assert.Contains("AuditLogs", TenantDataTables.ExportOnly);
    }

    [Fact]
    public async Task Existing_audit_interceptor_records_preparation_and_revision_even_without_explicit_audit_attachment()
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.UserId).Returns(NpmRentMonthTests.Reviewer);
        user.SetupGet(x => x.Username).Returns("Reviewer"); user.SetupGet(x => x.Role).Returns("SuperAdmin");
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"npm-month-audit-{Guid.NewGuid()}")
            .AddInterceptors(new AuditSaveChangesInterceptor(user.Object)).Options;
        await using var context = new AppDbContext(options);
        var month = NpmRentMonthTests.Month();
        context.NpmRentMonths.Add(month); await context.SaveChangesAsync();
        month.ReviseAssessment(NpmRentMonthTests.Evidence(1000m), 1, NpmRentMonthTests.Reviewer,
            "Reviewer", "SuperAdmin", NpmRentMonthTests.Utc, "Approved fixture revision");
        await context.SaveChangesAsync();
        var audits = await context.AuditLogs.Where(a => a.EntityId == month.Id).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => { Assert.Equal(month.MunicipalityId, a.MunicipalityId); Assert.Equal("SuperAdmin", a.ActorRole); });
        Assert.Contains(audits, a => a.NewValues!.Contains("Approved fixture revision"));
        Assert.Empty(context.Collections.Local); Assert.Empty(context.CollectionAllocations.Local);
        // In-process audit-pipeline proof only, not real database transaction or actor authorization testing.
    }

    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) => throw new NotSupportedException();
    }
}
