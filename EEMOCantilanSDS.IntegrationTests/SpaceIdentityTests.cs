using EEMOCantilanSDS.Application.Common.Lifecycle;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static EEMOCantilanSDS.IntegrationTests.SpaceTestKit;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// Phase 2: the stable physical-space identity and its database-level guarantees, against real PostgreSQL. The workflow is not involved: these
/// assert what the DATABASE refuses, because a race between two requests is finally decided there. Also the read-only preflight, the guarded
/// backfill (ambiguity is reported, never guessed) and the migration's rollback.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SpaceIdentityTests(PostgresFixture db)
{
    private static string? SqlState(DbUpdateException ex) => (ex.InnerException as PostgresException)?.SqlState;

    private async Task<(Guid Tenant, SpaceRegistration Space)> TenantWithSpaceAsync(string number = "12")
    {
        var tenant = await SeedTenantAsync(db, "Identity Tenant");
        var first = await AddAsync(db, tenant, number, new DateOnly(2026, 1, 1), closedOn: new DateOnly(2026, 6, 30));
        return (tenant, first.Registration);
    }

    // ── overlap protection ──

    [SkippableFact]
    public async Task TwoOccupanciesOfOneSpace_CanNeverShareADay_AndTheBoundaryIsInclusive()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (tenant, space) = await TenantWithSpaceAsync();                                   // occupancy 1: Jan 1 .. Jun 30

        // Starting on the last day of the previous occupancy shares that day: refused by the database (exclusion violation).
        var sameDay = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(db, tenant, "12", new DateOnly(2026, 6, 30), holder: "Other", onSpace: space));
        Assert.Equal("23P01", SqlState(sameDay));
        var overlapping = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(db, tenant, "12", new DateOnly(2026, 3, 1), closedOn: new DateOnly(2026, 8, 1), holder: "Other", onSpace: space));
        Assert.Equal("23P01", SqlState(overlapping));

        // The day after is fine, and a historical reuse of the same number on the same space is exactly this.
        var next = await AddAsync(db, tenant, "12", new DateOnly(2026, 7, 1), closedOn: new DateOnly(2026, 9, 30), holder: "Second Holder", onSpace: space);
        // An open occupancy then cannot start inside the closed one's range, nor before it ends.
        var open = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(db, tenant, "12", new DateOnly(2026, 9, 30), holder: "Third", onSpace: space));
        Assert.Equal("23P01", SqlState(open));
        var third = await AddAsync(db, tenant, "12", new DateOnly(2026, 10, 1), holder: "Third Holder", onSpace: space);
        // And once an occupancy is open-ended nothing may begin after it either (it runs to infinity).
        var later = await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(db, tenant, "12", new DateOnly(2027, 1, 1), holder: "Fourth", onSpace: space));
        Assert.Equal("23P01", SqlState(later));
        Assert.NotEqual(next.Account.Id, third.Account.Id);
    }

    [SkippableFact]
    public async Task AccountsOnDifferentSpacesNeverConflict_AndANumberCanBeReusedAcrossTenantsAndSources()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var a = await SeedTenantAsync(db, "Tenant A");
        var b = await SeedTenantAsync(db, "Tenant B");
        await AddAsync(db, a, "7", new DateOnly(2026, 1, 1));
        await AddAsync(db, b, "7", new DateOnly(2026, 1, 1));                                 // another municipality, same number
        await AddAsync(db, a, "7", new DateOnly(2026, 1, 1), kind: ObligationKind.VegetableFruitSpaceRental);   // another source, same number
        await AddAsync(db, a, "8", new DateOnly(2026, 1, 1));                                 // another space, overlapping dates
    }

    [SkippableFact]
    public async Task ASpaceNumberIsUniquePerTenantAndSource_AndSevenAndZeroZeroSevenAreTheSameSpace()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Numbers");
        await AddAsync(db, tenant, "7", new DateOnly(2026, 1, 1));
        await using var context = db.CreateContext(tenant);
        context.SpaceRegistrations.Add(SpaceRegistration.Create(tenant, ObligationKind.KanmanggaySpaceRental, "007", "test"));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.Equal("23505", SqlState(ex));
        Assert.Equal(SpaceRegistration.Normalize("007"), SpaceRegistration.Normalize("7"));
        Assert.NotEqual(SpaceRegistration.Normalize("A-7"), SpaceRegistration.Normalize("7"));
    }

    [SkippableFact]
    public async Task TheTenantCompositeKeys_RefuseAnotherMunicipalitysSpaceOrPredecessor()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var a = await SeedTenantAsync(db, "Tenant A");
        var b = await SeedTenantAsync(db, "Tenant B");
        var inA = await AddAsync(db, a, "12", new DateOnly(2026, 1, 1), closedOn: new DateOnly(2026, 3, 31));

        // Tenant B's account pointing at tenant A's registration.
        var foreignSpace = ObligationAccount.Create(b, ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, "12", null, null, new DateOnly(2026, 1, 1), "test", actualOccupant: "B Holder");
        foreignSpace.LinkRegistration(inA.Registration.Id);
        await using (var context = db.CreateContext(b))
        {
            context.ObligationAccounts.Add(foreignSpace);
            context.ObligationRates.Add(ObligationRate.Create(b, foreignSpace.Id, foreignSpace.ActiveFrom, 100m, "test"));
            Assert.Equal("23503", SqlState(await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())));
        }

        // Tenant B's account naming tenant A's account as its predecessor.
        var ownSpace = await AddAsync(db, b, "30", new DateOnly(2026, 1, 1), closedOn: new DateOnly(2026, 3, 31));
        var foreignPredecessor = ObligationAccount.Create(b, ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, "30", null, null, new DateOnly(2026, 4, 1), "test", actualOccupant: "B Holder");
        typeof(ObligationAccount).GetProperty(nameof(ObligationAccount.SpaceRegistrationId))!.SetValue(foreignPredecessor, ownSpace.Registration.Id);
        typeof(ObligationAccount).GetProperty(nameof(ObligationAccount.PredecessorAccountId))!.SetValue(foreignPredecessor, inA.Account.Id);
        await using (var context = db.CreateContext(b))
        {
            context.ObligationAccounts.Add(foreignPredecessor);
            context.ObligationRates.Add(ObligationRate.Create(b, foreignPredecessor.Id, foreignPredecessor.ActiveFrom, 100m, "test"));
            Assert.Equal("23503", SqlState(await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())));
        }
    }

    [SkippableFact]
    public async Task AnOccupancyHasAtMostOneSuccessor_AndNeverFollowsItself_AndAnEventLotHasNoSpace()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Chain");
        var first = await AddAsync(db, tenant, "12", new DateOnly(2026, 1, 1), closedOn: new DateOnly(2026, 3, 31));

        ObligationAccount Successor(DateOnly from)
        {
            var s = ObligationAccount.Create(tenant, ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, "12", null, null, from, "test", actualOccupant: "Next");
            s.FollowOccupancy(first.Account);
            return s;
        }
        var one = Successor(new DateOnly(2026, 4, 1));
        await using (var context = db.CreateContext(tenant))
        {
            context.ObligationAccounts.Add(one);
            context.ObligationRates.Add(ObligationRate.Create(tenant, one.Id, one.ActiveFrom, 100m, "test"));
            await context.SaveChangesAsync();
        }
        // A second successor of the same predecessor: the unique index refuses it, even on a different, non-overlapping interval.
        var two = Successor(new DateOnly(2027, 6, 1));
        await using (var context = db.CreateContext(tenant))
        {
            context.ObligationAccounts.Add(two);
            context.ObligationRates.Add(ObligationRate.Create(tenant, two.Id, two.ActiveFrom, 100m, "test"));
            Assert.Equal("23505", SqlState(await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync())));
        }
        // The domain refuses a successor that starts on or before the predecessor's last day, or of an open predecessor.
        Assert.Throws<ArgumentException>(() => Successor(new DateOnly(2026, 3, 31)));
        var openOne = await AddAsync(db, tenant, "40", new DateOnly(2026, 1, 1));
        var early = ObligationAccount.Create(tenant, ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, "40", null, null, new DateOnly(2026, 6, 1), "test", actualOccupant: "X");
        Assert.Throws<InvalidOperationException>(() => early.FollowOccupancy(openOne.Account));
        // A registration only exists for a monthly space: the domain and the database both say so.
        var lot = ObligationAccount.Create(tenant, ObligationKind.FiestaArawLotRental, Guid.Empty, null, "3", LotRentalEvent.Fiesta, new DateOnly(2026, 8, 15), new DateOnly(2026, 8, 15), "test", actualOccupant: "Lot");
        Assert.Throws<ArgumentException>(() => lot.LinkRegistration(first.Registration.Id));
    }

    // ── append-only evidence ──

    [SkippableFact]
    public async Task LifecycleEventsAndContractTerms_CannotBeChangedOrRemoved()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Evidence");
        var occupancy = await AddAsync(db, tenant, "12", new DateOnly(2026, 1, 1), signed: true);
        var evt = SpaceLifecycleEvent.Record(tenant, occupancy.Registration.Id, SpaceLifecycleAction.Renew, occupancy.Account.Id, null, new DateOnly(2029, 1, 1), Guid.NewGuid(),
            new string('a', 64), "v", "Renewed", "Res. 12", Guid.NewGuid(), "test", DateTime.UtcNow);
        var term = SpaceContractTerm.Create(tenant, occupancy.Account.Id, occupancy.Registration.Id, null, new DateOnly(2029, 1, 1), 3, "Lessee", "REF", "Res. 12", evt.Id, Guid.NewGuid(), "test", DateTime.UtcNow);
        await using (var context = db.CreateContext(tenant)) { context.SpaceLifecycleEvents.Add(evt); context.SpaceContractTerms.Add(term); await context.SaveChangesAsync(); }

        await using var raw = db.CreateRawConnection();
        await raw.OpenAsync();
        foreach (var sql in new[]
                 {
                     "UPDATE \"SpaceLifecycleEvents\" SET \"Reason\" = 'tampered'",
                     "DELETE FROM \"SpaceLifecycleEvents\"",
                     "UPDATE \"SpaceContractTerms\" SET \"Years\" = 5",
                     "DELETE FROM \"SpaceContractTerms\""
                 })
        {
            await using var command = new NpgsqlCommand(sql, raw);
            var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("55000", ex.SqlState);
            Assert.Contains("append-only", ex.MessageText);
        }
        await using var verify = db.CreateContext(tenant);
        Assert.Equal("Renewed", (await verify.SpaceLifecycleEvents.SingleAsync()).Reason);
        Assert.Equal(3, (await verify.SpaceContractTerms.SingleAsync()).Years);
    }

    [SkippableFact]
    public async Task OnlyATenantRestoreSwitch_PermitsDelete()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Restore Switch");
        var occupancy = await AddAsync(db, tenant, "12", new DateOnly(2026, 1, 1));
        await using (var context = db.CreateContext(tenant))
        {
            context.SpaceLifecycleEvents.Add(SpaceLifecycleEvent.Record(tenant, occupancy.Registration.Id, SpaceLifecycleAction.Backfill, occupancy.Account.Id, null, new DateOnly(2026, 1, 1),
                Guid.NewGuid(), new string('b', 64), "v", "r", null, Guid.Empty, "test", DateTime.UtcNow));
            await context.SaveChangesAsync();
        }
        await using var raw = db.CreateRawConnection();
        await raw.OpenAsync();
        await using var tx = await raw.BeginTransactionAsync();
        await using (var set = new NpgsqlCommand("SELECT set_config('stalltrack.tenant_restore', 'on', true)", raw, tx)) await set.ExecuteNonQueryAsync();
        await using (var delete = new NpgsqlCommand("DELETE FROM \"SpaceLifecycleEvents\"", raw, tx)) Assert.Equal(1, await delete.ExecuteNonQueryAsync());
        await using (var update = new NpgsqlCommand("UPDATE \"SpaceContractTerms\" SET \"Years\" = 1", raw, tx)) Assert.Equal(0, await update.ExecuteNonQueryAsync());   // no rows: nothing to forbid here
        await tx.RollbackAsync();
    }

    // ── creation integration ──

    [SkippableFact]
    public async Task ANewMonthlySpaceAccountIsBornWithItsSpace_VegetableNumbersStayServerAssigned_AndDuplicatesAreStillRefused()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Creation");
        await using var context = db.CreateContext(tenant);
        var workflow = new EEMOCantilanSDS.Application.Common.Revenue.ObligationWorkflow(context, new Caller(tenant, "SuperAdmin"), new Tenant(tenant), new BusinessClock(Today));
        var holder = "Space Holder";
        // Two Vegetable/Fruit spaces: numbers 1 and 2, allocated by the server, each with its own registration.
        for (var i = 0; i < 2; i++)
        {
            var created = await workflow.CreateAccountAsync(new(ObligationKind.VegetableFruitSpaceRental, Guid.Empty, null, "", null, null, new DateOnly(2026, 9, 1), 500m, ActualOccupant: holder));
            Assert.True(created.IsSuccess, created.Error);
        }
        var km = await workflow.CreateAccountAsync(new(ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, "K-5", null, null, new DateOnly(2026, 9, 1), 800m, ActualOccupant: holder));
        Assert.True(km.IsSuccess, km.Error);

        var accounts = await context.ObligationAccounts.AsNoTracking().OrderBy(a => a.SubjectLabel).ToListAsync();
        Assert.Equal(["1", "2", "K-5"], accounts.Select(a => a.SubjectLabel).ToArray());
        Assert.All(accounts, a => Assert.NotNull(a.SpaceRegistrationId));
        Assert.Equal(3, await context.SpaceRegistrations.CountAsync());
        Assert.Equal(accounts.Select(a => a.SpaceRegistrationId).Distinct().Count(), accounts.Count);
        // A number that already has an account cannot be created again: reuse is only possible through a lifecycle action.
        var duplicate = await workflow.CreateAccountAsync(new(ObligationKind.KanmanggaySpaceRental, Guid.Empty, null, "k-5", null, null, new DateOnly(2026, 9, 1), 800m, ActualOccupant: "Someone Else"));
        Assert.Equal(EEMOCantilanSDS.Application.Common.ResultStatus.Conflict, duplicate.Status);
        Assert.Equal(3, await context.SpaceRegistrations.CountAsync());
    }

    // ── preflight and backfill ──

    private async Task<Guid> SeedLegacyAsync()
    {
        var tenant = await SeedTenantAsync(db, "Legacy");
        // Unambiguous: alone on its number, with an assessed (unpaid) month behind it.
        var solo = await AddAsync(db, tenant, "1", new DateOnly(2026, 1, 1), register: false);
        await AssessAsync(db, tenant, solo.Account, solo.Rate, new DateOnly(2026, 2, 1));
        await AddAsync(db, tenant, "2", new DateOnly(2026, 1, 1), closedOn: new DateOnly(2026, 4, 30), register: false);
        // Ambiguous: three accounts share number 9 ("9" and "009" normalize alike), two of them overlapping, one with money behind it.
        var nine = await AddAsync(db, tenant, "9", new DateOnly(2025, 1, 1), closedOn: new DateOnly(2025, 12, 31), register: false);
        await AssessAsync(db, tenant, nine.Account, nine.Rate, new DateOnly(2025, 3, 1));
        await AddAsync(db, tenant, "009", new DateOnly(2026, 1, 1), closedOn: new DateOnly(2026, 6, 30), holder: "Maria", register: false);
        await AddAsync(db, tenant, "9", new DateOnly(2026, 5, 1), holder: "Pedro", register: false);
        // Same number, other source: not a duplicate.
        await AddAsync(db, tenant, "9", new DateOnly(2026, 1, 1), kind: ObligationKind.VegetableFruitSpaceRental, register: false);
        return tenant;
    }

    [SkippableFact]
    public async Task Preflight_ReportsDuplicatesOverlapsAndFinancialEvidence_AndWritesNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedLegacyAsync();
        await using var context = db.CreateContext(tenant);

        var report = await new SpaceIdentityPreflight(context).RunAsync(tenant);
        Assert.Equal(6, report.MonthlyAccounts);
        Assert.Equal((0, 6), (report.LinkedAccounts, report.UnlinkedAccounts));
        Assert.Equal(3, report.LinkableAccounts);                                              // "1", "2" and the Vegetable "9"
        Assert.False(report.IsClean);
        var duplicate = Assert.Single(report.Findings, f => f.Code == "DuplicateNormalizedNumber");
        Assert.Equal(3, duplicate.AccountIds.Count);
        Assert.True(duplicate.BlocksBackfill);
        Assert.True(duplicate.HasFinancialEvidence);                                           // a human must review it: money is behind one of them
        Assert.Contains(report.Findings, f => f.Code == "OverlappingIntervals");
        Assert.False(context.ChangeTracker.HasChanges());
        Assert.Equal(0, await context.SpaceRegistrations.CountAsync());
        Assert.Equal(0, await context.SpaceLifecycleEvents.CountAsync());
    }

    [SkippableFact]
    public async Task Backfill_LinksOnlyUnambiguousAccounts_NeverGuessesAChain_ChangesNoMoney_AndIsIdempotent()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedLegacyAsync();
        await using var before = db.CreateContext(tenant);
        var periodsBefore = await before.ObligationPeriods.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.AssessedAmount, p.ObligationRateId }).ToListAsync();
        var ratesBefore = await before.ObligationRates.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Amount, r.EffectiveFrom }).ToListAsync();
        var datesBefore = await before.ObligationAccounts.AsNoTracking().OrderBy(a => a.Id).Select(a => new { a.Id, a.ActiveFrom, a.ActiveTo, a.ActualOccupant }).ToListAsync();

        // A dry run changes nothing and says what it would skip.
        await using (var dry = db.CreateContext(tenant))
        {
            var preview = await new SpaceRegistrationBackfill(dry).RunAsync(tenant, "test", apply: false);
            Assert.Equal((0, 3), (preview.Linked, preview.SkippedAmbiguous));
            Assert.Equal(0, await dry.SpaceRegistrations.CountAsync());
        }

        await using (var run = db.CreateContext(tenant))
        {
            var applied = await new SpaceRegistrationBackfill(run).RunAsync(tenant, "test", apply: true);
            Assert.Equal(3, applied.Linked);
            Assert.Equal(3, applied.SkippedAmbiguous);
        }
        await using var after = db.CreateContext(tenant);
        var linked = await after.ObligationAccounts.AsNoTracking().Where(a => a.SpaceRegistrationId != null).ToListAsync();
        Assert.Equal(["1", "2", "9"], linked.Select(a => a.SubjectLabel).Order().ToArray());
        Assert.Equal(3, linked.Select(a => a.SpaceRegistrationId).Distinct().Count());
        Assert.All(linked, a => Assert.Null(a.PredecessorAccountId));                          // nothing is ever linked as a successor
        // The three accounts on Kanmanggay number 9 are untouched.
        var nines = await after.ObligationAccounts.AsNoTracking().Where(a => a.Kind == ObligationKind.KanmanggaySpaceRental && (a.SubjectLabel == "9" || a.SubjectLabel == "009")).ToListAsync();
        Assert.Equal(3, nines.Count);
        Assert.All(nines, a => Assert.Null(a.SpaceRegistrationId));
        Assert.Equal(3, await after.SpaceLifecycleEvents.CountAsync(e => e.Action == SpaceLifecycleAction.Backfill));
        // No financial or lifecycle-date fact changed.
        Assert.Equal(periodsBefore, await after.ObligationPeriods.AsNoTracking().OrderBy(p => p.Id).Select(p => new { p.Id, p.AssessedAmount, p.ObligationRateId }).ToListAsync());
        Assert.Equal(ratesBefore, await after.ObligationRates.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Amount, r.EffectiveFrom }).ToListAsync());
        Assert.Equal(datesBefore, await after.ObligationAccounts.AsNoTracking().OrderBy(a => a.Id).Select(a => new { a.Id, a.ActiveFrom, a.ActiveTo, a.ActualOccupant }).ToListAsync());

        // Idempotent: a second run links nothing and creates nothing.
        await using var again = db.CreateContext(tenant);
        var second = await new SpaceRegistrationBackfill(again).RunAsync(tenant, "test", apply: true);
        Assert.Equal(0, second.Linked);
        Assert.Equal(3, await again.SpaceRegistrations.CountAsync());
        // The preflight now reports the identified accounts as linked and the ambiguity unchanged.
        var report = await new SpaceIdentityPreflight(again).RunAsync(tenant);
        Assert.Equal((3, 3), (report.LinkedAccounts, report.UnlinkedAccounts));
        Assert.Contains(report.Findings, f => f.Code == "DuplicateNormalizedNumber");
    }

    [SkippableFact]
    public async Task Backfill_NeverLinksAnAccountNamedByABlockingFinding()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Blocked");
        var clean = await AddAsync(db, tenant, "1", new DateOnly(2026, 1, 1), register: false);
        var anomalous = await AddAsync(db, tenant, "2", new DateOnly(2026, 1, 1), register: false);
        await AssessAsync(db, tenant, anomalous.Account, anomalous.Rate, new DateOnly(2026, 8, 1));
        await using (var raw = db.CreateRawConnection())          // closure earlier than an assessed month: evidence the backfill must not paper over
        {
            await raw.OpenAsync();
            await using var command = new NpgsqlCommand($"UPDATE \"ObligationAccounts\" SET \"ActiveTo\" = '2026-06-30' WHERE \"Id\" = '{anomalous.Account.Id}'", raw);
            await command.ExecuteNonQueryAsync();
        }
        await using var context = db.CreateContext(tenant);
        var preflight = await new SpaceIdentityPreflight(context).RunAsync(tenant);
        Assert.Contains(preflight.Findings, f => f.Code == "PeriodAfterClosure" && f.AccountIds.Contains(anomalous.Account.Id));
        Assert.Equal(1, preflight.LinkableAccounts);
        var result = await new SpaceRegistrationBackfill(context).RunAsync(tenant, "test", apply: true);
        Assert.Equal((1, 1), (result.Linked, result.SkippedAmbiguous));
        await using var verify = db.CreateContext(tenant);
        Assert.NotNull((await verify.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == clean.Account.Id)).SpaceRegistrationId);
        Assert.Null((await verify.ObligationAccounts.AsNoTracking().SingleAsync(a => a.Id == anomalous.Account.Id)).SpaceRegistrationId);
        Assert.Equal(1, await verify.SpaceRegistrations.CountAsync());
    }

    [SkippableFact]
    public async Task Backfill_WithAnAuthorizedList_LinksOnlyThoseAccounts_AndNothingElse()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Allow List");
        var one = await AddAsync(db, tenant, "1", new DateOnly(2026, 1, 1), register: false);
        var two = await AddAsync(db, tenant, "2", new DateOnly(2026, 1, 1), register: false);
        var three = await AddAsync(db, tenant, "3", new DateOnly(2026, 1, 1), register: false);
        await using var context = db.CreateContext(tenant);
        var result = await new SpaceRegistrationBackfill(context).RunAsync(tenant, "test", apply: true, onlyAccountIds: new HashSet<Guid> { one.Account.Id, three.Account.Id });
        Assert.Equal(2, result.Linked);
        await using var verify = db.CreateContext(tenant);
        var linked = await verify.ObligationAccounts.AsNoTracking().Where(x => x.SpaceRegistrationId != null).Select(x => x.Id).ToListAsync();
        Assert.Equal(new[] { one.Account.Id, three.Account.Id }.Order().ToArray(), linked.Order().ToArray());
        Assert.Null((await verify.ObligationAccounts.AsNoTracking().SingleAsync(x => x.Id == two.Account.Id)).SpaceRegistrationId);
        Assert.Equal(2, await verify.SpaceLifecycleEvents.CountAsync(e => e.Action == SpaceLifecycleAction.Backfill));
    }

    [SkippableFact]
    public async Task Backfill_IsTenantScoped()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var a = await SeedTenantAsync(db, "Tenant A");
        var b = await SeedTenantAsync(db, "Tenant B");
        await AddAsync(db, a, "1", new DateOnly(2026, 1, 1), register: false);
        await AddAsync(db, b, "1", new DateOnly(2026, 1, 1), register: false);
        await using var context = db.CreateContext(a);
        Assert.Equal(1, (await new SpaceRegistrationBackfill(context).RunAsync(a, "test", apply: true)).Linked);
        await using var other = db.CreateContext(b);
        Assert.Equal(0, await other.SpaceRegistrations.CountAsync());
        Assert.Null((await other.ObligationAccounts.AsNoTracking().SingleAsync()).SpaceRegistrationId);
    }

    // ── migration rollback ──

    [SkippableFact]
    public async Task TheMigration_RollsBackCleanly_KeepsExistingAccounts_AndReapplies()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenant = await SeedTenantAsync(db, "Rollback");
        await AddAsync(db, tenant, "1", new DateOnly(2026, 1, 1), register: false);          // an account that predates the identity model

        await using var context = db.CreateContext(Guid.Empty);
        var migrator = context.GetService<IMigrator>();
        const string previous = "20261019090000_AddSlaughterChargeScope20261019";
        await migrator.MigrateAsync(previous);
        try
        {
            await using var raw = db.CreateRawConnection();
            await raw.OpenAsync();
            Assert.Equal(0L, await Scalar(raw, "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('SpaceRegistrations','SpaceLifecycleEvents','SpaceContractTerms')"));
            Assert.Equal(0L, await Scalar(raw, "SELECT count(*) FROM information_schema.columns WHERE table_name = 'ObligationAccounts' AND column_name IN ('SpaceRegistrationId','PredecessorAccountId','RetiredAtUtc')"));
            Assert.Equal(0L, await Scalar(raw, "SELECT count(*) FROM pg_constraint WHERE conname = 'EX_ObligationAccounts_SpaceOccupancy'"));
            Assert.Equal(1L, await Scalar(raw, "SELECT count(*) FROM \"ObligationAccounts\""));       // existing data survives the rollback
        }
        finally
        {
            await migrator.MigrateAsync();                                                     // forward again to the latest
        }
        await using var verify = db.CreateRawConnection();
        await verify.OpenAsync();
        Assert.Equal(3L, await Scalar(verify, "SELECT count(*) FROM information_schema.tables WHERE table_name IN ('SpaceRegistrations','SpaceLifecycleEvents','SpaceContractTerms')"));
        Assert.Equal(1L, await Scalar(verify, "SELECT count(*) FROM pg_constraint WHERE conname = 'EX_ObligationAccounts_SpaceOccupancy'"));
        Assert.Equal(1L, await Scalar(verify, "SELECT count(*) FROM \"ObligationAccounts\""));
        Assert.Equal(2L, await Scalar(verify, "SELECT count(*) FROM pg_trigger WHERE tgname IN ('TR_SpaceLifecycleEvents_AppendOnly','TR_SpaceContractTerms_AppendOnly')"));
    }

    private static async Task<long> Scalar(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
