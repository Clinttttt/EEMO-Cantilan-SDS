using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// ST-20. The Head-controlled, prospective activation of FRESH monthly rent rows, and the durable late-payload adapter that makes it safe:
/// through the real workflows, tenant filters and PostgreSQL. Nothing here activates anything by itself.
/// </summary>
public sealed partial class EcfCollectionWorkflowTests
{
    private static SettlementCutoverReconciliationEvidence AttestedEvidence(params Guid[] collectors) => new(
        true, true, true, true, true, true, "RENT-ACT-REF-1",
        collectors.Select(id => new CutoverCollectorEvidence(id, "1.0.0", 0, true, DateTime.UtcNow.AddMinutes(-2), "queue-drained-1")).ToArray());

    private RentActivationWorkflow Activation(AppDbContext context, Guid user, Guid tenantId, string role = "SuperAdmin")
    {
        var actor = new TestActor(user, tenantId, role);
        var fixedTenant = new FixedTenant(tenantId);
        return new RentActivationWorkflow(context, actor, fixedTenant, new SettlementCutoverWorkflow(context, actor, fixedTenant));
    }

    /// <summary>A facility whose last month carries legacy cash (stays legacy) and whose current month has no row yet (a fresh month).</summary>
    private async Task<(Seed Seed, RentSeed Rent, Guid Collector, DateOnly Previous)> SeedFreshMonthAsync(FacilityCode facility = FacilityCode.TCC)
    {
        var seed = await SeedEcfAsync(canonical: true);
        var previous = seed.Period.AddMonths(-1);
        var rent = await SeedRentSourcesAsync(seed, [previous], false, partialAmount: 400m, facilityCode: facility, monthlyRate: 1000m, sourceOwned: true);
        var collector = await SeedCollectorAsync(seed.TenantId, facility);
        return (seed, rent, collector, previous);
    }

    [SkippableFact]
    public async Task RentActivation_plan_names_the_fresh_row_leaves_legacy_cash_alone_and_writes_nothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (seed, rent, collector, previous) = await SeedFreshMonthAsync();
        await using var context = db.CreateContext(seed.TenantId);
        var flow = Activation(context, seed.UserId, seed.TenantId);

        var fresh = (await flow.PlanAsync(new(FacilityCode.TCC, seed.Period.Year, seed.Period.Month, AttestedEvidence(collector)))).Value!;
        var row = Assert.Single(fresh.Rows);
        Assert.True(row.Eligible); Assert.Null(row.PaymentRecordId); Assert.Equal(1, fresh.EligibleCount); Assert.True(fresh.Ready);

        var legacyMonth = (await flow.PlanAsync(new(FacilityCode.TCC, previous.Year, previous.Month, AttestedEvidence(collector)))).Value!;
        var legacyRow = Assert.Single(legacyMonth.Rows);
        Assert.False(legacyRow.Eligible); Assert.Contains("legacy settlement evidence", legacyRow.Reason); Assert.False(legacyMonth.Ready);

        var noEvidence = (await flow.PlanAsync(new(FacilityCode.TCC, seed.Period.Year, seed.Period.Month, null))).Value!;
        Assert.False(noEvidence.Ready);

        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Single(await verify.PaymentRecords.ToListAsync());                        // only the seeded legacy month; the plan created nothing
        Assert.Empty(await verify.CollectionSettlementCutovers.Where(x => x.SourceKind == CollectionSourceKind.PaymentRecord).ToListAsync());   // no rent cutover was started
    }

    [SkippableFact]
    public async Task RentActivation_makes_a_fresh_month_canonical_then_Mobile_returns_a_genuine_SRC_and_old_legacy_payloads_are_preserved_not_applied()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (seed, rent, collector, previous) = await SeedFreshMonthAsync();
        var evidence = AttestedEvidence(collector);
        string fingerprint; Guid recordId;
        await using (var context = db.CreateContext(seed.TenantId))
            fingerprint = (await Activation(context, seed.UserId, seed.TenantId).PlanAsync(new(FacilityCode.TCC, seed.Period.Year, seed.Period.Month, evidence))).Value!.PlanFingerprint;

        await using (var context = db.CreateContext(seed.TenantId))
        {
            var activated = await Activation(context, seed.UserId, seed.TenantId).ActivateAsync(new(FacilityCode.TCC, seed.Period.Year, seed.Period.Month, fingerprint, evidence));
            Assert.True(activated.IsSuccess, activated.Error);
            Assert.Equal((1, 0), (activated.Value!.Activated, activated.Value.Failed));
        }
        await using (var verify = db.CreateContext(seed.TenantId))
        {
            var record = await verify.PaymentRecords.SingleAsync(x => x.BillingMonth == seed.Period.Month && x.BillingYear == seed.Period.Year);
            recordId = record.Id;
            Assert.Equal(SettlementAuthority.Canonical, record.SettlementAuthorityState);
            var cutover = await verify.CollectionSettlementCutovers.SingleAsync(x => x.SourceId == record.Id);
            Assert.Equal((1000m, 0m, 1000m), (cutover.OpeningAssessmentAmount, cutover.OpeningLegacySettledAmount, cutover.OpeningOutstandingAmount));
            Assert.Empty(await verify.Collections.ToListAsync());                                   // activation creates no Collection and no SRC
            var legacy = await verify.PaymentRecords.SingleAsync(x => x.Id == rent.PaymentRecordIds[previous]);
            Assert.Equal((SettlementAuthority.Legacy, PaymentStatus.Partial, 400m), (legacy.SettlementAuthorityState, legacy.Status, legacy.PartialAmount));   // history untouched
        }

        // The first real collection afterwards carries a genuine SRC.
        long version; await using (var read = db.CreateContext(seed.TenantId)) version = (await read.PaymentRecords.SingleAsync(x => x.Id == recordId)).SettlementVersion;
        string src;
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var posted = await AsCollector(context, seed.TenantId, collector).PostMobileRentAsync(new(Guid.NewGuid(), rent.StallId, seed.Period.Year, seed.Period.Month, 400m, version, PhilippineTime.Today));
            Assert.True(posted.IsSuccess, posted.Error);
            src = posted.Value!.ReferenceCode;
            Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", src);
        }

        // An older legacy payload for the now-canonical month is never applied; it is preserved durably, idempotently.
        var late = new RecordPaymentCommand(rent.StallId, seed.Period.Year, seed.Period.Month, PaymentStatus.Paid, null, null, "OR-LATE-1", Guid.NewGuid());
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var record = await context.PaymentRecords.SingleAsync(x => x.Id == recordId);
            var reconcile = new LegacyRentPayloadReconciliation(context);
            var first = await reconcile.PreserveAsync(seed.TenantId, record, late, "collector-1", fromWeb: false, default);
            Assert.True(first.IsSuccess, first.Error); Assert.Contains("No Collection, SRC or balance was created", first.Value);
            var replay = await reconcile.PreserveAsync(seed.TenantId, record, late, "collector-1", fromWeb: false, default);
            Assert.Equal(first.Value, replay.Value);
            var conflict = await reconcile.PreserveAsync(seed.TenantId, record, late with { ORNumber = "OR-OTHER" }, "collector-1", fromWeb: false, default);
            Assert.False(conflict.IsSuccess); Assert.Equal(ResultStatus.Conflict, conflict.Status);
        }
        await using (var verify = db.CreateContext(seed.TenantId))
        {
            var operation = await verify.PostingOperations.SingleAsync(x => x.ClientOperationId == late.ClientOperationId);
            Assert.Equal((PostingOperationStatus.ReconciliationRequired, "OLD_MOBILE_PAYLOAD_AFTER_CUTOVER"), (operation.Status, operation.OutcomeCode));
            var collection = await verify.Collections.SingleAsync();
            Assert.Equal((src, 400m), (collection.ReferenceCode, collection.TotalAmount));        // still exactly one real collection
            Assert.Equal(1, await verify.PostingOperations.CountAsync(x => x.Status == PostingOperationStatus.ReconciliationRequired));
        }

        // The month is now canonical: a second review finds nothing fresh, and a re-run is refused rather than repeated.
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var flow = Activation(context, seed.UserId, seed.TenantId);
            var again = (await flow.PlanAsync(new(FacilityCode.TCC, seed.Period.Year, seed.Period.Month, evidence))).Value!;
            Assert.Equal(0, again.EligibleCount); Assert.Contains("Already canonical", again.Rows.Single().Reason);
            Assert.False((await flow.ActivateAsync(new(FacilityCode.TCC, seed.Period.Year, seed.Period.Month, again.PlanFingerprint, evidence))).IsSuccess);
        }
    }

    [SkippableFact]
    public async Task RentActivation_is_refused_without_the_collectors_evidence_and_writes_nothing_not_even_the_month_row()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (seed, _, collector, _) = await SeedFreshMonthAsync();
        var withoutCollector = AttestedEvidence();                                                       // the assigned collector attested nothing
        await using var context = db.CreateContext(seed.TenantId);
        var flow = Activation(context, seed.UserId, seed.TenantId);
        var plan = (await flow.PlanAsync(new(FacilityCode.TCC, seed.Period.Year, seed.Period.Month, withoutCollector))).Value!;

        var refused = await flow.ActivateAsync(new(FacilityCode.TCC, seed.Period.Year, seed.Period.Month, plan.PlanFingerprint, withoutCollector));

        Assert.False(refused.IsSuccess); Assert.Contains("Collector " + collector.ToString("N") + " has no verified", refused.Error);
        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Single(await verify.PaymentRecords.ToListAsync());                                        // the month row was rolled back with the refusal
        Assert.Empty(await verify.CollectionSettlementCutovers.Where(x => x.OpeningAssessmentAmount == 1000m).ToListAsync());

        // A stale plan (evidence changed since it was reviewed) is refused too.
        var stale = await flow.ActivateAsync(new(FacilityCode.TCC, seed.Period.Year, seed.Period.Month, plan.PlanFingerprint, AttestedEvidence(collector)));
        Assert.False(stale.IsSuccess); Assert.Contains("Review the plan again", stale.Error);
    }

    [SkippableFact]
    public async Task RentActivation_refuses_a_collector_the_public_market_and_a_month_that_has_not_begun_and_never_crosses_tenants()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var (seed, _, collector, _) = await SeedFreshMonthAsync();
        var other = await SeedEcfAsync(canonical: true);
        await using var context = db.CreateContext(seed.TenantId);

        var notHead = await Activation(context, seed.UserId, seed.TenantId, "Collector").PlanAsync(new(FacilityCode.TCC, seed.Period.Year, seed.Period.Month, AttestedEvidence(collector)));
        Assert.Equal(ResultStatus.Forbidden, notHead.Status);
        var flow = Activation(context, seed.UserId, seed.TenantId);
        Assert.Equal(ResultStatus.Invalid, (await flow.PlanAsync(new(FacilityCode.NPM, seed.Period.Year, seed.Period.Month, null))).Status);
        Assert.Equal(ResultStatus.Invalid, (await flow.PlanAsync(new(FacilityCode.TCC, seed.Period.AddMonths(1).Year, seed.Period.AddMonths(1).Month, null))).Status);

        await using var otherContext = db.CreateContext(other.TenantId);
        var otherPlan = (await Activation(otherContext, other.UserId, other.TenantId).PlanAsync(new(FacilityCode.TCC, seed.Period.Year, seed.Period.Month, null))).Value!;
        Assert.Empty(otherPlan.Rows);                                                                    // the other tenant sees none of this tenant's stalls
    }
}
