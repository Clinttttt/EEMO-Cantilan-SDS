using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Npgsql;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class PreviewRentActivationTests(PostgresFixture db)
{
    private sealed class Actor(Guid userId, Guid tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "head";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "preview";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed record Seed(Guid TenantId, Guid FacilityId, Guid UserId, PreviewMonthlyRentTarget Target, string Connection);

    private async Task<Seed> SeedAsync()
    {
        await db.ResetAsync();
        var municipality = Municipality.Create($"PRV-{Guid.NewGuid():N}"[..24], "Preview Test", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"prv-{Guid.NewGuid():N}"[..32]);
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Municipalities.Add(municipality); await setup.SaveChangesAsync(); }
        var facility = Facility.Create(FacilityCode.NCC, "NCC", "NCC", municipalityId: municipality.Id);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.ForMonthlyRental(FacilityCode.NCC), municipality.Id);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1),
            "Permanent Stall Rent", RevenueInstrumentType.OfficialReceipt, municipality.Id);
        await using (var context = db.CreateContext(municipality.Id)) { context.AddRange(facility, classification, policy); await context.SaveChangesAsync(); }
        var connection = db.ApiConnectionString;
        var b = new NpgsqlConnectionStringBuilder(connection);
        // The container is the "preview" for this test; host is loopback and the port is the dynamically mapped one.
        return new Seed(municipality.Id, facility.Id, Guid.NewGuid(),
            new PreviewMonthlyRentTarget(b.Database!, b.Port, municipality.Id, FacilityCode.NCC), connection);
    }

    private static readonly DateOnly Today = PhilippineTime.Today;

    [SkippableFact]
    public async Task Activation_uses_the_real_lifecycle_collects_nothing_and_then_two_head_receipts_get_distinct_SRCs()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var outcome = await PreviewRentActivation.RunAsync(context, seed.Target, seed.Connection, seed.UserId, Today.Year, Today.Month);

        Assert.Equal(SettlementAuthority.Canonical, outcome.Authority);
        Assert.Empty(await context.Collections.ToArrayAsync());       // activation never collects cash
        Assert.Empty(await context.PostingOperations.ToArrayAsync());
        var cutover = await context.CollectionSettlementCutovers.SingleAsync(x => x.SourceId == outcome.PaymentRecordId);
        Assert.Equal((1200m, 0m, 1200m), (cutover.OpeningAssessmentAmount, cutover.OpeningLegacySettledAmount, cutover.OpeningOutstandingAmount));

        var options = PreviewMonthlyRentGate.OptionsFor(true, seed.Target, seed.Connection, out var refusals);
        Assert.Empty(refusals);
        var flow = new CollectionComposerWorkflow(context, new Actor(seed.UserId, seed.TenantId), new Tenant(seed.TenantId), rentOptions: options);
        var quote = (await flow.GetRentObligationAsync(outcome.StallId, Today.Year, Today.Month)).Value!;
        Assert.True(quote.CanPostOfficeReceipt); Assert.Equal(1200, quote.OutstandingAmount);

        var first = await flow.PostOfficeRentAsync(new(Guid.NewGuid(), outcome.StallId, Today.Year, Today.Month, 600, quote.SourceVersion, Today, outcome.ContractId));
        Assert.True(first.IsSuccess, first.Error);
        var remaining = (await flow.GetRentObligationAsync(outcome.StallId, Today.Year, Today.Month)).Value!;
        Assert.Equal(600, remaining.OutstandingAmount);
        var second = await flow.PostOfficeRentAsync(new(Guid.NewGuid(), outcome.StallId, Today.Year, Today.Month, 600, remaining.SourceVersion, Today, outcome.ContractId));
        Assert.True(second.IsSuccess, second.Error);
        Assert.NotEqual(first.Value!.ReferenceCode, second.Value!.ReferenceCode);
        Assert.Equal(0, (await flow.GetRentObligationAsync(outcome.StallId, Today.Year, Today.Month)).Value!.OutstandingAmount);
        context.ChangeTracker.Clear();
        Assert.Equal(2, await context.Collections.CountAsync());
        Assert.Empty(await context.AccountableDocuments.Where(x => x.CollectionId != null).ToArrayAsync()); // no physical OR invented
    }

    [SkippableFact]
    public async Task A_second_permitted_synthetic_stall_activates_alongside_the_first_without_touching_it()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var first = await PreviewRentActivation.RunAsync(context, seed.Target, seed.Connection, seed.UserId, Today.Year, Today.Month);
        var before = await context.CollectionSettlementCutovers.SingleAsync(x => x.SourceId == first.PaymentRecordId);
        var second = await PreviewRentActivation.RunAsync(context, seed.Target, seed.Connection, seed.UserId, Today.Year, Today.Month, "PREVIEW-NCC-02");

        Assert.NotEqual(first.StallId, second.StallId);
        Assert.Equal(SettlementAuthority.Canonical, second.Authority);
        Assert.Equal(2, await context.CollectionSettlementCutovers.CountAsync());
        context.ChangeTracker.Clear();
        var kept = await context.CollectionSettlementCutovers.SingleAsync(x => x.SourceId == first.PaymentRecordId);
        Assert.Equal((before.Id, before.BoundaryVersion, before.OpeningOutstandingAmount), (kept.Id, kept.BoundaryVersion, kept.OpeningOutstandingAmount));
        Assert.Empty(await context.Collections.ToArrayAsync());      // still no cash
        Assert.Empty(await context.PostingOperations.ToArrayAsync());

        var notPermitted = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PreviewRentActivation.RunAsync(context, seed.Target, seed.Connection, seed.UserId, Today.Year, Today.Month, "PREVIEW-NCC-04"));
        Assert.Contains("not permitted", notPermitted.Message);
        Assert.Equal(2, await context.Stalls.CountAsync());
    }

    [SkippableFact]
    public async Task A_second_activation_is_refused_and_changes_nothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        await PreviewRentActivation.RunAsync(context, seed.Target, seed.Connection, seed.UserId, Today.Year, Today.Month);
        var cutovers = await context.CollectionSettlementCutovers.CountAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PreviewRentActivation.RunAsync(context, seed.Target, seed.Connection, seed.UserId, Today.Year, Today.Month));
        Assert.Contains("not Legacy", ex.Message);
        Assert.Equal(cutovers, await context.CollectionSettlementCutovers.CountAsync());
    }

    [SkippableFact]
    public async Task A_wrong_database_or_wrong_tenant_is_refused_before_any_write()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);

        var wrongDb = await Assert.ThrowsAsync<InvalidOperationException>(() => PreviewRentActivation.RunAsync(
            context, seed.Target with { Database = "stalltrack_preview" }, seed.Connection, seed.UserId, Today.Year, Today.Month));
        Assert.Contains("Refused", wrongDb.Message);

        var wrongTenant = await Assert.ThrowsAsync<InvalidOperationException>(() => PreviewRentActivation.RunAsync(
            context, seed.Target with { Tenant = Guid.NewGuid() }, seed.Connection, seed.UserId, Today.Year, Today.Month));
        Assert.Contains("tenant", wrongTenant.Message);

        Assert.Empty(await context.Stalls.ToArrayAsync());
        Assert.Empty(await context.PaymentRecords.ToArrayAsync());
        Assert.Empty(await context.CollectionSettlementCutovers.ToArrayAsync());
    }

    [SkippableFact]
    public async Task A_positive_legacy_opening_on_the_synthetic_stall_is_refused_and_existing_legacy_history_is_untouched()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        // An existing, unrelated legacy payment (shape of the preview's NCC 1200 / reference 546564).
        var legacyStall = Stall.Create(seed.FacilityId, "1", 1200m, ApplicableFees.BaseRental, municipalityId: seed.TenantId);
        var legacyRecord = PaymentRecord.Create(legacyStall.Id, Today.Year, Today.Month, 1200m, "legacy");
        legacyRecord.UpdateStatus(PaymentStatus.Paid, 0, updatedBy: "head"); legacyRecord.SetOrNumber("546564", "head");
        // A synthetic-named stall that already carries legacy cash must never be activated.
        var tainted = Stall.Create(seed.FacilityId, PreviewRentActivation.StallNumber, 1200m, ApplicableFees.BaseRental, municipalityId: seed.TenantId);
        var taintedContract = Contract.Create(tainted.Id, "x", "x", new DateOnly(Today.Year, Today.Month, 1), 5, 1200m, createdBy: "t");
        var taintedRecord = PaymentRecord.Create(tainted.Id, Today.Year, Today.Month, 1200m, "t");
        taintedRecord.RecordPayment("LEGACY", seed.UserId, PaymentStatus.Partial, partialAmount: 600m, updatedBy: "t");
        context.AddRange(legacyStall, tainted, taintedContract); context.PaymentRecords.AddRange(legacyRecord, taintedRecord);
        await context.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PreviewRentActivation.RunAsync(context, seed.Target, seed.Connection, seed.UserId, Today.Year, Today.Month));
        Assert.Contains("zero legacy collected cash", ex.Message);
        context.ChangeTracker.Clear();
        var kept = await context.PaymentRecords.SingleAsync(x => x.Id == legacyRecord.Id);
        Assert.Equal(("546564", PaymentStatus.Paid, SettlementAuthority.Legacy), (kept.ORNumber, kept.Status, kept.SettlementAuthorityState));
        Assert.Equal(SettlementAuthority.Legacy, (await context.PaymentRecords.SingleAsync(x => x.Id == taintedRecord.Id)).SettlementAuthorityState);
        Assert.Empty(await context.CollectionSettlementCutovers.ToArrayAsync());
    }

    [SkippableFact]
    public async Task Collection_Manager_record_reports_the_writer_that_owns_each_month()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var outcome = await PreviewRentActivation.RunAsync(context, seed.Target, seed.Connection, seed.UserId, Today.Year, Today.Month);
        var repository = new PaymentRepository(context);

        // Canonical month: the page offers receipts only.
        var canonical = await repository.GetPaymentRecordAsync(outcome.StallId, Today.Year, Today.Month, default);
        Assert.Equal((SettlementAuthority.Canonical, 1200m, 1200m), (canonical!.SettlementAuthority, canonical.MonthlyRental, canonical.BalanceDue));

        // A legacy month and a month mid-cutover are reported as such, so the page keeps its editor or fails closed.
        var legacy = PaymentRecord.Create(outcome.StallId, Today.Year, 1, 1200m, "t");
        var pending = PaymentRecord.Create(outcome.StallId, Today.Year, 2, 1200m, "t");
        pending.MarkSettlementPendingCutover();
        context.PaymentRecords.AddRange(legacy, pending); await context.SaveChangesAsync();
        Assert.Equal(SettlementAuthority.Legacy, (await repository.GetPaymentRecordAsync(outcome.StallId, Today.Year, 1, default))!.SettlementAuthority);
        Assert.Equal(SettlementAuthority.PendingCutover, (await repository.GetPaymentRecordAsync(outcome.StallId, Today.Year, 2, default))!.SettlementAuthority);
    }

    [SkippableFact]
    public async Task Readiness_blockers_are_not_bypassed_an_assigned_collector_stops_the_tool()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        var seed = await SeedAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var collector = CollectorUser.Create("Mobile Collector", "MC-01", "mc-" + Guid.NewGuid().ToString("N")[..8],
            null, null, new HashedPassword("test-hash"), seed.TenantId);
        collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, seed.FacilityId, FacilityCode.NCC));
        context.CollectorUsers.Add(collector);
        await context.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PreviewRentActivation.RunAsync(context, seed.Target, seed.Connection, seed.UserId, Today.Year, Today.Month));
        Assert.Contains("collectors are assigned", ex.Message);
        Assert.Empty(await context.CollectionSettlementCutovers.ToArrayAsync());
        Assert.DoesNotContain(await context.PaymentRecords.ToListAsync(), x => x.SettlementAuthorityState != SettlementAuthority.Legacy);
    }
}
