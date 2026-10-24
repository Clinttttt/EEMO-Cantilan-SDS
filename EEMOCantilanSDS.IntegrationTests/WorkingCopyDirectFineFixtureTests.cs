using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// The synthetic direct-fine authority for the working copy: how it is gated, and that the Office-origin SRCs it enables are genuine canonical
/// cash that flows through Office cash turnover, void, and the existing accountability, on a disposable PostgreSQL database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class WorkingCopyDirectFineFixtureTests(PostgresFixture db)
{
    private sealed class Actor(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role.ToLowerInvariant();
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "wcfix";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Clock(DateOnly today) : IClock
    {
        public DateOnly PhilippineToday => today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => DateTime.SpecifyKind(today.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
    }

    private static readonly DateOnly Today = PhilippineTime.Today;
    private sealed record World(Guid TenantId, Guid HeadId, string Connection, string Database, int Port);

    private async Task<World> SeedAsync()
    {
        await db.ResetAsync();
        var municipality = Municipality.Create($"WCF-{Guid.NewGuid():N}"[..20], "Working copy fixture test", "Province", MunicipalityStatus.Active,
            tenantCode: $"wcf-{Guid.NewGuid():N}"[..28]);
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Municipalities.Add(municipality); await setup.SaveChangesAsync(); }
        await using var context = db.CreateContext(municipality.Id);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.PenaltiesAndFines, municipality.Id);
        context.AddRange(classification,
            RevenueClassificationPolicy.Create(classification.Id, Today.AddYears(-1), "Fines", RevenueInstrumentType.OfficialReceipt, municipality.Id),
            PenaltyDefinition.Create(municipality.Id, "LATE_PAYMENT", Today.AddYears(-1), "Fines", null, GovernedServiceBasis.DirectApprovedAmount, null, null, true, "fixture"),
            PenaltyDefinition.Create(municipality.Id, "RETIRED", Today.AddYears(-1), "Retired fines", null, GovernedServiceBasis.DirectApprovedAmount, null, null, false, "fixture"));
        await context.SaveChangesAsync();
        var b = new Npgsql.NpgsqlConnectionStringBuilder(db.ApiConnectionString);
        return new(municipality.Id, Guid.NewGuid(), db.ApiConnectionString, b.Database!, b.Port);
    }

    private DirectFineCollectionWorkflow Direct(AppDbContext ctx, World w, string role = "SuperAdmin", DateOnly? today = null) =>
        new(ctx, new Actor(w.HeadId, w.TenantId, role), new Tenant(w.TenantId), new Clock(today ?? Today));

    private OfficeCashTurnoverWorkflow Turnover(AppDbContext ctx, World w, string role = "SuperAdmin") =>
        new(ctx, new Actor(w.HeadId, w.TenantId, role), new Tenant(w.TenantId), new OfficeCashTurnoverOptions(true), new Clock(Today));

    private static DirectFineCollectionRequest Fine(Guid authority, decimal amount, string? payer = "SYNTHETIC OFFICE-TURNOVER TEST", string? reason = "Synthetic acceptance test",
        string? reference = "SYNTH-FINE-001") => new(Guid.NewGuid(), authority, Today, amount, reason, reference, payer);

    [SkippableFact]
    public async Task The_fixture_refuses_the_preview_the_municipal_database_unknown_tenants_and_unusable_penalties_without_writing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var w = await SeedAsync();
        await using var context = db.CreateContext(w.TenantId);
        Task<WorkingCopyDirectFineFixture.Outcome> Ensure(string database, int port, Guid? tenant = null, string code = "LATE_PAYMENT") =>
            WorkingCopyDirectFineFixture.EnsureAsync(context, w.Connection, database, port, tenant ?? w.TenantId, code, Today);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure("stalltrack_preview", w.Port));          // the accepted preview, by name
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(w.Database, 55489));                      // ...and by port
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure("EEMOCantilanSDS.DB", w.Port));           // the retained municipal database
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(w.Database, 5432));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure("some_other_database", w.Port));          // not the session's database
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(w.Database, w.Port, tenant: Guid.NewGuid())); // unknown tenant
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(w.Database, w.Port, code: "NO_SUCH_CATEGORY"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Ensure(w.Database, w.Port, code: "RETIRED"));    // inactive
        Assert.Empty(await context.DirectFineAuthorities.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.Collections.AsNoTracking().ToArrayAsync());
    }

    [SkippableFact]
    public async Task The_fixture_writes_one_labelled_time_limited_authority_idempotently_and_nothing_else()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var w = await SeedAsync();
        await using var context = db.CreateContext(w.TenantId);

        var first = await WorkingCopyDirectFineFixture.EnsureAsync(context, w.Connection, w.Database, w.Port, w.TenantId, "LATE_PAYMENT", Today);
        var again = await WorkingCopyDirectFineFixture.EnsureAsync(context, w.Connection, w.Database, w.Port, w.TenantId, "LATE_PAYMENT", Today);
        Assert.True(first.Created); Assert.False(again.Created); Assert.Equal(first.AuthorityId, again.AuthorityId);

        var authority = await context.DirectFineAuthorities.AsNoTracking().SingleAsync();
        Assert.StartsWith(WorkingCopyDirectFineFixture.Marker, authority.GoverningReference);
        Assert.StartsWith(WorkingCopyDirectFineFixture.Marker, authority.ApprovalReference);
        Assert.Equal((true, true, false, false), (authority.AllowsUncappedAmount, authority.RequiresPayerParticulars, authority.RequiresReason, authority.RequiresSupportingReference));
        Assert.Equal((Today, Today.AddDays(13), WorkingCopyDirectFineFixture.FixtureActor), (authority.EffectiveFrom, authority.EffectiveTo, authority.VerifiedBy));
        Assert.Empty(await context.Collections.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.CollectionLines.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.PostingOperations.AsNoTracking().ToArrayAsync());

        // Only the Head sees it, and it lapses by itself.
        var available = (await Direct(context, w).OptionsAsync()).Value!.Categories;
        Assert.Equal(Guid.Empty, available[0].AuthorityId);
        var category = Assert.Single(available.Where(x => x.AuthorityId == first.AuthorityId));
        Assert.Equal((true, false, false), (category.RequiresPayerParticulars, category.RequiresReason, category.RequiresSupportingReference));
        Assert.Equal(ResultStatus.Forbidden, (await Direct(context, w, "Admin").OptionsAsync()).Status);
        Assert.Equal(ResultStatus.Forbidden, (await Direct(context, w, "Collector").OptionsAsync()).Status);
        var afterExpiry = (await Direct(context, w, today: Today.AddDays(14)).OptionsAsync()).Value!.Categories;
        Assert.Equal(Guid.Empty, Assert.Single(afterExpiry).AuthorityId);
        Assert.DoesNotContain(afterExpiry, c => c.AuthorityId == first.AuthorityId);
    }

    [SkippableFact]
    public async Task Two_synthetic_SRCs_are_Office_origin_canonical_fines_that_flow_through_turnover_void_and_accountability()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var w = await SeedAsync();
        await using var context = db.CreateContext(w.TenantId);
        var authority = (await WorkingCopyDirectFineFixture.EnsureAsync(context, w.Connection, w.Database, w.Port, w.TenantId, "LATE_PAYMENT", Today)).AuthorityId;

        // The payer particulars (the only evidence this authority requires) are enforced by the normal workflow, not by the fixture; the Office requires no reason or reference.
        Assert.False((await Direct(context, w).PostAsync(Fine(authority, 120m, payer: null))).IsSuccess);
        Assert.False((await Direct(context, w, "Admin").PostAsync(Fine(authority, 120m))).IsSuccess);
        Assert.Empty(await context.Collections.AsNoTracking().ToArrayAsync());

        var a = await Direct(context, w).PostAsync(Fine(authority, 120m)); Assert.True(a.IsSuccess, a.Error);
        var b = await Direct(context, w).PostAsync(Fine(authority, 80m, reference: "SYNTH-FINE-002")); Assert.True(b.IsSuccess, b.Error);
        Assert.NotEqual(a.Value!.ReferenceCode, b.Value!.ReferenceCode);

        // Genuine canonical Office cash: no collector, Head actor, fines classification, Official Receipt policy, one line, full evidence, no physical OR invented.
        var collections = await context.Collections.AsNoTracking().OrderBy(x => x.ReferenceNumber).ToListAsync();
        Assert.All(collections, c => Assert.Equal((null, "SuperAdmin", "SYNTHETIC OFFICE-TURNOVER TEST"), (c.CollectorId, c.ActorRole, c.PayerName)));
        var lines = await context.CollectionLines.AsNoTracking().ToListAsync();
        var fines = await context.RevenueClassifications.AsNoTracking().SingleAsync(x => x.SemanticCode == RevenueClassificationCodes.PenaltiesAndFines);
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Equal((fines.Id, CollectionSourceKind.DirectFineCollection, authority), (l.RevenueClassificationId, l.SourceKind, l.SourceId)));
        Assert.All(lines, l => { Assert.Contains(WorkingCopyDirectFineFixture.Marker, l.CalculationSnapshot); Assert.Contains("SYNTHETIC OFFICE-TURNOVER TEST", l.CalculationSnapshot); });
        Assert.Equal(2, await context.PostingOperations.AsNoTracking().CountAsync(x => x.Origin == "DirectFineCollection" && x.CollectionId != null));
        Assert.Empty(await context.AccountableDocuments.AsNoTracking().Where(x => x.CollectionId != null).ToArrayAsync());
        Assert.Empty(await context.FineAssessments.AsNoTracking().ToArrayAsync());                                      // no account or assessment created
        Assert.Empty(await context.PaymentRecords.AsNoTracking().ToArrayAsync());

        // Eligible for turnover at their exact cash.
        var review = (await Turnover(context, w).GetReviewAsync(new(Today, Today))).Value!;
        Assert.Equal(new[] { a.Value.ReferenceCode, b.Value.ReferenceCode }.OrderBy(x => x), review.Collections.Select(x => x.ReferenceCode).OrderBy(x => x));
        Assert.Equal(200m, review.ExpectedAmount);

        var short1 = await Turnover(context, w).RecordAsync(new(Guid.NewGuid(), Today, Today, Today, review.Collections.Select(x => x.CollectionId).ToList(), 190m,
            "SYNTH-TURNOVER-001", "Test Cashier", null));
        Assert.False(short1.IsSuccess);                                                                                  // a difference needs an explanation
        var recorded = await Turnover(context, w).RecordAsync(new(Guid.NewGuid(), Today, Today, Today, review.Collections.Select(x => x.CollectionId).ToList(), 190m,
            "SYNTH-TURNOVER-001", "Test Cashier", "Counted again at the cashier"));
        Assert.True(recorded.IsSuccess, recorded.Error);
        Assert.Equal((200m, 190m, 10m, true), (recorded.Value!.Row.ExpectedAmount, recorded.Value.Row.TurnedOverAmount, recorded.Value.Row.DifferenceAmount, recorded.Value.Row.NeedsReview));

        // Accountability: Office cash is separate from collectors and untouched by the turnover.
        var position = (await Turnover(context, w).GetPositionAsync(Today, Today)).Value!;
        Assert.Equal((200m, 200m, 0m), (position.Collected, position.Covered, position.Pending));
        var remit = new RemittanceWorkflow(context, new Actor(w.HeadId, w.TenantId, "SuperAdmin"), new Tenant(w.TenantId));
        var collectors = (await remit.GetPositionAsync(Today, Today)).Value!;
        Assert.Equal((0m, 0m, 0m, 200m, 2), (collectors.Collected, collectors.Remitted, collectors.Unremitted, collectors.OfficeOriginCollected, collectors.OfficeOriginCount));
        Assert.Empty(await context.CollectionRemittances.AsNoTracking().ToArrayAsync());
        var feed = await new CollectionActivityReader(context).GetAsync(w.TenantId, Today, Today);
        Assert.Equal(200m, feed.Where(x => x.Authority == "Canonical").Sum(x => x.NetAmount));

        // Void releases the cash; the original receipts and the turnover record both stay.
        var voided = await Turnover(context, w).VoidAsync(recorded.Value.Row.Id, new("Synthetic acceptance void"));
        Assert.True(voided.IsSuccess, voided.Error);
        Assert.Equal(200m, (await Turnover(context, w).GetPositionAsync(Today, Today)).Value!.Pending);
        context.ChangeTracker.Clear();
        Assert.Equal(2, await context.Collections.CountAsync()); Assert.Equal(1, await context.OfficeCashTurnovers.CountAsync());
    }
}
