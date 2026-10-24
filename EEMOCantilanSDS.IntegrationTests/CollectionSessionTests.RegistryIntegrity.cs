using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using Microsoft.EntityFrameworkCore;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Domain.Constants;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using EEMOCantilanSDS.Api;
using EEMOCantilanSDS.Application.Common.Revenue;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class CollectionSessionTests
{
    [SkippableFact]
    public async Task Normal_request_activation_works_through_API_and_delete_is_head_only_and_audited()
    {
        var w = await SeedAsync();
        await using (var db = database.CreateContext(w.TenantId)) await EnableOffice(db, w);
        var config = new ConfigurationBuilder().Build(); // Default-on for schema-ready tenants.
        void Activate(IServiceCollection services) => services.AddFishMeatRequestActivation(config);
        await using var collector = await CollectorApiHost.StartAsync(database, w.CollectorId, w.TenantId, afterApplication: Activate);
        var submitted = await ApiPost<FishMeatRegistrationRequestDto>(collector.Client, "/api/office-sources/fish-meat/registration-requests",
            new SubmitFishMeatRegistrationRequest(Guid.NewGuid(), "Synthetic API vendor", FishMeatVendorType.Meat, PhilippineTime.Today.Year));
        await using var head = await CollectorApiHost.StartAsync(database, w.CollectorId, w.TenantId, role: "SuperAdmin", afterApplication: Activate);
        var approved = await ApiPost<FishMeatRegistrationRequestDto>(head.Client,
            $"/api/office-sources/fish-meat/registration-requests/{submitted.Id}/complete",
            new CompleteFishMeatRegistrationRequest(Guid.NewGuid(), 1, "Synthetic office verification",
                new(Guid.NewGuid(), PhilippineTime.Today.Year, FishMeatVendorType.Meat, VendorRegistrationKind.New,
                    "Synthetic approved vendor", LastName: "Family", FirstName: "Given", NatureOfBusiness: "Retail meat")));
        Assert.Equal(FishMeatRequestStatus.Approved, approved.Status);
        var seen = await collector.Client.GetFromJsonAsync<FishMeatRegistrationRequestDto>(
            $"/api/office-sources/fish-meat/registration-requests/{submitted.Id}", CollectorApiHost.Json);
        Assert.Equal(approved.RegistrationId, seen!.RegistrationId);
        var id = approved.RegistrationId!.Value;
        var deletion = new DeleteVendorRegistrationRequest(Guid.NewGuid(), "Synthetic reviewed deletion");
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await collector.Client.PostAsJsonAsync(
            $"/api/office-sources/fish-meat/registrations/{id}/delete", deletion, CollectorApiHost.Json)).StatusCode);
        await ApiPost<VendorRegistrationMutationResult>(head.Client, $"/api/office-sources/fish-meat/registrations/{id}/close", new CloseVendorRegistrationRequest(Guid.NewGuid()));
        var deleted = await ApiPost<VendorRegistrationMutationResult>(head.Client, $"/api/office-sources/fish-meat/registrations/{id}/delete", deletion);
        Assert.Equal(VendorRegistrationStatus.Deleted, deleted.Status);
        await using var verify = database.CreateContext(w.TenantId);
        Assert.Empty(await verify.Collections.ToListAsync()); Assert.Empty(await verify.AccountableDocuments.ToListAsync());
        Assert.Equal(FishMeatRequestStatus.Approved, (await verify.FishMeatRegistrationRequests.SingleAsync()).Status);
        Assert.True(await verify.AuditLogs.AnyAsync(x => x.EntityId == id && x.NewValues != null && x.NewValues.Contains("DeleteReason")));
        Assert.Empty(head.Services.GetRequiredService<FishMeatRegistrationOptions>().RegistrationRequiredFrom);
    }

    [SkippableFact]
    public async Task Deletion_migration_rolls_back_before_use_but_never_discards_tombstones()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        Assert.Equal("stalltrack_tests", connection.Database); Assert.NotEqual(5432, connection.Port);
        var migrations = db.Database.GetMigrations().ToArray();
        const string current = "20261022124440_FishMeatRegistrationLogicalDeletion20261022";
        var currentIndex = Array.IndexOf(migrations, current);
        Assert.True(currentIndex > 0);
        var previous = migrations[currentIndex - 1];
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(previous);
        Assert.DoesNotContain(current, await db.Database.GetAppliedMigrationsAsync());
        await migrator.MigrateAsync(current);
        var vendor = FishMeatVendorRegistration.Register(w.TenantId, Guid.NewGuid(), PhilippineTime.Today.Year,
            FishMeatVendorType.Fish, VendorRegistrationKind.New, "Synthetic tombstone", null, null, null, "Head");
        vendor.Close(Guid.NewGuid(), PhilippineTime.Today, "Head", "Reviewed");
        vendor.DeleteClosed(Guid.NewGuid(), "Head", "Retain evidence", DateTime.UtcNow);
        db.FishMeatVendorRegistrations.Add(vendor); await db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => migrator.MigrateAsync(previous));
        Assert.Contains("tombstones", error.MessageText);
        Assert.Contains(current, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal("Retain evidence", (await db.FishMeatVendorRegistrations.AsNoTracking().SingleAsync()).DeleteReason);
        // Other features can introduce newer migrations; restore the disposable fixture's latest schema.
        await migrator.MigrateAsync(migrations[^1]);
    }

    [SkippableFact]
    public async Task Pending_request_must_be_reviewed_before_deletion_and_delete_races_commit_one_intent()
    {
        var w = await SeedAsync(); Guid id;
        await using (var db = database.CreateContext(w.TenantId))
        {
            await EnableOffice(db, w); var head = Office(db, w, "SuperAdmin");
            id = (await head.RegisterAsync(new(Guid.NewGuid(), PhilippineTime.Today.Year,
                FishMeatVendorType.Meat, VendorRegistrationKind.New, "Synthetic pending vendor"))).Value!.Id;
            var pending = (await Office(db, w).SubmitNpmRequestAsync(new(Guid.NewGuid(), id, "M1", null, "Verify office evidence"))).Value!;
            await head.CloseRegistrationAsync(id, new(Guid.NewGuid()));
            Assert.Equal("ReviewPendingNpmRequestsBeforeDelete", (await head.DeleteRegistrationAsync(id, new(Guid.NewGuid(), "Reviewed"))).Error);
            Assert.True((await head.ReviewNpmRequestAsync(pending.Id, new(Guid.NewGuid(), pending.Version,
                NpmRegistrationRequestStatus.Rejected, "Registration is closed; preserve proposal history"))).IsSuccess);
        }
        await using var first = database.CreateContext(w.TenantId); await using var second = database.CreateContext(w.TenantId);
        var results = await Task.WhenAll(
            Office(first, w, "SuperAdmin").DeleteRegistrationAsync(id, new(Guid.NewGuid(), "Reviewed first intent")),
            Office(second, w, "SuperAdmin").DeleteRegistrationAsync(id, new(Guid.NewGuid(), "Reviewed second intent")));
        Assert.Single(results, x => x.IsSuccess); Assert.Single(results, x => x.Status == ResultStatus.Conflict);
        await using var check = database.CreateContext(w.TenantId);
        Assert.Equal(NpmRegistrationRequestStatus.Rejected, (await check.NpmRegistrationRequests.SingleAsync()).Status);
        Assert.Equal(VendorRegistrationStatus.Deleted, (await check.FishMeatVendorRegistrations.SingleAsync()).Status);
        Assert.Empty(await check.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Request_schema_absence_returns_safe_diagnostic_without_creating_any_money()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FishMeatRegistrationRequests\" RENAME TO \"SyntheticMissingRequests\"");
        var head = new EEMOCantilanSDS.Application.Common.Revenue.OfficeCollectionWorkflow(db,
            new Caller(w.CollectorId, w.TenantId, "SuperAdmin"), new Tenant(w.TenantId), new Clock(PhilippineTime.Today),
            new EEMOCantilanSDS.Infrastructure.Fees.FeeRateResolver(db),
            new EEMOCantilanSDS.Application.Common.Revenue.FishMeatRegistrationOptions { RequestsInstalled = true, RequestTenants = new HashSet<Guid> { w.TenantId } },
            new FishMeatRequestSchema(db));
        Assert.Equal("VendorRequestSchemaNotEnabled", (await head.VendorRequestsAsync()).Error);
        await tx.RollbackAsync();
        Assert.Empty(await db.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Delete_requires_closed_withdraws_only_links_and_preserves_cash_and_occupancy()
    {
        var w = await SeedAsync(section: MarketSection.MeatSection);
        await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var head = Office(db, w, "SuperAdmin"); var today = PhilippineTime.Today;
        var candidate = Assert.Single((await head.NpmVendorCandidatesAsync(today.Year)).Value!.Rows);
        var operation = Guid.NewGuid();
        var vendor = (await head.RegisterAsync(new(operation, today.Year, FishMeatVendorType.Meat,
            VendorRegistrationKind.New, "Synthetic linked vendor", NpmAssociation: Confirmation(candidate, operation)))).Value!;
        var source = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(today), null!, new NoMarketDays());
        Assert.Equal(LinkedNpmStatus.Available, (await source.DiscoverNativeAsync(new(SourceIdentityKind.FishMeatVendorRegistration, vendor.Id), today, default)).NpmLinkStatus);
        var payment = new SourceNativeCollectionRequest(Guid.NewGuid(), today, 50m, new(CollectorOperationCodes.FishMeatVendorFee, vendor.Id));
        var cash = (await Office(db, w).PostAsync(payment)).Value!;
        var before = Assert.Single((await head.ActivityAsync(today, today)).Value!);
        var deletion = new DeleteVendorRegistrationRequest(Guid.NewGuid(), "Reviewed duplicate office registration");
        Assert.Equal("CloseRegistrationBeforeDelete", (await head.DeleteRegistrationAsync(vendor.Id, deletion)).Error);
        Assert.Equal(ResultStatus.Forbidden, (await Office(db, w, "Admin").DeleteRegistrationAsync(vendor.Id, deletion)).Status);
        Assert.True((await head.CloseRegistrationAsync(vendor.Id, new(Guid.NewGuid()))).IsSuccess);
        Assert.Null((await db.FishMeatNpmAssociations.SingleAsync()).CorrectedAtUtc);
        Assert.True((await head.DeleteRegistrationAsync(vendor.Id, deletion)).IsSuccess);
        db.ChangeTracker.Clear();
        Assert.Equal(VendorRegistrationStatus.Deleted, (await db.FishMeatVendorRegistrations.SingleAsync()).Status);
        Assert.NotNull((await db.FishMeatNpmAssociations.SingleAsync()).CorrectedAtUtc);
        Assert.Single(await db.Contracts.ToListAsync()); Assert.Single(await db.Stalls.ToListAsync());
        var replay = (await head.DeleteRegistrationAsync(vendor.Id, deletion)).Value!;
        Assert.True(replay.ExistingOutcome); Assert.NotNull(replay.DeletedAtUtc); Assert.Equal(deletion.Reason, replay.DeleteReason);
        Assert.Equal("RegistrationDeleted", (await head.DeleteRegistrationAsync(vendor.Id, deletion with { ClientOperationId = Guid.NewGuid() })).Error);
        Assert.Equal(ResultStatus.Conflict, (await head.DeleteRegistrationAsync(vendor.Id, deletion with { Reason = "Changed" })).Status);
        Assert.Empty((await head.ManageRegistrationsAsync(today.Year, today.Month)).Value!.Rows);
        Assert.Single((await head.ManageRegistrationsAsync(today.Year, today.Month, VendorRegistrationStatus.Deleted)).Value!.Rows);
        Assert.Empty((await head.RegistrationsAsync(today.Year)).Value!);
        Assert.Empty((await source.DiscoverNativeAsync(new(SourceIdentityKind.FishMeatVendorRegistration, vendor.Id), today, default)).Operations);
        Assert.Equal(cash.CollectionId, (await Office(db, w).PostAsync(payment)).Value!.CollectionId);
        Assert.Equal(before, Assert.Single((await head.ActivityAsync(today, today)).Value!));
        Assert.Single(await db.Collections.ToListAsync());
        var export = await new EEMOCantilanSDS.Infrastructure.Repositories.SystemHealth.TenantExportRepository(db).ExportAsync(default);
        var tombstone = Assert.Single(Assert.IsType<List<FishMeatVendorRegistration>>(export.Tables["FishMeatVendorRegistrations"]));
        Assert.Equal(deletion.Reason, tombstone.DeleteReason); Assert.Equal(vendor.Id, tombstone.Id);
        Assert.Equal("RegistrationDeleted", (await head.RenewRegistrationAsync(vendor.Id, new(Guid.NewGuid(), today.Year + 1))).Error);
        await using var foreign = database.CreateContext(Guid.NewGuid());
        Assert.Equal(ResultStatus.NotFound, (await Office(foreign, w with { TenantId = foreign.CurrentMunicipalityId }, "SuperAdmin")
            .DeleteRegistrationAsync(vendor.Id, deletion)).Status);
    }

    [SkippableFact]
    public async Task Linked_source_diagnostics_do_not_claim_missing_registration_when_collector_lacks_assignment()
    {
        var w = await SeedAsync(section: MarketSection.MeatSection);
        await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var head = Office(db, w, "SuperAdmin"); var today = PhilippineTime.Today;
        var vendor = (await head.RegisterAsync(new(Guid.NewGuid(), today.Year, FishMeatVendorType.Meat,
            VendorRegistrationKind.New, "Same name is not a link"))).Value!;
        var source = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(today), null!, new NoMarketDays());
        var identity = new CollectionSourceIdentity(SourceIdentityKind.FishMeatVendorRegistration, vendor.Id);
        Assert.Equal(LinkedNpmStatus.NotLinked, (await source.DiscoverNativeAsync(identity, today, default)).NpmLinkStatus);
        var collector = await db.CollectorUsers.Include(x => x.FacilityAssignments).SingleAsync(x => x.Id == w.CollectorId);
        db.RemoveRange(collector.FacilityAssignments); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        Assert.Equal(LinkedNpmStatus.NotAuthorized, (await source.DiscoverNativeAsync(identity, today, default)).NpmLinkStatus);
        Assert.Empty(await db.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Import_preserves_office_fields_in_database_and_detects_changed_retry()
    {
        var w = await SeedAsync();
        await using var db = database.CreateContext(w.TenantId);
        var head = Office(db, w, "SuperAdmin");
        var fish = new RegisterFishMeatVendorRequest(Guid.NewGuid(), PhilippineTime.Today.Year,
            FishMeatVendorType.Fish, VendorRegistrationKind.Renew, "Synthetic Fish Vendor",
            BusinessName: "Fish business", LastName: " Family ", FirstName: " Given ", MiddleName: " Middle ",
            Sex: " Male ", NatureOfBusiness: " Retail seafood ");
        var meat = new RegisterFishMeatVendorRequest(Guid.NewGuid(), PhilippineTime.Today.Year,
            FishMeatVendorType.Meat, VendorRegistrationKind.New, "Synthetic Meat Vendor",
            LastName: "Family", FirstName: "Given", NatureOfBusiness: "Retail meat");
        var request = new VendorRegistryImportRequest([new(1, fish), new(2, meat)]);
        var preview = (await head.PreviewImportAsync(request)).Value!;
        Assert.True(preview.CanSave);
        Assert.Equal("Family", preview.Rows[0].Registration.LastName);
        var result = await head.SaveImportAsync(request);
        Assert.True(result.IsSuccess, result.Error);
        db.ChangeTracker.Clear();
        var saved = await db.FishMeatVendorRegistrations.SingleAsync(x => x.ClientOperationId == fish.ClientOperationId);
        Assert.Equal(("Family", "Given", "Middle", "Male", "Retail seafood"),
            (saved.LastName, saved.FirstName, saved.MiddleName, saved.Sex, saved.NatureOfBusiness));
        Assert.Null(saved.Address); Assert.Null(saved.NameExtension); Assert.Null(saved.Reference);
        var blank = await db.FishMeatVendorRegistrations.SingleAsync(x => x.ClientOperationId == meat.ClientOperationId);
        Assert.Null(blank.Sex); Assert.Null(blank.MiddleName);
        var projected = (await head.ManageRegistrationsAsync(fish.TaxYear, PhilippineTime.Today.Month)).Value!.Rows.Single(x => x.Registration.Id == saved.Id).Registration;
        Assert.Equal("Family", projected.LastName); Assert.Equal("Retail seafood", projected.NatureOfBusiness);
        Assert.True((await head.SaveImportAsync(request)).IsSuccess);
        var conflict = request with { Rows = [new(1, fish with { Sex = "Different" })] };
        Assert.False((await head.PreviewImportAsync(conflict)).Value!.CanSave);
        Assert.False((await head.SaveImportAsync(conflict)).IsSuccess);
        Assert.Equal(2, await db.FishMeatVendorRegistrations.CountAsync());
        Assert.Empty(await db.Collections.ToListAsync());
        Assert.Empty(await db.AccountableDocuments.ToListAsync());
        Assert.Empty(await db.FishMeatNpmAssociations.ToListAsync());
    }
}
