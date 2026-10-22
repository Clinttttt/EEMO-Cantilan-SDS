using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;
public sealed partial class CollectionSessionTests
{
    private static ConfirmNpmVendorAssociationRequest Confirmation(NpmVendorCandidate c, Guid? operation = null) =>
        new(operation ?? Guid.NewGuid(), c.ContractId, c.StallId, c.SourceVersion, "Office checked occupant and annual registration");

    [SkippableFact]
    public async Task Fish_meat_import_previews_existing_npm_contract_without_automatic_association()
    {
        var w = await SeedAsync(section: MarketSection.FishSection);
        await using var db = database.CreateContext(w.TenantId);
        var head = Office(db, w, "SuperAdmin");
        var year = PhilippineTime.Today.Year;
        var source = Assert.Single((await head.NpmVendorCandidatesAsync(year)).Value!.Rows);
        var request = new VendorRegistryImportRequest([new(1,
            new(Guid.NewGuid(), year, FishMeatVendorType.Fish, VendorRegistrationKind.New,
                $"  {source.Occupant}  "))]);
        var preview = Assert.Single((await head.PreviewImportAsync(request)).Value!.Rows);
        var candidate = Assert.Single(preview.PossibleNpmOccupancies!);
        Assert.Equal(source.ContractId, candidate.ContractId);
        Assert.Equal(source.StallId, candidate.StallId);
        Assert.False(candidate.AlreadyLinked);
        Assert.Empty(await db.FishMeatNpmAssociations.ToListAsync());
        var saved = await head.SaveImportAsync(request);
        Assert.True(saved.IsSuccess, saved.Error);
        Assert.Single(saved.Value!.Rows);
        Assert.Empty(await db.FishMeatNpmAssociations.ToListAsync());
        Assert.Empty(await db.Collections.ToListAsync());
    }

    [SkippableTheory]
    [InlineData(MarketSection.FishSection, FishMeatVendorType.Fish)]
    [InlineData(MarketSection.MeatSection, FishMeatVendorType.Meat)]
    public async Task Npm_linked_registration_is_atomic_replay_safe_and_has_no_cash_side_effects(MarketSection area, FishMeatVendorType type)
    {
        var w = await SeedAsync(section: area); await using var db = database.CreateContext(w.TenantId);
        var head = Office(db, w, "SuperAdmin"); var year = PhilippineTime.Today.Year;
        var source = Assert.Single((await head.NpmVendorCandidatesAsync(year)).Value!.Rows);
        Assert.Equal(type, source.SuggestedType);
        var beforeCollections = await db.Collections.CountAsync(); var beforePeriods = await db.ObligationPeriods.CountAsync();
        var op = Guid.NewGuid();
        var request = new RegisterFishMeatVendorRequest(op, year, type, VendorRegistrationKind.New, source.Occupant,
            NpmAssociation: Confirmation(source, op));
        var saved = await head.RegisterAsync(request); Assert.True(saved.IsSuccess, saved.Error);
        var replay = await head.RegisterAsync(request); Assert.Equal(saved.Value!.Id, replay.Value!.Id);
        Assert.Equal("RegistrationIntentConflict", (await head.RegisterAsync(request with { DisplayName = "Different" })).Error);
        Assert.Single(await db.FishMeatVendorRegistrations.ToListAsync());
        var link = Assert.Single(await db.FishMeatNpmAssociations.ToListAsync()); Assert.Equal(source.ContractId, link.ContractId);
        Assert.Equal(source.Occupant, link.OccupantSnapshot); Assert.NotEmpty(link.ConfirmedBy);
        Assert.Equal(0, (await head.NpmVendorCandidatesAsync(year)).Value!.UnresolvedCount);
        Assert.Equal(beforeCollections, await db.Collections.CountAsync()); Assert.Equal(beforePeriods, await db.ObligationPeriods.CountAsync());
        Assert.Empty(await db.CollectionAllocations.ToListAsync());
        Assert.Equal("RegistrationIntentConflict", (await head.RegisterAsync(request with { ClientOperationId = Guid.NewGuid(), NpmAssociation = Confirmation(source) })).Error);
    }

    [SkippableTheory]
    [InlineData("Admin")]
    [InlineData("Collector")]
    public async Task Npm_links_reject_non_head_writers(string role)
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var head = Office(db, w, "SuperAdmin"); var year = PhilippineTime.Today.Year;
        var source = Assert.Single((await head.NpmVendorCandidatesAsync(year)).Value!.Rows);
        var vendor = (await head.RegisterAsync(new(Guid.NewGuid(), year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Independent"))).Value!;
        Assert.Equal(ResultStatus.Forbidden, (await Office(db, w, role).LinkNpmVendorAsync(vendor.Id, Confirmation(source))).Status);
        var op = Guid.NewGuid();
        Assert.Equal(ResultStatus.Forbidden, (await Office(db, w, role).RegisterAsync(new(op, year, FishMeatVendorType.Fish,
            VendorRegistrationKind.New, "Independent", NpmAssociation: Confirmation(source, op)))).Status);
        Assert.Empty(await db.FishMeatNpmAssociations.ToListAsync());
    }

    [SkippableFact]
    public async Task Npm_existing_registration_link_correction_and_renewal_preserve_independent_identity()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var head = Office(db, w, "SuperAdmin"); var year = PhilippineTime.Today.Year;
        var source = Assert.Single((await head.NpmVendorCandidatesAsync(year)).Value!.Rows);
        var vendor = (await head.RegisterAsync(new(Guid.NewGuid(), year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Independent"))).Value!;
        var request = Confirmation(source);
        var result = await head.LinkNpmVendorAsync(vendor.Id, request); Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(result.Value!.Id, (await head.LinkNpmVendorAsync(vendor.Id, request)).Value!.Id);
        Assert.Equal("RegistrationIntentConflict", (await head.LinkNpmVendorAsync(vendor.Id, request with { ConfirmationNote = "different" })).Error);
        Assert.Equal("AssociationAlreadyExists", (await head.LinkNpmVendorAsync(vendor.Id, request with { ClientOperationId = Guid.NewGuid() })).Error);
        var renew = await head.RenewRegistrationAsync(vendor.Id, new(Guid.NewGuid(), year + 1)); Assert.True(renew.IsSuccess, renew.Error);
        Assert.Empty((await head.NpmVendorLinksAsync(renew.Value!.Registration.Id)).Value!);
        Assert.Equal(1, (await head.NpmVendorCandidatesAsync(year + 1)).Value!.UnresolvedCount);
        var correction = new CorrectNpmVendorAssociationRequest(Guid.NewGuid(), "Office verified incorrect association");
        var corrected = await head.CorrectNpmVendorLinkAsync(result.Value.Id, correction); Assert.True(corrected.IsSuccess, corrected.Error);
        Assert.Equal(corrected.Value!.CorrectedAtUtc, (await head.CorrectNpmVendorLinkAsync(result.Value.Id, correction)).Value!.CorrectedAtUtc);
        Assert.Equal(result.Value.ConfirmedBy, corrected.Value.ConfirmedBy); Assert.Equal(result.Value.OccupantSnapshot, corrected.Value.OccupantSnapshot);
        Assert.Equal(1, (await head.NpmVendorCandidatesAsync(year)).Value!.UnresolvedCount);
        Assert.True((await head.LinkNpmVendorAsync(vendor.Id, request with { ClientOperationId = Guid.NewGuid() })).IsSuccess);
        Assert.Equal(2, await db.FishMeatNpmAssociations.CountAsync());
        Assert.Empty(await db.Collections.ToListAsync());
    }

    [SkippableTheory]
    [InlineData(MarketSection.VegetableArea)]
    [InlineData(null)]
    public async Task Npm_other_areas_require_explicit_exception_never_auto_type(MarketSection? section)
    {
        var w = await SeedAsync(section: section ?? MarketSection.VegetableArea); await using var db = database.CreateContext(w.TenantId);
        if (section is null) await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Stalls\" SET \"Section\" = NULL, \"CustomSectionName\" = 'Extension' WHERE \"Id\" = {w.StallId}");
        var head = Office(db, w, "SuperAdmin"); var year = PhilippineTime.Today.Year;
        Assert.Empty((await head.NpmVendorCandidatesAsync(year)).Value!.Rows);
        var source = Assert.Single((await head.NpmVendorCandidatesAsync(year, exceptions: true)).Value!.Rows); Assert.Null(source.SuggestedType);
        Assert.Equal(0, (await head.NpmVendorCandidatesAsync(year, exceptions: true)).Value!.UnresolvedCount);
        var vendor = (await head.RegisterAsync(new(Guid.NewGuid(), year, FishMeatVendorType.Meat, VendorRegistrationKind.New, "Verified exception"))).Value!;
        Assert.Equal("RequiresExceptionConfirmation", (await head.LinkNpmVendorAsync(vendor.Id, Confirmation(source))).Error);
        Assert.True((await head.LinkNpmVendorAsync(vendor.Id, Confirmation(source) with { ExceptionConfirmed = true })).IsSuccess);
    }

    [SkippableFact]
    public async Task Npm_name_matches_are_warnings_not_identity_and_multiple_stalls_are_valid()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var head = Office(db, w, "SuperAdmin"); var year = PhilippineTime.Today.Year;
        var source = Assert.Single((await head.NpmVendorCandidatesAsync(year)).Value!.Rows);
        var old = (await head.RegisterAsync(new(Guid.NewGuid(), year, FishMeatVendorType.Fish, VendorRegistrationKind.New, source.Occupant))).Value!;
        source = Assert.Single((await head.NpmVendorCandidatesAsync(year)).Value!.Rows);
        Assert.Single(source.PossibleMatches); Assert.Empty(source.Links); Assert.Equal(1, (await head.NpmVendorCandidatesAsync(year)).Value!.UnresolvedCount);
        var op = Guid.NewGuid(); var request = new RegisterFishMeatVendorRequest(op, year, FishMeatVendorType.Fish, VendorRegistrationKind.New, source.Occupant,
            NpmAssociation: Confirmation(source, op));
        Assert.Equal("RequiresDuplicateReview", (await head.RegisterAsync(request)).Error);
        var saved = await head.RegisterAsync(request with { NpmAssociation = request.NpmAssociation! with { DuplicateReviewConfirmed = true } });
        Assert.True(saved.IsSuccess, saved.Error); Assert.NotEqual(old.Id, saved.Value!.Id);
        var originalStall = await db.Stalls.SingleAsync(x => x.Id == w.StallId);
        var stall = Stall.Create(originalStall.FacilityId, "FISH-2", 900m, ApplicableFees.BaseRental, MarketSection.FishSection, createdBy: "head", municipalityId: w.TenantId);
        var contract = Contract.Create(stall.Id, source.Occupant, source.Occupant, PhilippineTime.Today, 3, 900m, createdBy: "head");
        db.AddRange(stall, contract); await db.SaveChangesAsync();
        var second = (await head.NpmVendorCandidatesAsync(year)).Value!.Rows.Single(x => x.StallId == stall.Id);
        Assert.True((await head.LinkNpmVendorAsync(saved.Value.Id, Confirmation(second))).IsSuccess);
        Assert.Equal(2, (await head.NpmVendorLinksAsync(saved.Value.Id)).Value!.Count);
    }

    [SkippableFact]
    public async Task Npm_source_ids_tenant_and_stale_occupancy_are_server_validated()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var head = Office(db, w, "SuperAdmin"); var year = PhilippineTime.Today.Year;
        var source = Assert.Single((await head.NpmVendorCandidatesAsync(year)).Value!.Rows);
        var vendor = (await head.RegisterAsync(new(Guid.NewGuid(), year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Independent"))).Value!;
        Assert.Equal("InvalidNpmOccupancy", (await head.LinkNpmVendorAsync(vendor.Id, Confirmation(source) with { StallId = Guid.NewGuid() })).Error);
        Assert.Equal("NpmOccupancyChanged", (await head.LinkNpmVendorAsync(vendor.Id, Confirmation(source) with { SourceVersion = "stale" })).Error);
        Assert.Equal(ResultStatus.NotFound, (await head.LinkNpmVendorAsync(Guid.NewGuid(), Confirmation(source))).Status);
        await using var wrongTenant = database.CreateContext(Guid.NewGuid());
        Assert.Empty(await wrongTenant.FishMeatVendorRegistrations.ToListAsync()); Assert.Empty(await wrongTenant.FishMeatNpmAssociations.ToListAsync());
        var other = w with { TenantId = Guid.NewGuid() };
        Assert.Equal(ResultStatus.NotFound, (await Office(wrongTenant, other, "SuperAdmin").LinkNpmVendorAsync(vendor.Id, Confirmation(source))).Status);
        var malformed = FishMeatNpmAssociation.Confirm(w.TenantId, vendor.Id, source.ContractId, Guid.NewGuid(), Guid.NewGuid(), "fingerprint", source.Occupant, source.SourceVersion, "confirmed", "head");
        db.Add(malformed); await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        Assert.Empty(await db.FishMeatNpmAssociations.ToListAsync());
    }

    [SkippableFact]
    public async Task Npm_link_concurrent_requests_have_one_active_exact_pair()
    {
        var w = await SeedAsync(); NpmVendorCandidate source; Guid vendor;
        await using (var setup = database.CreateContext(w.TenantId))
        {
            var head = Office(setup, w, "SuperAdmin"); source = Assert.Single((await head.NpmVendorCandidatesAsync(PhilippineTime.Today.Year)).Value!.Rows);
            vendor = (await head.RegisterAsync(new(Guid.NewGuid(), PhilippineTime.Today.Year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Independent"))).Value!.Id;
        }
        await using var left = database.CreateContext(w.TenantId); await using var right = database.CreateContext(w.TenantId);
        var request = Confirmation(source);
        var results = await Task.WhenAll(Office(left, w, "SuperAdmin").LinkNpmVendorAsync(vendor, request), Office(right, w, "SuperAdmin").LinkNpmVendorAsync(vendor, request));
        Assert.Contains(results, x => x.IsSuccess);
        await using var verify = database.CreateContext(w.TenantId); Assert.Single(await verify.FishMeatNpmAssociations.ToListAsync());
        var replay = await Office(verify, w, "SuperAdmin").LinkNpmVendorAsync(vendor, request); Assert.True(replay.IsSuccess, replay.Error);
        Assert.Empty(await verify.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Npm_historical_contract_and_replacement_never_transfer_link_or_lifecycle()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var head = Office(db, w, "SuperAdmin"); var year = PhilippineTime.Today.Year;
        var original = Assert.Single((await head.NpmVendorCandidatesAsync(year)).Value!.Rows);
        var vendor = (await head.RegisterAsync(new(Guid.NewGuid(), year, FishMeatVendorType.Fish, VendorRegistrationKind.New, original.Occupant))).Value!;
        Assert.True((await head.LinkNpmVendorAsync(vendor.Id, Confirmation(original))).IsSuccess);
        var contract = await db.Contracts.SingleAsync(x => x.Id == original.ContractId); contract.Terminate("head", PhilippineTime.Today);
        var successor = Contract.Create(w.StallId, "Replacement occupant", "Replacement occupant", PhilippineTime.Today, 3, 900m, createdBy: "head");
        db.Add(successor); await db.SaveChangesAsync();
        var current = Assert.Single((await head.NpmVendorCandidatesAsync(year)).Value!.Rows); Assert.Equal(successor.Id, current.ContractId); Assert.Empty(current.Links);
        var historical = (await head.NpmVendorCandidatesAsync(year, true)).Value!.Rows.Single(x => x.ContractId == original.ContractId);
        Assert.False(historical.Active); Assert.Single(historical.Links);
        Assert.Equal("NpmOccupancyChanged", (await head.LinkNpmVendorAsync(vendor.Id, Confirmation(original))).Error);
        Assert.Equal(VendorRegistrationStatus.Active, (await db.FishMeatVendorRegistrations.SingleAsync(x => x.Id == vendor.Id)).Status);
        var another = (await head.RegisterAsync(new(Guid.NewGuid(), year, FishMeatVendorType.Fish, VendorRegistrationKind.New, "Historical business"))).Value!;
        Assert.Equal("RequiresExceptionConfirmation", (await head.LinkNpmVendorAsync(another.Id, Confirmation(historical))).Error);
        Assert.True((await head.LinkNpmVendorAsync(another.Id, Confirmation(historical) with { ExceptionConfirmed = true })).IsSuccess);
        Assert.True((await head.CloseRegistrationAsync(vendor.Id, new(Guid.NewGuid(), "Independent annual closure"))).IsSuccess);
        Assert.True((await db.Contracts.SingleAsync(x => x.Id == successor.Id)).IsActive);
        Assert.Empty(await db.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task Npm_linked_creation_race_and_invalid_intent_leave_no_orphan_registration()
    {
        var w = await SeedAsync(); NpmVendorCandidate source;
        await using (var setup = database.CreateContext(w.TenantId)) source = Assert.Single((await Office(setup, w, "SuperAdmin").NpmVendorCandidatesAsync(PhilippineTime.Today.Year)).Value!.Rows);
        var op = Guid.NewGuid(); var request = new RegisterFishMeatVendorRequest(op, PhilippineTime.Today.Year, FishMeatVendorType.Fish,
            VendorRegistrationKind.New, source.Occupant, NpmAssociation: Confirmation(source, op));
        await using var left = database.CreateContext(w.TenantId); await using var right = database.CreateContext(w.TenantId);
        var results = await Task.WhenAll(Office(left, w, "SuperAdmin").RegisterAsync(request), Office(right, w, "SuperAdmin").RegisterAsync(request));
        Assert.Contains(results, x => x.IsSuccess);
        await using var verify = database.CreateContext(w.TenantId); var head = Office(verify, w, "SuperAdmin");
        Assert.Single(await verify.FishMeatVendorRegistrations.ToListAsync()); Assert.Single(await verify.FishMeatNpmAssociations.ToListAsync());
        Assert.True((await head.RegisterAsync(request)).IsSuccess);
        Assert.Equal("RegistrationIntentConflict", (await head.RegisterAsync(request with { NpmAssociation = null })).Error);
        var invalidOp = Guid.NewGuid();
        Assert.Equal("InvalidNpmOccupancy", (await head.RegisterAsync(request with { ClientOperationId = invalidOp, NpmAssociation = Confirmation(source, invalidOp) with { ContractId = Guid.NewGuid() } })).Error);
        Assert.Single(await verify.FishMeatVendorRegistrations.ToListAsync()); Assert.Single(await verify.FishMeatNpmAssociations.ToListAsync());
        Assert.Empty(await verify.Collections.ToListAsync());
    }
}
