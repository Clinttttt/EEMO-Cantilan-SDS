using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Persistence.Configuration;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class CollectionSessionTests
{
    // The request entity now belongs to the official migrated AppDbContext.
    private AppDbContext VendorRequestContext(Guid tenant) => database.CreateContext(tenant);
    private static OfficeCollectionWorkflow VendorRequests(AppDbContext db, World w, string role = "Collector", bool policy = false,
        bool installed = true) => new(db, new Caller(w.CollectorId, w.TenantId, role), new Tenant(w.TenantId),
            new Clock(PhilippineTime.Today), new FeeRateResolver(db), new FishMeatRegistrationOptions {
                RequestsInstalled = installed, RegistrationRequiredFrom = policy
                    ? new Dictionary<Guid, DateOnly> { [w.TenantId] = PhilippineTime.Today } : new Dictionary<Guid, DateOnly>() });

    // Assert the reviewed schema was installed by official EF migration, never create a
    // hidden candidate table after the fixture migrates.
    private static async Task InstallVendorRequestCandidate(AppDbContext db)
    {
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        Assert.Equal("stalltrack_tests", connection.Database); Assert.NotEqual(5432, connection.Port);
        var exists = await db.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM pg_tables WHERE schemaname='public' AND tablename='FishMeatRegistrationRequests'").SingleAsync();
        Assert.Equal(1, exists);
        Assert.Contains("20261020133853_AddFishMeatRegistrationRequestsWorkingCopy20261020",
            await db.Database.GetAppliedMigrationsAsync());
    }
    [SkippableFact]
    public async Task Vendor_request_retry_duplicate_return_cancel_and_head_approval_are_nonfinancial()
    {
        var w = await SeedAsync(); await using var db = VendorRequestContext(w.TenantId);
        await InstallVendorRequestCandidate(db); await EnableOffice(db, w);
        const string sequenceSql = "SELECT last_value::text || ':' || is_called::text AS \"Value\" FROM \"CollectionReferenceNumberSeq\"";
        var srcBefore = await db.Database.SqlQueryRaw<string>(sequenceSql).SingleAsync();
        var collector = VendorRequests(db, w); var head = VendorRequests(db, w, "SuperAdmin");
        var request = new SubmitFishMeatRegistrationRequest(Guid.NewGuid(), "Prospective vendor", FishMeatVendorType.Fish,
            PhilippineTime.Today.Year, Note: "Office must verify official identity");
        var submitted = await collector.SubmitVendorRequestAsync(request); Assert.True(submitted.IsSuccess, submitted.Error);
        var row = submitted.Value!;
        Assert.Equal(row.Id, (await collector.SubmitVendorRequestAsync(request)).Value!.Id);
        Assert.Equal(ResultStatus.Conflict, (await collector.SubmitVendorRequestAsync(request with { Note = "Different intent" })).Status);
        Assert.Equal(ResultStatus.Conflict, (await collector.SubmitVendorRequestAsync(request with { ClientOperationId = Guid.NewGuid() })).Status);
        Assert.True((await collector.SubmitVendorRequestAsync(request with { ClientOperationId = Guid.NewGuid(),
            Contact = "Different proposed vendor evidence; equal names are not identity" })).IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, (await collector.ReviewVendorRequestAsync(row.Id,
            new(Guid.NewGuid(), 1, FishMeatRequestStatus.Rejected, "Not Head"))).Status);
        Assert.Equal(ResultStatus.Conflict, (await head.ReviewVendorRequestAsync(row.Id,
            new(Guid.NewGuid(), 1, FishMeatRequestStatus.Approved, "Verified", Guid.NewGuid()))).Status);
        var returned = new ReviewFishMeatRegistrationRequest(Guid.NewGuid(), 1, FishMeatRequestStatus.Returned, "Confirm details");
        Assert.True((await head.ReviewVendorRequestAsync(row.Id, returned)).IsSuccess);
        Assert.True((await head.ReviewVendorRequestAsync(row.Id, returned)).IsSuccess);
        Assert.Equal(ResultStatus.Conflict, (await head.ReviewVendorRequestAsync(row.Id, returned with { Reason = "Changed" })).Status);
        var next = (await collector.SubmitVendorRequestAsync(request with { ClientOperationId = Guid.NewGuid(), ReplacesRequestId = row.Id })).Value!;
        var register = new RegisterFishMeatVendorRequest(next.Id, PhilippineTime.Today.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New,
            "Office verified name", Address: "Office recorded address");
        var vendor = (await head.RegisterAsync(register)).Value!;
        Assert.Equal(vendor.Id, (await head.RegisterAsync(register)).Value!.Id);
        var approval = new ReviewFishMeatRegistrationRequest(Guid.NewGuid(), 1, FishMeatRequestStatus.Approved, "Office identity verified", vendor.Id);
        Assert.True((await head.ReviewVendorRequestAsync(next.Id, approval)).IsSuccess);
        Assert.True((await head.ReviewVendorRequestAsync(next.Id, approval)).IsSuccess);
        Assert.Single(await db.FishMeatVendorRegistrations.ToListAsync());
        var cancelled = (await collector.SubmitVendorRequestAsync(request with { ClientOperationId = Guid.NewGuid() })).Value!;
        var cancel = new CancelFishMeatRegistrationRequest(Guid.NewGuid(), 1);
        Assert.True((await collector.CancelVendorRequestAsync(cancelled.Id, cancel)).IsSuccess);
        Assert.True((await collector.CancelVendorRequestAsync(cancelled.Id, cancel)).IsSuccess);
        Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.PostingOperations.ToListAsync());
        Assert.Empty(await db.CollectionAllocations.ToListAsync()); Assert.Empty(await db.FishMeatNpmAssociations.ToListAsync());
        Assert.Empty(await db.AccountableDocuments.ToListAsync()); Assert.Empty(await db.CollectionRemittances.ToListAsync());
        Assert.Equal(srcBefore, await db.Database.SqlQueryRaw<string>(sequenceSql).SingleAsync());
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Vendor_request_concurrent_submission_and_decisions_commit_once(int repetition)
    {
        var w = await SeedAsync();
        await using (var setup = VendorRequestContext(w.TenantId)) { await InstallVendorRequestCandidate(setup); await EnableOffice(setup, w); }
        var request = new SubmitFishMeatRegistrationRequest(Guid.NewGuid(), "Concurrency " + repetition, FishMeatVendorType.Meat, PhilippineTime.Today.Year);
        await using var first = VendorRequestContext(w.TenantId); await using var second = VendorRequestContext(w.TenantId);
        var submissions = await Task.WhenAll(VendorRequests(first, w).SubmitVendorRequestAsync(request),
            VendorRequests(second, w).SubmitVendorRequestAsync(request with { ClientOperationId = Guid.NewGuid() }));
        var row = Assert.Single(submissions, x => x.IsSuccess).Value!;
        var decisions = await Task.WhenAll(VendorRequests(first, w, "SuperAdmin").ReviewVendorRequestAsync(row.Id,
            new(Guid.NewGuid(), 1, FishMeatRequestStatus.Returned, "Evidence")),
            VendorRequests(second, w, "SuperAdmin").ReviewVendorRequestAsync(row.Id,
            new(Guid.NewGuid(), 1, FishMeatRequestStatus.Rejected, "Rejected")));
        Assert.Single(decisions, x => x.IsSuccess); Assert.Single(decisions, x => x.Status == ResultStatus.Conflict);
        Assert.Empty(await first.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Vendor_registration_first_policy_blocks_typed_bypass_but_preserves_posted_replay()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var legacy = Office(db, w);
        var intent = new SourceNativeCollectionRequest(Guid.NewGuid(), PhilippineTime.Today, 123.45m,
            new(CollectorOperationCodes.FishMeatVendorFee), "Unregistered snapshot");
        var posted = await legacy.PostAsync(intent); Assert.True(posted.IsSuccess, posted.Error);
        var policy = VendorRequests(db, w, policy: true);
        Assert.Equal(posted.Value!.CollectionId, (await policy.PostAsync(intent)).Value!.CollectionId);
        Assert.Equal("RequiresRegisteredVendor", (await policy.QuoteAsync(intent)).Error);
        Assert.False((await policy.PostAsync(intent with { ClientOperationId = Guid.NewGuid() })).IsSuccess);
        var vendor = (await Office(db, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), PhilippineTime.Today.Year,
            FishMeatVendorType.Fish, VendorRegistrationKind.New, "Registered vendor"))).Value!;
        var registered = intent with { ClientOperationId = Guid.NewGuid(), Charge = new(CollectorOperationCodes.FishMeatVendorFee, vendor.Id) };
        var quote = await policy.QuoteAsync(registered); Assert.True(quote.IsSuccess, quote.Error);
        Assert.Equal(123.45m, quote.Value!.Amount);
        Assert.True((await policy.PostAsync(registered with { ExpectedSourceVersion = quote.Value.SourceVersion })).IsSuccess);
        Assert.False((await policy.QuoteAsync(registered with { Charge = new(CollectorOperationCodes.FishMeatVendorFee, Guid.NewGuid()) })).IsSuccess);
        Assert.Equal(2, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Vendor_requests_are_tenant_scoped_and_default_off_without_schema_queries()
    {
        var w = await SeedAsync(); await using var db = VendorRequestContext(w.TenantId); await InstallVendorRequestCandidate(db); await EnableOffice(db, w);
        var request = new SubmitFishMeatRegistrationRequest(Guid.NewGuid(), "Tenant evidence", FishMeatVendorType.Meat, PhilippineTime.Today.Year);
        Assert.False((await VendorRequests(db, w, installed: false).SubmitVendorRequestAsync(request)).IsSuccess);
        var row = (await VendorRequests(db, w).SubmitVendorRequestAsync(request)).Value!;
        await using var other = VendorRequestContext(Guid.NewGuid());
        Assert.Empty(await other.FishMeatRegistrationRequests.ToListAsync());
        Assert.Equal(ResultStatus.NotFound, (await VendorRequests(other, w with { TenantId = other.CurrentMunicipalityId }, "SuperAdmin").VendorRequestAsync(row.Id)).Status);
        var foreignWorld = await SeedAsync(reset: false);
        await using var foreign = database.CreateContext(foreignWorld.TenantId);
        var foreignVendor = (await Office(foreign, foreignWorld, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), PhilippineTime.Today.Year,
            FishMeatVendorType.Meat, VendorRegistrationKind.New, "Tenant evidence"))).Value!;
        Assert.Equal(ResultStatus.Conflict, (await VendorRequests(db, w, "SuperAdmin").ReviewVendorRequestAsync(row.Id,
            new(Guid.NewGuid(), 1, FishMeatRequestStatus.Approved, "Cross tenant attempt", foreignVendor.Id))).Status);
        Assert.False((await VendorRequests(db, w, policy: true).PostAsync(new(Guid.NewGuid(), PhilippineTime.Today, 10m,
            new(CollectorOperationCodes.FishMeatVendorFee, foreignVendor.Id), "Same name is not identity"))).IsSuccess);
        Assert.Equal(ResultStatus.Invalid, (await VendorRequests(db, w).SubmitVendorRequestAsync(request with {
            ClientOperationId = Guid.NewGuid(), TaxYear = PhilippineTime.Today.Year - 1 })).Status);
    }

    [SkippableFact]
    public async Task Vendor_request_completion_is_atomic_idempotent_and_rejects_stale_review()
    {
        var w = await SeedAsync(); await using var db = VendorRequestContext(w.TenantId);
        await InstallVendorRequestCandidate(db); await EnableOffice(db, w);
        var row = (await VendorRequests(db, w).SubmitVendorRequestAsync(new(Guid.NewGuid(), "Proposal",
            FishMeatVendorType.Meat, PhilippineTime.Today.Year))).Value!;
        var head = VendorRequests(db, w, "SuperAdmin");
        var request = new CompleteFishMeatRegistrationRequest(Guid.NewGuid(), 1, "Office verified identity and paper fields",
            new(Guid.NewGuid(), PhilippineTime.Today.Year, FishMeatVendorType.Meat, VendorRegistrationKind.New,
                "Completed office name", BusinessName: "Verified business", Address: "Office address"));
        Assert.False((await head.CompleteVendorRequestAsync(row.Id, request with { Reason = "" })).IsSuccess);
        Assert.Empty(await db.FishMeatVendorRegistrations.ToListAsync()); // Registration SaveChanges rolled back with the failed decision.
        var result = await head.CompleteVendorRequestAsync(row.Id, request); Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(result.Value!.RegistrationId, (await head.CompleteVendorRequestAsync(row.Id, request)).Value!.RegistrationId);
        Assert.Equal(ResultStatus.Conflict, (await head.CompleteVendorRequestAsync(row.Id, request with { ClientOperationId = Guid.NewGuid() })).Status);
        Assert.Single(await db.FishMeatVendorRegistrations.ToListAsync()); Assert.Empty(await db.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Vendor_registration_policy_discovery_closed_expired_and_tenant_boundaries()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var options = new FishMeatRegistrationOptions { RegistrationRequiredFrom = new Dictionary<Guid, DateOnly> { [w.TenantId] = PhilippineTime.Today } };
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId),
            new Clock(PhilippineTime.Today), null!, new NoMarketDays(), registrationOptions: options);
        var discovery = await sources.DiscoverNativeAsync(null, PhilippineTime.Today, default);
        Assert.DoesNotContain(discovery.Operations, x => x.OperationCode == CollectorOperationCodes.FishMeatVendorFee && x.CanAdd);
        var head = VendorRequests(db, w, "SuperAdmin"); var policy = VendorRequests(db, w, policy: true);
        var old = (await head.RegisterAsync(new(Guid.NewGuid(), PhilippineTime.Today.Year - 1, FishMeatVendorType.Fish,
            VendorRegistrationKind.New, "Old tax year"))).Value!;
        SourceNativeCollectionRequest Intent(Guid id) => new(Guid.NewGuid(), PhilippineTime.Today, 10m,
            new(CollectorOperationCodes.FishMeatVendorFee, id), "Typed bypass");
        Assert.False((await policy.PostAsync(Intent(old.Id))).IsSuccess);
        var current = (await head.RegisterAsync(new(Guid.NewGuid(), PhilippineTime.Today.Year, FishMeatVendorType.Fish,
            VendorRegistrationKind.New, "Current year"))).Value!;
        Assert.True((await head.CloseRegistrationAsync(current.Id, new(Guid.NewGuid()))).IsSuccess);
        Assert.Equal("RegistrationClosed", (await policy.QuoteAsync(Intent(current.Id))).Error);
        await using var other = database.CreateContext(Guid.NewGuid());
        Assert.Empty(await other.FishMeatVendorRegistrations.ToListAsync());
        Assert.False(options.RequiresRegistration(Guid.NewGuid(), PhilippineTime.Today));
        Assert.False(options.RequiresRegistration(w.TenantId, PhilippineTime.Today.AddDays(-1)));
        Assert.Empty(await db.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Vendor_registration_policy_rechecks_unsynced_sessions_and_replays_posted_sessions()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        CollectionSessionSources Sources(bool required) => new(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId),
            new Clock(PhilippineTime.Today), null!, new NoMarketDays(), registrationOptions: new FishMeatRegistrationOptions {
                RegistrationRequiredFrom = required ? new Dictionary<Guid, DateOnly> { [w.TenantId] = PhilippineTime.Today } : new Dictionary<Guid, DateOnly>() });
        var intent = new CollectionSessionIntent(Guid.NewGuid(), PhilippineTime.Today, null, [
            new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, 25m, Native: new(CollectorOperationCodes.FishMeatVendorFee))],
            PayerSnapshot: "Unregistered offline proposal");
        var old = NativeWorkflow(db, w, Sources(false));
        var posted = await Record(old, intent);
        var enforced = NativeWorkflow(db, w, Sources(true));
        var replay = await enforced.RecordAsync(new(intent, "Offline retry")); Assert.True(replay.IsSuccess, replay.Error);
        Assert.Equal(posted.Collections.Select(x => x.CollectionId), replay.Value!.Collections.Select(x => x.CollectionId));
        var unsynced = intent with { ClientCollectionSessionId = Guid.NewGuid(), Items = [
            new(Guid.NewGuid(), CollectionSessionItemKind.SourceNative, 25m, Native: new(CollectorOperationCodes.FishMeatVendorFee))] };
        var rejected = await enforced.QuoteAsync(unsynced);
        Assert.True(!rejected.IsSuccess || !rejected.Value!.CanRecord);
        Assert.Single(await db.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Vendor_request_concurrent_completion_creates_one_registration_and_no_cash()
    {
        var w = await SeedAsync(); Guid id;
        await using (var setup = VendorRequestContext(w.TenantId))
        {
            await InstallVendorRequestCandidate(setup); await EnableOffice(setup, w);
            id = (await VendorRequests(setup, w).SubmitVendorRequestAsync(new(Guid.NewGuid(), "Prospective",
                FishMeatVendorType.Fish, PhilippineTime.Today.Year))).Value!.Id;
        }
        var command = new CompleteFishMeatRegistrationRequest(Guid.NewGuid(), 1, "Office verification",
            new(Guid.NewGuid(), PhilippineTime.Today.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Reviewed identity"));
        await using var first = VendorRequestContext(w.TenantId); await using var second = VendorRequestContext(w.TenantId);
        var decisions = await Task.WhenAll(VendorRequests(first, w, "SuperAdmin").CompleteVendorRequestAsync(id, command),
            VendorRequests(second, w, "SuperAdmin").CompleteVendorRequestAsync(id, command with { ClientOperationId = Guid.NewGuid() }));
        Assert.Single(decisions, x => x.IsSuccess); Assert.Single(decisions, x => x.Status == ResultStatus.Conflict);
        Assert.Single(await first.FishMeatVendorRegistrations.ToListAsync()); Assert.Empty(await first.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Vendor_request_API_requires_authentication_and_head_review_role()
    {
        var w = await SeedAsync();
        await using var host = await CollectorApiHost.StartAsync(database, w.CollectorId, w.TenantId);
        var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(host.Client,
            $"/api/office-sources/fish-meat/registration-requests/{Guid.NewGuid()}/review",
            new ReviewFishMeatRegistrationRequest(Guid.NewGuid(), 1, FishMeatRequestStatus.Rejected, "Not authorized"));
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
            (await host.Client.GetAsync("/api/office-sources/fish-meat/registration-requests")).StatusCode);
    }
}
