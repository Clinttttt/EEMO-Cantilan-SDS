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
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class EcfCollectionWorkflowTests(PostgresFixture db)
{
    private sealed record Seed(Guid TenantId, Guid UserId, Guid BillId, Guid OrDocumentId,
        Guid OtherOrDocumentId, Guid CtDocumentId, DateOnly Period);

    private sealed class TestActor(Guid userId, Guid tenantId, string role = "Admin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "ecf-test-admin";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "ecf-test";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    [SkippableFact]
    public async Task EcfQuoteUsesElectricityFactsAndCanonicalPartialPostingPreservesWater()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);

        var quoteResult = await workflow.GetObligationAsync(seed.BillId);
        Assert.True(quoteResult.IsSuccess, quoteResult.Error);
        var quote = quoteResult.Value!;
        Assert.Equal(seed.TenantId, quote.MunicipalityId);
        Assert.Equal(CollectionSourceKind.UtilityBill, quote.SourceKind);
        Assert.Equal(CollectionSourcePart.Electricity, quote.SourcePart);
        Assert.Equal(800m, quote.AssessedAmount);
        Assert.Equal(200m, quote.CumulativeSettledEvidence);
        Assert.Equal(600m, quote.OutstandingAmount);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, quote.Instrument);
        Assert.Equal(SettlementAuthority.Canonical, quote.SettlementAuthority);
        Assert.Equal("Lisa ECF", quote.PayerNameSnapshot);

        var draftResult = await workflow.CreateDraftAsync(
            new CreateEcfCollectionDraftRequest(seed.BillId, 300m, seed.OrDocumentId));
        Assert.True(draftResult.IsSuccess, draftResult.Error);
        var draft = draftResult.Value!;
        Assert.False(draft.IsReviewedForCurrentRevision);
        Assert.Equal(300m, draft.TotalAmount);
        Assert.Equal(0, await context.Collections.CountAsync());
        Assert.Equal(0, await context.CollectionAllocations.CountAsync());
        Assert.Equal(AccountableDocumentState.InOffice,
            (await context.AccountableDocuments.SingleAsync(x => x.Id == seed.OrDocumentId)).State);
        Assert.Equal(200m, (await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).ElecAmountPaid);

        var staleUpdate = await workflow.UpdateAllocationAsync(draft.DraftId,
            new UpdateEcfDraftAllocationRequest(draft.Revision - 1, 301m));
        Assert.False(staleUpdate.IsSuccess);
        Assert.Contains("STALE DRAFT", staleUpdate.Error, StringComparison.OrdinalIgnoreCase);

        var reviewed = await workflow.ReviewAsync(draft.DraftId, new EcfDraftRevisionRequest(draft.Revision));
        Assert.True(reviewed.IsSuccess, reviewed.Error);
        var changed = await workflow.UpdateAllocationAsync(draft.DraftId,
            new UpdateEcfDraftAllocationRequest(reviewed.Value!.Revision, 301m));
        Assert.True(changed.IsSuccess, changed.Error);
        Assert.Equal(301m, changed.Value!.TotalAmount);
        Assert.False(changed.Value.IsReviewedForCurrentRevision);
        var restored = await workflow.UpdateAllocationAsync(draft.DraftId,
            new UpdateEcfDraftAllocationRequest(changed.Value.Revision, 300m));
        Assert.True(restored.IsSuccess, restored.Error);
        Assert.False(restored.Value!.IsReviewedForCurrentRevision);
        reviewed = await workflow.ReviewAsync(draft.DraftId,
            new EcfDraftRevisionRequest(restored.Value.Revision));
        Assert.True(reviewed.IsSuccess, reviewed.Error);
        var operationId = Guid.NewGuid();
        var posted = await workflow.PostAsync(draft.DraftId,
            new PostEcfCollectionDraftRequest(reviewed.Value!.Revision, operationId));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(300m, posted.Value!.Amount);
        Assert.Equal("OR-0001", posted.Value.DocumentNumber);

        var collection = await context.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations).SingleAsync();
        Assert.Equal(300m, collection.TotalAmount);
        var line = Assert.Single(collection.Lines);
        Assert.Equal(300m, line.Amount);
        Assert.Equal(CollectionSourcePart.Electricity, line.SourcePart);
        var allocation = Assert.Single(line.Allocations);
        Assert.Equal(CollectionSourceKind.UtilityBill, allocation.SourceKind);
        Assert.Equal(seed.BillId, allocation.SourceId);
        Assert.Equal(CollectionSourcePart.Electricity, allocation.SourcePart);
        Assert.Equal(300m, allocation.Amount);
        using (var snapshot = System.Text.Json.JsonDocument.Parse(line.CalculationSnapshot!))
        {
            var facts = snapshot.RootElement;
            Assert.Equal(80m, facts.GetProperty("currentReading").GetDecimal());
            Assert.Equal(10m, facts.GetProperty("ratePerKwh").GetDecimal());
            Assert.Equal(seed.Period.Year, facts.GetProperty("billingYear").GetInt32());
            Assert.Equal(seed.Period.Month, facts.GetProperty("billingMonth").GetInt32());
        }

        var bill = await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(500m, bill.ElecAmountPaid);
        Assert.Equal(PaymentStatus.Partial, bill.ElecStatus);
        Assert.Equal("OR-0001", bill.ElecORNumber);
        Assert.Equal(7m, bill.WaterPartialAmount);
        Assert.Equal(PaymentStatus.Partial, bill.WaterStatus);
        Assert.Equal("LEGACY-W-001", bill.WaterORNumber);
        Assert.Equal(SettlementAuthority.Legacy, bill.WaterSettlementAuthorityState);

        var document = await context.AccountableDocuments.SingleAsync(x => x.Id == seed.OrDocumentId);
        Assert.Equal(AccountableDocumentState.Consumed, document.State);
        Assert.Equal(collection.Id, document.CollectionId);
        Assert.Equal(operationId, document.ClientOperationId);
        var operation = await context.PostingOperations.SingleAsync(x => x.ClientOperationId == operationId);
        Assert.Equal(PostingOperationStatus.Succeeded, operation.Status);
        Assert.Equal(collection.Id, operation.CollectionId);
        var savedDraft = await context.WebCollectionDrafts.SingleAsync(x => x.Id == draft.DraftId);
        Assert.Equal(CollectionDraftStatus.Posted, savedDraft.Status);
        Assert.Equal(collection.Id, savedDraft.CollectionId);

        var replay = await workflow.PostAsync(draft.DraftId,
            new PostEcfCollectionDraftRequest(savedDraft.Revision - 1, operationId));
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.True(replay.Value!.ReturnedExistingOutcome);
        Assert.Equal(collection.Id, replay.Value.CollectionId);
        Assert.Single(await context.Collections.ToListAsync());
        var differentOperation = await workflow.PostAsync(draft.DraftId,
            new PostEcfCollectionDraftRequest(savedDraft.Revision - 1, Guid.NewGuid()));
        Assert.True(differentOperation.IsSuccess, differentOperation.Error);
        Assert.Equal(collection.Id, differentOperation.Value!.CollectionId);
        var consumedOrReuse = await workflow.CreateDraftAsync(
            new CreateEcfCollectionDraftRequest(seed.BillId, 100m, seed.OrDocumentId));
        Assert.False(consumedOrReuse.IsSuccess);

        var activity = await workflow.GetActivityAsync(new DateOnly(seed.Period.Year, seed.Period.Month, 1),
            new DateOnly(seed.Period.Year, seed.Period.Month, 1).AddMonths(1).AddDays(-1));
        Assert.True(activity.IsSuccess, activity.Error);
        var activityRow = Assert.Single(activity.Value!);
        Assert.Equal(collection.Id, activityRow.CollectionId);
        Assert.Equal(1, activityRow.ItemCount);
        Assert.Equal(300m, activityRow.TotalAmount);
        Assert.Equal(seed.Period.Year, Assert.Single(activityRow.Lines).BillingYear);
    }

    [SkippableFact]
    public async Task LegacyEcfCanDraftAndReviewButCannotPostOrConsumeReceipt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: false);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);

        var quote = (await workflow.GetObligationAsync(seed.BillId)).Value!;
        Assert.Equal(SettlementAuthority.Legacy, quote.SettlementAuthority);
        Assert.False(quote.CanPostCanonical);
        var draftResult = await workflow.CreateDraftAsync(
            new CreateEcfCollectionDraftRequest(seed.BillId, 300m, seed.OrDocumentId));
        Assert.True(draftResult.IsSuccess, draftResult.Error);
        var reviewed = await workflow.ReviewAsync(draftResult.Value!.DraftId,
            new EcfDraftRevisionRequest(draftResult.Value.Revision));
        Assert.True(reviewed.IsSuccess, reviewed.Error);

        var operationId = Guid.NewGuid();
        var rejected = await workflow.PostAsync(reviewed.Value!.DraftId,
            new PostEcfCollectionDraftRequest(reviewed.Value.Revision, operationId));
        Assert.False(rejected.IsSuccess);
        Assert.Contains("Legacy", rejected.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.Collections.ToListAsync());
        Assert.Empty(await context.CollectionAllocations.ToListAsync());
        Assert.Empty(await context.CollectionSettlementCutovers.ToListAsync()); // No startup migration activates a source.
        var bill = await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(SettlementAuthority.Legacy, bill.ElectricitySettlementAuthorityState);
        Assert.Equal(200m, bill.ElecAmountPaid);
        Assert.Equal(7m, bill.WaterPartialAmount);
        Assert.Equal(AccountableDocumentState.InOffice,
            (await context.AccountableDocuments.SingleAsync(x => x.Id == seed.OrDocumentId)).State);
        Assert.Equal(PostingOperationStatus.Rejected,
            (await context.PostingOperations.SingleAsync(x => x.ClientOperationId == operationId)).Status);
    }

    [SkippableFact]
    public async Task StaleSourceVersionIsDurablyRejectedThenRefreshAndReviewAllowsNewAttempt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var user = new TestActor(seed.UserId, seed.TenantId);
        Guid draftId;
        long revision;
        var firstOperation = Guid.NewGuid();
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var workflow = new EcfCollectionWorkflow(context, user, new FixedTenant(seed.TenantId));
            var draft = (await workflow.CreateDraftAsync(
                new CreateEcfCollectionDraftRequest(seed.BillId, 300m, seed.OrDocumentId))).Value!;
            draftId = draft.DraftId;
            var reviewed = await workflow.ReviewAsync(draft.DraftId, new EcfDraftRevisionRequest(draft.Revision));
            Assert.True(reviewed.IsSuccess, reviewed.Error);
            revision = reviewed.Value!.Revision;
        }

        await using (var concurrent = db.CreateContext(seed.TenantId))
            await concurrent.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"UtilityBills\" SET \"ElectricitySourceVersion\" = \"ElectricitySourceVersion\" + 1 WHERE \"MunicipalityId\" = {seed.TenantId} AND \"Id\" = {seed.BillId}");

        await using (var context = db.CreateContext(seed.TenantId))
        {
            var workflow = new EcfCollectionWorkflow(context, user, new FixedTenant(seed.TenantId));
            var rejected = await workflow.PostAsync(draftId, new PostEcfCollectionDraftRequest(revision, firstOperation));
            Assert.False(rejected.IsSuccess);
            Assert.Contains("changed", rejected.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(PostingOperationStatus.Rejected,
                (await context.PostingOperations.SingleAsync(x => x.ClientOperationId == firstOperation)).Status);

            var refreshed = await workflow.UpdateAllocationAsync(draftId,
                new UpdateEcfDraftAllocationRequest(revision, 300m));
            Assert.True(refreshed.IsSuccess, refreshed.Error);
            Assert.False(refreshed.Value!.IsReviewedForCurrentRevision);
            var review = await workflow.ReviewAsync(draftId,
                new EcfDraftRevisionRequest(refreshed.Value.Revision));
            Assert.True(review.IsSuccess, review.Error);
            var posted = await workflow.PostAsync(draftId,
                new PostEcfCollectionDraftRequest(review.Value!.Revision, Guid.NewGuid()));
            Assert.True(posted.IsSuccess, posted.Error);
            Assert.Equal(1, await context.Collections.CountAsync());
        }
    }

    [SkippableFact]
    public async Task SameOperationRaceHasOneEffectAndChangedIntentConflicts()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var actor = new TestActor(seed.UserId, seed.TenantId);
        Guid draftId;
        long revision;
        Guid changedDraftId;
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var workflow = new EcfCollectionWorkflow(context, actor, new FixedTenant(seed.TenantId));
            var draft = (await workflow.CreateDraftAsync(
                new CreateEcfCollectionDraftRequest(seed.BillId, 600m, seed.OrDocumentId))).Value!;
            var reviewed = await workflow.ReviewAsync(draft.DraftId, new EcfDraftRevisionRequest(draft.Revision));
            Assert.True(reviewed.IsSuccess, reviewed.Error);
            draftId = draft.DraftId;
            revision = reviewed.Value!.Revision;
            var changedDraft = await workflow.CreateDraftAsync(
                new CreateEcfCollectionDraftRequest(seed.BillId, 100m, seed.OtherOrDocumentId));
            Assert.True(changedDraft.IsSuccess, changedDraft.Error);
            changedDraftId = changedDraft.Value!.DraftId;
        }

        var operationId = Guid.NewGuid();
        async Task<Result<EcfPostOutcomeDto>> AttemptAsync()
        {
            await using var context = db.CreateContext(seed.TenantId);
            var workflow = new EcfCollectionWorkflow(context, actor, new FixedTenant(seed.TenantId));
            return await workflow.PostAsync(draftId, new PostEcfCollectionDraftRequest(revision, operationId));
        }

        var outcomes = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        Assert.All(outcomes, result => Assert.True(result.IsSuccess, result.Error));
        Assert.Single(outcomes.Select(x => x.Value!.CollectionId).Distinct());
        await using (var verify = db.CreateContext(seed.TenantId))
        {
            Assert.Single(await verify.Collections.ToListAsync());
            Assert.Single(await verify.PostingOperations.ToListAsync(), x => x.ClientOperationId == operationId);
            Assert.Equal(800m, (await verify.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).ElecAmountPaid);
        }

        await using var changedContext = db.CreateContext(seed.TenantId);
        var changedWorkflow = new EcfCollectionWorkflow(changedContext, actor, new FixedTenant(seed.TenantId));
        var changed = await changedWorkflow.PostAsync(changedDraftId,
            new PostEcfCollectionDraftRequest(1, operationId));
        Assert.False(changed.IsSuccess);
        Assert.Contains("IDEMPOTENCY CONFLICT", changed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await changedContext.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task CompetingFullOutstandingAttemptsConsumeOnlyOneOrAndOneAllocation()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var actor = new TestActor(seed.UserId, seed.TenantId);
        var drafts = new List<(Guid Id, long Revision, Guid DocumentId)>();
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var workflow = new EcfCollectionWorkflow(context, actor, new FixedTenant(seed.TenantId));
            foreach (var documentId in new[] { seed.OrDocumentId, seed.OtherOrDocumentId })
            {
                var draft = (await workflow.CreateDraftAsync(
                    new CreateEcfCollectionDraftRequest(seed.BillId, 600m, documentId))).Value!;
                var reviewed = await workflow.ReviewAsync(draft.DraftId, new EcfDraftRevisionRequest(draft.Revision));
                Assert.True(reviewed.IsSuccess, reviewed.Error);
                drafts.Add((draft.DraftId, reviewed.Value!.Revision, documentId));
            }
        }

        async Task<Result<EcfPostOutcomeDto>> AttemptAsync((Guid Id, long Revision, Guid DocumentId) draft)
        {
            await using var context = db.CreateContext(seed.TenantId);
            var workflow = new EcfCollectionWorkflow(context, actor, new FixedTenant(seed.TenantId));
            return await workflow.PostAsync(draft.Id,
                new PostEcfCollectionDraftRequest(draft.Revision, Guid.NewGuid()));
        }

        var outcomes = await Task.WhenAll(drafts.Select(AttemptAsync));
        Assert.Single(outcomes, x => x.IsSuccess);
        Assert.Single(outcomes, x => !x.IsSuccess);
        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Single(await verify.Collections.ToListAsync());
        Assert.Single(await verify.CollectionAllocations.ToListAsync());
        Assert.Equal(800m, (await verify.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).ElecAmountPaid);
        var documents = await verify.AccountableDocuments.OrderBy(x => x.SerialNumber).ToListAsync();
        Assert.Single(documents, x => x.State == AccountableDocumentState.Consumed);
        Assert.Single(documents, x => x.State == AccountableDocumentState.InOffice);
    }

    [SkippableFact]
    public async Task OverOutstandingAndCashTicketDocumentsCannotEnterEcfDraft()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);

        var over = await workflow.CreateDraftAsync(new CreateEcfCollectionDraftRequest(seed.BillId, 601m));
        Assert.False(over.IsSuccess);
        var ct = await workflow.CreateDraftAsync(new CreateEcfCollectionDraftRequest(seed.BillId, 100m, seed.CtDocumentId));
        Assert.False(ct.IsSuccess);
        Assert.Empty(await context.WebCollectionDrafts.ToListAsync());
        Assert.Empty(await context.Collections.ToListAsync());
    }

    private EcfCollectionWorkflow Workflow(AppDbContext context, Seed seed) =>
        new(context, new TestActor(seed.UserId, seed.TenantId), new FixedTenant(seed.TenantId));

    private async Task<Seed> SeedEcfAsync(bool canonical)
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var period = PhilippineTime.Today;
        var periodStart = new DateOnly(period.Year, period.Month, 1);
        var municipality = Municipality.Create($"ECF-{Guid.NewGuid():N}", "ECF Test", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"ecf-{Guid.NewGuid():N}"[..32]);
        tenant = municipality.Id;
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM",
            archetype: BillingArchetype.DailyStall, municipalityId: tenant);
        var stall = Stall.Create(facility.Id, "ECF-01", 0m,
            ApplicableFees.Electricity | ApplicableFees.Water, MarketSection.FishSection,
            createdBy: "test", municipalityId: tenant);
        var payor = Payor.Create(tenant, "Lisa ECF", BusinessPayorKind.Person, "test");
        var contract = Contract.Create(stall.Id, "Lisa ECF", "Lisa ECF",
            periodStart.AddYears(-5), 20, 0m, createdBy: "test");
        contract.AssociatePayor(payor.Id, "test");
        var bill = UtilityBill.Create(stall.Id, period.Year, period.Month,
            0m, 80m, 10m, 0m, 4m, 5m, "test");
        bill.RecordPayment("LEGACY-E-001", "LEGACY-W-001", null,
            PaymentStatus.Partial, 200m, PaymentStatus.Partial, 7m, updatedBy: "test");

        var classification = RevenueClassification.Create(RevenueClassificationCodes.Ecf, tenant);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1),
            "Electricity Consumption Fee", RevenueInstrumentType.OfficialReceipt, tenant);
        var receivedAt = DateTime.UtcNow.AddMinutes(-5);
        var orBook = AccountableFormBook.Receive(tenant, RevenueInstrumentType.OfficialReceipt,
            "ECF TEST OR", "OR-", 1, 99, 4, receivedAt, user.ToString("N"), "test");
        var orDocument = AccountableDocument.Register(orBook, 1, "test");
        var secondOrDocument = AccountableDocument.Register(orBook, 2, "test");
        var ctBook = AccountableFormBook.Receive(tenant, RevenueInstrumentType.CashTicket,
            "ECF TEST CT", "CT-", 1, 99, 4, receivedAt, user.ToString("N"), "test");
        var ctDocument = AccountableDocument.Register(ctBook, 1, "test");

        CollectionSettlementCutover? cutover = null;
        if (canonical)
        {
            bill.MarkElectricityPendingCutover();
            var settled = bill.ElecAmountPaid;
            var cutoverAt = DateTime.UtcNow.AddMinutes(-2);
            cutover = CollectionSettlementCutover.Freeze(tenant, CollectionSourceKind.UtilityBill,
                bill.Id, CollectionSourcePart.Electricity, bill.ElectricitySourceVersion, cutoverAt,
                bill.ElecCharge, settled, bill.ElecCharge - settled,
                "{\"pendingMobileOperations\":0,\"issuedDocuments\":0,\"legacyWritersQuiesced\":true}",
                user, cutoverAt.AddMinutes(1));
            bill.ActivateCanonicalElectricitySettlement(cutover);
        }

        await using (var context = db.CreateContext(tenant))
        {
            context.AddRange(facility, stall, payor, contract, bill, classification, policy,
                orBook, orDocument, secondOrDocument, ctBook, ctDocument);
            if (cutover is not null) context.CollectionSettlementCutovers.Add(cutover);
            await context.SaveChangesAsync();
        }

        return new Seed(tenant, user, bill.Id, orDocument.Id, secondOrDocument.Id, ctDocument.Id, periodStart);
    }
}
