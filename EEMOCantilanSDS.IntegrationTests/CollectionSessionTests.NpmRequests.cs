using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class CollectionSessionTests
{
    [SkippableFact]
    public async Task Npm_request_API_authentication_and_role_boundaries_do_not_post_collections()
    {
        var w = await SeedAsync(); Guid vendorId;
        await using (var seed = database.CreateContext(w.TenantId))
        {
            await EnableOffice(seed, w);
            vendorId = (await Office(seed, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), PhilippineTime.Today.Year,
                FishMeatVendorType.Fish, VendorRegistrationKind.New, "API request vendor"))).Value!.Id;
        }
        await using var host = await CollectorApiHost.StartAsync(database, w.CollectorId, w.TenantId);
        var row = await ApiPost<NpmRegistrationRequestDto>(host.Client, "/api/office-sources/npm-requests",
            new SubmitNpmRegistrationRequest(Guid.NewGuid(), vendorId, "F-1", null, "Head must verify office authorization."));
        var review = await host.Client.PostAsJsonAsync($"/api/office-sources/npm-requests/{row.Id}/review",
            new ReviewNpmRegistrationRequest(Guid.NewGuid(), 1, NpmRegistrationRequestStatus.Rejected, "Collector cannot review"), CollectorApiHost.Json);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, review.StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/office-sources/npm-requests")).StatusCode);
        await using var check = database.CreateContext(w.TenantId);
        Assert.Single(await check.NpmRegistrationRequests.ToListAsync()); Assert.Empty(await check.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Npm_concurrent_reviews_commit_one_decision_and_preserve_zero_cash()
    {
        var w = await SeedAsync(); Guid requestId;
        await using (var setup = database.CreateContext(w.TenantId))
        {
            await EnableOffice(setup, w);
            var vendor = (await Office(setup, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), PhilippineTime.Today.Year,
                FishMeatVendorType.Fish, VendorRegistrationKind.New, "Concurrent request"))).Value!;
            requestId = (await Office(setup, w).SubmitNpmRequestAsync(new(Guid.NewGuid(), vendor.Id, "F1", null, "Verify evidence"))).Value!.Id;
        }
        await using var first = database.CreateContext(w.TenantId); await using var second = database.CreateContext(w.TenantId);
        var results = await Task.WhenAll(
            Office(first, w, "SuperAdmin").ReviewNpmRequestAsync(requestId, new(Guid.NewGuid(), 1, NpmRegistrationRequestStatus.Returned, "More evidence")),
            Office(second, w, "SuperAdmin").ReviewNpmRequestAsync(requestId, new(Guid.NewGuid(), 1, NpmRegistrationRequestStatus.Rejected, "No authority")));
        Assert.Single(results, x => x.IsSuccess); Assert.Single(results, x => x.Status == ResultStatus.Conflict);
        await using var verify = database.CreateContext(w.TenantId);
        Assert.Equal(2, (await verify.NpmRegistrationRequests.SingleAsync()).Version);
        Assert.Empty(await verify.Collections.ToListAsync()); Assert.Empty(await verify.FishMeatNpmAssociations.ToListAsync());
    }

    [SkippableFact]
    public async Task Npm_registry_reuse_API_reuses_canonical_writer_and_rolls_back_invalid_or_duplicate_tenancy()
    {
        var w = await SeedAsync();
        Guid vendorId;
        await using (var seed = database.CreateContext(w.TenantId))
        {
            vendorId = (await Office(seed, w, "SuperAdmin").RegisterAsync(new(Guid.NewGuid(), PhilippineTime.Today.Year,
                FishMeatVendorType.Fish, VendorRegistrationKind.New, "Verified registry identity"))).Value!.Id;
        }
        var fault = new CollectorApiHost.Fault();
        await using var host = await CollectorApiHost.StartAsync(database, w.CollectorId, w.TenantId, fault, role: "SuperAdmin");
        var tenancy = new EEMOCantilanSDS.Application.Command.Stalls.CreateStall.CreateStallCommand(FacilityCode.NPM,
            "REUSE-12", 900m, ApplicableFees.BaseRental, MarketSection.FishSection, null, null, null, 30m,
            "Verified registry identity", null, PhilippineTime.Today.ToDateTime(TimeOnly.MinValue), 3);
        var request = new CreateNpmVendorOccupancyRequest(Guid.NewGuid(), vendorId, "Verified registry identity",
            "Office verified tenancy and duplicates", tenancy, true);
        var result = await ApiPost<NpmVendorOccupancyResult>(host.Client, "/api/office-sources/fish-meat/npm-occupancies", request);
        Assert.False(result.Replayed);
        var replay = await ApiPost<NpmVendorOccupancyResult>(host.Client, "/api/office-sources/fish-meat/npm-occupancies", request);
        Assert.True(replay.Replayed); Assert.Equal(result.ContractId, replay.ContractId);
        var duplicate = await host.Client.PostAsJsonAsync("/api/office-sources/fish-meat/npm-occupancies", request with { ClientOperationId = Guid.NewGuid() }, CollectorApiHost.Json);
        Assert.False(duplicate.IsSuccessStatusCode);
        var invalid = await host.Client.PostAsJsonAsync("/api/office-sources/fish-meat/npm-occupancies", request with {
            ClientOperationId = Guid.NewGuid(), Occupancy = tenancy with { StallNo = "INVALID-12", ContractYears = 0 } }, CollectorApiHost.Json);
        Assert.False(invalid.IsSuccessStatusCode);
        // Inject failure AFTER the canonical writer's SaveChanges: the outer transaction must roll back.
        fault.FailAfterStallNumber = "ROLLBACK-12";
        var retryable = request with { ClientOperationId = Guid.NewGuid(), Occupancy = tenancy with { StallNo = "ROLLBACK-12" } };
        var failed = await host.Client.PostAsJsonAsync("/api/office-sources/fish-meat/npm-occupancies", retryable, CollectorApiHost.Json);
        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, failed.StatusCode);
        await using var check = database.CreateContext(w.TenantId);
        Assert.Equal(2, await check.Stalls.CountAsync()); Assert.Equal(2, await check.Contracts.CountAsync());
        Assert.Single(await check.FishMeatVendorRegistrations.ToListAsync()); Assert.Single(await check.FishMeatNpmAssociations.ToListAsync());
        Assert.Empty(await check.Collections.ToListAsync());
        fault.FailAfterStallNumber = null;
        var retry = await ApiPost<NpmVendorOccupancyResult>(host.Client, "/api/office-sources/fish-meat/npm-occupancies", retryable);
        Assert.False(retry.Replayed);
        Assert.Equal(3, await check.Stalls.CountAsync()); Assert.Equal(2, await check.FishMeatNpmAssociations.CountAsync());
    }

    [SkippableFact]
    public async Task Npm_request_submission_replay_return_and_resubmission_never_post_cash()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        await EnableOffice(db, w);
        var head = Office(db, w, "SuperAdmin"); var collector = Office(db, w);
        var vendor = (await head.RegisterAsync(new(Guid.NewGuid(), PhilippineTime.Today.Year,
            FishMeatVendorType.Fish, VendorRegistrationKind.New, "Registered independent vendor"))).Value!;
        var stalls = await db.Stalls.CountAsync(); var terms = await db.Contracts.CountAsync();
        var command = new SubmitNpmRegistrationRequest(Guid.NewGuid(), vendor.Id, "F-12", MarketSection.FishSection, "Office must verify tenancy evidence.");
        var submitted = await collector.SubmitNpmRequestAsync(command);
        Assert.True(submitted.IsSuccess, submitted.Error);
        var row = submitted.Value!;
        Assert.Equal(NpmRegistrationRequestStatus.Submitted, row.Status);
        Assert.Equal(row.Id, (await collector.SubmitNpmRequestAsync(command)).Value!.Id);
        Assert.Equal(ResultStatus.Conflict, (await collector.SubmitNpmRequestAsync(command with { Note = "changed" })).Status);
        Assert.Equal(ResultStatus.Conflict, (await collector.SubmitNpmRequestAsync(command with { ClientOperationId = Guid.NewGuid() })).Status);
        var returned = new ReviewNpmRegistrationRequest(Guid.NewGuid(), row.Version, NpmRegistrationRequestStatus.Returned, "Provide the actual stall authorization.");
        Assert.True((await head.ReviewNpmRequestAsync(row.Id, returned)).IsSuccess);
        Assert.True((await head.ReviewNpmRequestAsync(row.Id, returned)).IsSuccess);
        Assert.Equal(ResultStatus.Conflict, (await head.ReviewNpmRequestAsync(row.Id, returned with { Reason = "different" })).Status);
        var replacement = await collector.SubmitNpmRequestAsync(command with { ClientOperationId = Guid.NewGuid(), ReplacesRequestId = row.Id, Note = "Office reference supplied." });
        Assert.True(replacement.IsSuccess, replacement.Error);
        Assert.Equal(row.Id, replacement.Value!.ReplacesRequestId);
        Assert.Equal(2, (await collector.NpmRequestsAsync()).Value!.Total);
        Assert.Equal(stalls, await db.Stalls.CountAsync()); Assert.Equal(terms, await db.Contracts.CountAsync());
        Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.CollectionAllocations.ToListAsync());
        Assert.Empty(await db.ObligationPeriods.ToListAsync()); Assert.Single(await db.FishMeatVendorRegistrations.ToListAsync());
    }

    [SkippableFact]
    public async Task Npm_request_approval_requires_verified_current_tenancy_and_is_idempotent_without_collection()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        await EnableOffice(db, w);
        var head = Office(db, w, "SuperAdmin");
        var vendor = (await head.RegisterAsync(new(Guid.NewGuid(), PhilippineTime.Today.Year,
            FishMeatVendorType.Fish, VendorRegistrationKind.New, "Independent vendor"))).Value!;
        var row = (await Office(db, w).SubmitNpmRequestAsync(new(Guid.NewGuid(), vendor.Id, "F-12", MarketSection.FishSection, "Verify occupancy"))).Value!;
        var decision = new ReviewNpmRegistrationRequest(Guid.NewGuid(), row.Version, NpmRegistrationRequestStatus.Approved, "Verified tenancy and source association.");
        Assert.Equal(ResultStatus.Conflict, (await head.ReviewNpmRequestAsync(row.Id, decision)).Status);
        var source = Assert.Single((await head.NpmVendorCandidatesAsync(PhilippineTime.Today.Year)).Value!.Rows);
        var link = await head.LinkNpmVendorAsync(vendor.Id, Confirmation(source)); Assert.True(link.IsSuccess, link.Error);
        decision = decision with { AssociationId = link.Value!.Id };
        foreach (var role in new[] { "Admin", "Collector" })
            Assert.Equal(ResultStatus.Forbidden, (await Office(db, w, role).ReviewNpmRequestAsync(row.Id, decision)).Status);
        var approved = await head.ReviewNpmRequestAsync(row.Id, decision); Assert.True(approved.IsSuccess, approved.Error);
        Assert.Equal(NpmRegistrationRequestStatus.Approved, approved.Value!.Status);
        Assert.Equal(2, approved.Value.Version);
        Assert.Equal(approved.Value.Id, (await head.ReviewNpmRequestAsync(row.Id, decision)).Value!.Id);
        Assert.Equal(ResultStatus.Conflict, (await head.ReviewNpmRequestAsync(row.Id, decision with { ClientOperationId = Guid.NewGuid() })).Status);
        Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.CollectionAllocations.ToListAsync());
    }

    [SkippableFact]
    public async Task Npm_requests_are_tenant_scoped_and_rejection_preserves_history_without_tenancy()
    {
        var w = await SeedAsync(); var other = await SeedAsync(reset: false);
        await using var db = database.CreateContext(w.TenantId); await EnableOffice(db, w);
        var head = Office(db, w, "SuperAdmin");
        var vendor = (await head.RegisterAsync(new(Guid.NewGuid(), PhilippineTime.Today.Year,
            FishMeatVendorType.Fish, VendorRegistrationKind.New, "Same name"))).Value!;
        var command = new SubmitNpmRegistrationRequest(Guid.NewGuid(), vendor.Id, "A1", null, "Reference pending");
        var row = (await Office(db, w).SubmitNpmRequestAsync(command)).Value!;
        await using var foreign = database.CreateContext(other.TenantId); await EnableOffice(foreign, other);
        Assert.Equal(ResultStatus.NotFound, (await Office(foreign, other).SubmitNpmRequestAsync(command)).Status);
        Assert.Empty((await Office(foreign, other, "SuperAdmin").NpmRequestsAsync()).Value!.Rows);
        var rejected = new ReviewNpmRegistrationRequest(Guid.NewGuid(), 1, NpmRegistrationRequestStatus.Rejected, "No verified tenancy authorization.");
        Assert.Equal(ResultStatus.NotFound, (await Office(foreign, other, "SuperAdmin").ReviewNpmRequestAsync(row.Id, rejected)).Status);
        Assert.True((await head.ReviewNpmRequestAsync(row.Id, rejected)).IsSuccess);
        Assert.Equal(NpmRegistrationRequestStatus.Rejected, (await head.NpmRequestsAsync()).Value!.Rows.Single().Status);
        Assert.Equal(ResultStatus.Invalid, (await head.NpmRequestsAsync(pageSize: 101)).Status);
        Assert.Empty((await head.NpmRequestsAsync(page: 2, pageSize: 1)).Value!.Rows);
        Assert.Empty(await db.FishMeatNpmAssociations.ToListAsync()); Assert.Empty(await db.Collections.ToListAsync());
    }
}
