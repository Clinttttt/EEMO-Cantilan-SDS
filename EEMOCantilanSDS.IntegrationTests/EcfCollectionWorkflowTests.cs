using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
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
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class EcfCollectionWorkflowTests(PostgresFixture db)
{
    private sealed record Seed(Guid TenantId, Guid UserId, Guid BillId, Guid PayorId, Guid OrDocumentId,
        Guid OtherOrDocumentId, Guid CtDocumentId, DateOnly Period);
    private sealed record RentSeed(Guid StallId, Guid ContractId, Guid PayorId, Guid ClassificationId,
        IReadOnlyDictionary<DateOnly, Guid> PaymentRecordIds);

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

    private sealed class MutableTestClock(DateOnly today) : IClock
    {
        public DateOnly PhilippineToday { get; set; } = today;
        public DateTime UtcNow => DateTime.SpecifyKind(PhilippineToday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        public DateTime PhilippineNow => DateTime.SpecifyKind(
            PhilippineToday.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
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
        Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", posted.Value.ReferenceCode);

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
        Assert.Equal("LEGACY-E-001", bill.ElecORNumber);   // the legacy document field is left as it was; the SRC is on the Collection
        Assert.Equal(7m, bill.WaterPartialAmount);
        Assert.Equal(PaymentStatus.Partial, bill.WaterStatus);
        Assert.Equal("LEGACY-W-001", bill.WaterORNumber);
        Assert.Equal(SettlementAuthority.Legacy, bill.WaterSettlementAuthorityState);

        // IA-062: no physical form is consumed; the Collection is identified by its server-generated SRC.
        var document = await context.AccountableDocuments.SingleAsync(x => x.Id == seed.OrDocumentId);
        Assert.Equal(AccountableDocumentState.InOffice, document.State);
        Assert.Null(document.CollectionId);
        Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", collection.ReferenceCode);
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
    public async Task APhysicalFormReportedLostAfterReview_DoesNotAffectPosting_BecauseNoFormIsNeeded()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);
        var draft = await workflow.CreateDraftAsync(new CreateEcfCollectionDraftRequest(seed.BillId, 100m, seed.OrDocumentId));
        Assert.True(draft.IsSuccess, draft.Error);
        var reviewed = (await workflow.ReviewAsync(draft.Value!.DraftId, new EcfDraftRevisionRequest(draft.Value.Revision))).Value!;

        // A physical form turns out to be missing before the money is posted. A collection no longer depends on any form.
        await using (var office = db.CreateContext(seed.TenantId))
        {
            var custody = new AccountableFormCustodyWorkflow(office, new TestActor(seed.UserId, seed.TenantId), new FixedTenant(seed.TenantId));
            var book = await office.AccountableDocuments.Where(x => x.Id == seed.OrDocumentId).Select(x => new { x.FormBookId, x.SerialNumber }).SingleAsync();
            var loss = await custody.ReportLossAsync(new ReportFormLossRequest(book.FormBookId, book.SerialNumber, book.SerialNumber,
                AccountableFormCopies.WholeSet, PhilippineTime.Today, "Office", "Missing from the booklet"));
            Assert.True(loss.IsSuccess, loss.Error);
        }

        await using var fresh = db.CreateContext(seed.TenantId);
        var posted = await Workflow(fresh, seed).PostAsync(reviewed.DraftId, new PostEcfCollectionDraftRequest(reviewed.Revision, Guid.NewGuid()));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Single(await fresh.Collections.ToListAsync());
        Assert.Equal(AccountableDocumentState.Lost, (await fresh.AccountableDocuments.SingleAsync(x => x.Id == seed.OrDocumentId)).State);
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
    public async Task CompetingFullOutstandingAttemptsPostOnlyOneCollectionAndOneAllocation()
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
        var orDocuments = documents.Where(x => x.InstrumentType == RevenueInstrumentType.OfficialReceipt).ToList();
        Assert.All(orDocuments, x => Assert.Equal(AccountableDocumentState.InOffice, x.State));   // no physical form is consumed (IA-062)
        Assert.Single(documents, x => x.InstrumentType == RevenueInstrumentType.CashTicket
            && x.State == AccountableDocumentState.InOffice);
    }

    [SkippableFact]
    public async Task OverOutstandingCannotEnterEcfDraft_AndASuppliedLegacyDocumentIsIgnored()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);

        var over = await workflow.CreateDraftAsync(new CreateEcfCollectionDraftRequest(seed.BillId, 601m));
        Assert.False(over.IsSuccess);
        var ct = await workflow.CreateDraftAsync(new CreateEcfCollectionDraftRequest(seed.BillId, 100m, seed.CtDocumentId));
        Assert.True(ct.IsSuccess, ct.Error);   // a physical document id is ignored (IA-062), never validated
        Assert.Single(await context.WebCollectionDrafts.ToListAsync());
        Assert.Empty(await context.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task RentAdapterUsesRentalComponentAndMixedLegacyPartialRequiresReconciliation()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: false);
        var period = seed.Period.AddMonths(-1);
        var rent = await SeedRentSourcesAsync(seed, [period], canonical: false,
            partialAmount: 100m, legacyElectricityAmount: 125m, legacyFishKilos: 5m);

        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);
        var result = await workflow.GetRentObligationAsync(rent.StallId, period.Year, period.Month);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(900m, result.Value!.AssessedRentalAmount);
        Assert.Equal(1030m, await context.PaymentRecords.Where(x => x.Id == rent.PaymentRecordIds[period])
            .Select(x => x.TotalBill).SingleAsync());
        Assert.True(result.Value.RequiresLegacyReconciliation);
        Assert.False(result.Value.CanAddToDraft);
        Assert.Equal(SettlementAuthority.Legacy, result.Value.SettlementAuthority);
        Assert.Empty(await context.CollectionSettlementCutovers.ToListAsync());
    }

    [SkippableFact]
    public async Task RentPeriodsAndEcfShareOneReviewedOrWithIndependentLinesAndAllocations()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var july = seed.Period.AddMonths(-2);
        var august = seed.Period.AddMonths(-1);
        var rent = await SeedRentSourcesAsync(seed, [july, august], canonical: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);

        var candidates = await workflow.GetPayorObligationsAsync(seed.PayorId);
        Assert.True(candidates.IsSuccess, candidates.Error);
        var rentCandidates = candidates.Value!.Where(x => x.SourceKind == CollectionSourceKind.PaymentRecord).ToList();
        Assert.Equal(new[] { july, august, seed.Period },
            rentCandidates.Select(x => new DateOnly(x.BillingYear, x.BillingMonth, 1)));
        Assert.All(rentCandidates, x => Assert.Equal(seed.PayorId, x.PayorId));

        var over = await workflow.AddRentAllocationAsync(
            new AddRentDraftAllocationRequest(rent.StallId, july.Year, july.Month, 900.01m));
        Assert.False(over.IsSuccess);
        Assert.Empty(await context.WebCollectionDrafts.ToListAsync());

        var julyDraft = await workflow.AddRentAllocationAsync(
            new AddRentDraftAllocationRequest(rent.StallId, july.Year, july.Month, 900m));
        Assert.True(julyDraft.IsSuccess, julyDraft.Error);
        var augustDraft = await workflow.AddRentAllocationAsync(
            new AddRentDraftAllocationRequest(rent.StallId, august.Year, august.Month, 600m,
                julyDraft.Value!.Revision));
        Assert.True(augustDraft.IsSuccess, augustDraft.Error);
        var ecfDraft = await workflow.AddEcfLineAsync(
            new AddEcfDraftLineRequest(seed.BillId, 500m, augustDraft.Value!.Revision));
        Assert.True(ecfDraft.IsSuccess, ecfDraft.Error);
        Assert.Equal(seed.PayorId, ecfDraft.Value!.PayorId);
        Assert.Equal(2000m, ecfDraft.Value.TotalAmount);
        Assert.Equal(2, ecfDraft.Value.Lines.Count);
        Assert.Contains("kWh", ecfDraft.Value.Lines.Single(x =>
            x.ClassificationName == "Electricity Consumption Fee").CalculationDetail);

        var selected = await workflow.SelectDocumentAsync(ecfDraft.Value.DraftId,
            new SelectEcfDraftDocumentRequest(ecfDraft.Value.Revision, seed.OrDocumentId));
        Assert.True(selected.IsSuccess, selected.Error);
        var reviewed = await workflow.ReviewAsync(selected.Value!.DraftId,
            new EcfDraftRevisionRequest(selected.Value.Revision));
        Assert.True(reviewed.IsSuccess, reviewed.Error);
        var operationId = Guid.NewGuid();
        var posted = await workflow.PostAsync(reviewed.Value!.DraftId,
            new PostEcfCollectionDraftRequest(reviewed.Value.Revision, operationId));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(2000m, posted.Value!.Amount);
        Assert.Equal(2, posted.Value.ItemCount);

        var collection = await context.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations).SingleAsync();
        Assert.Equal(2000m, collection.TotalAmount);
        Assert.Equal(seed.PayorId, collection.PayorId);
        Assert.Equal(2, collection.Lines.Count);
        var rentLine = Assert.Single(collection.Lines, x => x.RevenueClassificationId == rent.ClassificationId);
        Assert.Equal(1500m, rentLine.Amount);
        Assert.Equal(new[] { 900m, 600m }, rentLine.Allocations.OrderBy(x => x.SourceId)
            .Select(x => x.Amount).OrderByDescending(x => x).ToArray());
        Assert.Equal(2, rentLine.Allocations.Count);
        Assert.All(rentLine.Allocations, x =>
        {
            Assert.Equal(CollectionSourceKind.PaymentRecord, x.SourceKind);
            Assert.Null(x.SourcePart);
        });
        var ecfClassificationId = await context.RevenueClassifications.Where(x =>
            x.SemanticCode == RevenueClassificationCodes.Ecf).Select(x => x.Id).SingleAsync();
        var ecfLine = Assert.Single(collection.Lines, x => x.RevenueClassificationId == ecfClassificationId);
        Assert.Equal(500m, ecfLine.Amount);
        Assert.Equal(CollectionSourcePart.Electricity, ecfLine.SourcePart);
        Assert.Equal(seed.BillId, Assert.Single(ecfLine.Allocations).SourceId);

        var activity = await workflow.GetActivityAsync(seed.Period, seed.Period.AddMonths(1).AddDays(-1));
        Assert.True(activity.IsSuccess, activity.Error);
        var row = Assert.Single(activity.Value!);
        Assert.Equal(collection.Id, row.CollectionId);
        Assert.Equal(2, row.ItemCount);
        Assert.Equal(2000m, row.TotalAmount);
        Assert.Equal(2, row.Lines.Count);
        Assert.Equal(2, row.Lines.Single(x => x.ClassificationName == "Permanent Stall Rent").Allocations!.Count);
        Assert.Equal(500m, row.Lines.Single(x => x.ClassificationName == "Electricity Consumption Fee").Amount);
        Assert.Contains("kWh", row.Lines.Single(x =>
            x.ClassificationName == "Electricity Consumption Fee").CalculationDetail);
        Assert.Equal(AccountableDocumentState.InOffice,
            (await context.AccountableDocuments.SingleAsync(x => x.Id == seed.OrDocumentId)).State);

        var julyRecord = await context.PaymentRecords.SingleAsync(x => x.Id == rent.PaymentRecordIds[july]);
        var augustRecord = await context.PaymentRecords.SingleAsync(x => x.Id == rent.PaymentRecordIds[august]);
        Assert.Equal(PaymentStatus.Paid, julyRecord.Status);
        Assert.Equal(PaymentStatus.Partial, augustRecord.Status);
        Assert.Equal(600m, augustRecord.PartialAmount);
        Assert.Equal(SettlementAuthority.Canonical, julyRecord.SettlementAuthorityState);
        Assert.Equal(SettlementAuthority.Canonical, augustRecord.SettlementAuthorityState);
        Assert.Equal(SettlementAuthority.Canonical,
            (await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).ElectricitySettlementAuthorityState);
        Assert.Equal(SettlementAuthority.Legacy,
            (await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).WaterSettlementAuthorityState);
    }

    [SkippableFact]
    public async Task AllocationEditsPreserveOrRemoveOnlyTheIntendedCollectionLine()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var july = seed.Period.AddMonths(-2);
        var august = seed.Period.AddMonths(-1);
        var rent = await SeedRentSourcesAsync(seed, [july, august], canonical: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);

        var ecf = await workflow.CreateDraftAsync(
            new CreateEcfCollectionDraftRequest(seed.BillId, 500m, seed.OrDocumentId));
        Assert.True(ecf.IsSuccess, ecf.Error);
        var ecfAllocation = Assert.Single(Assert.Single(ecf.Value!.Lines).Allocations!);
        var reducedEcf = await workflow.UpdateDraftAllocationAsync(ecf.Value.DraftId,
            new UpdateCollectionDraftAllocationRequest(ecfAllocation.AllocationId, ecf.Value.Revision, 400m));
        Assert.True(reducedEcf.IsSuccess, reducedEcf.Error);
        Assert.Equal(ecf.Value.Revision + 1, reducedEcf.Value!.Revision);
        Assert.Equal(400m, reducedEcf.Value.TotalAmount);
        Assert.Equal(400m, Assert.Single(reducedEcf.Value.Lines).Amount);
        Assert.False(reducedEcf.Value.IsReviewedForCurrentRevision);

        var removedEcf = await workflow.UpdateDraftAllocationAsync(reducedEcf.Value.DraftId,
            new UpdateCollectionDraftAllocationRequest(ecfAllocation.AllocationId, reducedEcf.Value.Revision, 0m));
        Assert.True(removedEcf.IsSuccess, removedEcf.Error);
        Assert.Empty(removedEcf.Value!.Lines);
        Assert.Equal(0m, removedEcf.Value.TotalAmount);

        var rentDraft = await workflow.AddRentAllocationAsync(
            new AddRentDraftAllocationRequest(rent.StallId, july.Year, july.Month, 900m, removedEcf.Value.Revision));
        Assert.True(rentDraft.IsSuccess, rentDraft.Error);
        var julyAllocation = Assert.Single(Assert.Single(rentDraft.Value!.Lines).Allocations!);
        var reducedRent = await workflow.UpdateDraftAllocationAsync(rentDraft.Value.DraftId,
            new UpdateCollectionDraftAllocationRequest(julyAllocation.AllocationId, rentDraft.Value.Revision, 600m));
        Assert.True(reducedRent.IsSuccess, reducedRent.Error);
        Assert.Equal(600m, Assert.Single(reducedRent.Value!.Lines).Amount);

        var withAugust = await workflow.AddRentAllocationAsync(
            new AddRentDraftAllocationRequest(rent.StallId, august.Year, august.Month, 300m,
                reducedRent.Value.Revision));
        Assert.True(withAugust.IsSuccess, withAugust.Error);
        var rentLine = Assert.Single(withAugust.Value!.Lines);
        Assert.Equal(900m, rentLine.Amount);
        var julyAllocationInDraft = Assert.Single(rentLine.Allocations!, x => x.BillingMonth == july.Month);
        var withoutJuly = await workflow.UpdateDraftAllocationAsync(withAugust.Value.DraftId,
            new UpdateCollectionDraftAllocationRequest(julyAllocationInDraft.AllocationId,
                withAugust.Value.Revision, 0m));
        Assert.True(withoutJuly.IsSuccess, withoutJuly.Error);
        var remainingRentLine = Assert.Single(withoutJuly.Value!.Lines);
        Assert.Equal(300m, remainingRentLine.Amount);
        Assert.Equal(300m, Assert.Single(remainingRentLine.Allocations!).Amount);
        Assert.False(withoutJuly.Value.IsReviewedForCurrentRevision);
        Assert.Empty(await context.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task DraftDateRefreshIsExplicitAndRentPolicyUsesTheDraftDateUntilRefresh()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var rentPeriod = seed.Period.AddMonths(-1);
        var rent = await SeedRentSourcesAsync(seed, [rentPeriod], canonical: true);
        var draftDate = PhilippineTime.Today.AddDays(-2);
        var nextDate = draftDate.AddDays(1);
        var clock = new MutableTestClock(draftDate);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed, clock);

        var added = await workflow.AddRentAllocationAsync(new AddRentDraftAllocationRequest(
            rent.StallId, rentPeriod.Year, rentPeriod.Month, 300m));
        Assert.True(added.IsSuccess, added.Error);
        var selected = await workflow.SelectDocumentAsync(added.Value!.DraftId,
            new SelectEcfDraftDocumentRequest(added.Value.Revision, seed.OrDocumentId));
        Assert.True(selected.IsSuccess, selected.Error);
        var reviewed = await workflow.ReviewAsync(selected.Value!.DraftId,
            new EcfDraftRevisionRequest(selected.Value.Revision));
        Assert.True(reviewed.IsSuccess, reviewed.Error);

        var sameDay = await workflow.ResumeDraftAsync(reviewed.Value!.DraftId,
            new EcfDraftRevisionRequest(reviewed.Value.Revision));
        Assert.True(sameDay.IsSuccess, sameDay.Error);
        Assert.Equal(reviewed.Value.Revision, sameDay.Value!.Revision);
        Assert.True(sameDay.Value.IsReviewedForCurrentRevision);

        var rentPolicy = RevenueClassificationPolicy.Create(rent.ClassificationId, nextDate,
            "Permanent Stall Rent Updated", RevenueInstrumentType.OfficialReceipt, seed.TenantId);
        context.RevenueClassificationPolicies.Add(rentPolicy);
        await context.SaveChangesAsync();

        clock.PhilippineToday = nextDate;
        var oldReviewPost = await workflow.PostAsync(reviewed.Value.DraftId,
            new PostEcfCollectionDraftRequest(reviewed.Value.Revision, Guid.NewGuid()));
        Assert.False(oldReviewPost.IsSuccess);
        Assert.Contains("earlier Philippine business date", oldReviewPost.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.Collections.ToListAsync());

        var refreshed = await workflow.ResumeDraftAsync(reviewed.Value.DraftId,
            new EcfDraftRevisionRequest(reviewed.Value.Revision));
        Assert.True(refreshed.IsSuccess, refreshed.Error);
        Assert.Equal(nextDate, refreshed.Value!.BusinessDate);
        Assert.Equal(reviewed.Value.Revision + 1, refreshed.Value.Revision);
        Assert.False(refreshed.Value.IsReviewedForCurrentRevision);
        Assert.Equal("Permanent Stall Rent Updated", Assert.Single(refreshed.Value.Lines).ClassificationName);
        Assert.False(refreshed.Value.RequiresBusinessDateRefresh);
        var renewedReview = await workflow.ReviewAsync(refreshed.Value.DraftId,
            new EcfDraftRevisionRequest(refreshed.Value.Revision));
        Assert.True(renewedReview.IsSuccess, renewedReview.Error);
        Assert.True(renewedReview.Value!.IsReviewedForCurrentRevision);
        Assert.Empty(await context.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task FailedDraftDateRefreshLeavesDraftAndFinancialStateUnchanged()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: false);
        var originalDate = PhilippineTime.Today.AddDays(-2);
        var clock = new MutableTestClock(originalDate);
        Guid draftId;
        long revision;
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var workflow = Workflow(context, seed, clock);
            var draft = await workflow.CreateDraftAsync(
                new CreateEcfCollectionDraftRequest(seed.BillId, 100m, seed.OrDocumentId));
            Assert.True(draft.IsSuccess, draft.Error);
            var reviewed = await workflow.ReviewAsync(draft.Value!.DraftId,
                new EcfDraftRevisionRequest(draft.Value.Revision));
            Assert.True(reviewed.IsSuccess, reviewed.Error);
            draftId = reviewed.Value!.DraftId;
            revision = reviewed.Value.Revision;
        }

        await using (var mutation = db.CreateContext(seed.TenantId))
        {
            var bill = await mutation.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
            bill.MarkElectricityPendingCutover();
            await mutation.SaveChangesAsync();
        }

        clock.PhilippineToday = originalDate.AddDays(1);
        await using var resumeContext = db.CreateContext(seed.TenantId);
        var failed = await Workflow(resumeContext, seed, clock).ResumeDraftAsync(draftId,
            new EcfDraftRevisionRequest(revision));
        Assert.False(failed.IsSuccess);
        Assert.Contains("pending cutover", failed.Error, StringComparison.OrdinalIgnoreCase);
        var persisted = await resumeContext.WebCollectionDrafts.AsNoTracking().SingleAsync(x => x.Id == draftId);
        Assert.Equal(originalDate, persisted.BusinessDate);
        Assert.Equal(revision, persisted.Revision);
        Assert.Equal(revision, persisted.ReviewedRevision);
        Assert.Empty(await resumeContext.Collections.ToListAsync());
        Assert.Empty(await resumeContext.CollectionAllocations.ToListAsync());
        Assert.Equal(AccountableDocumentState.InOffice,
            (await resumeContext.AccountableDocuments.SingleAsync(x => x.Id == seed.OrDocumentId)).State);
        Assert.Equal(200m,
            (await resumeContext.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).ElecAmountPaid);
    }

    [SkippableFact]
    public async Task EcfActivityFiltersParentsByEcfLineWhileSharedActivityReturnsAllCollections()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var firstRentPeriod = seed.Period.AddMonths(-3);
        var combinedRentPeriod = seed.Period.AddMonths(-2);
        var rentOnlyPeriod = seed.Period.AddMonths(-1);
        var rent = await SeedRentSourcesAsync(seed,
            [firstRentPeriod, combinedRentPeriod, rentOnlyPeriod], canonical: true);
        var now = DateTime.UtcNow;
        var extraBook = AccountableFormBook.Receive(seed.TenantId, RevenueInstrumentType.OfficialReceipt,
            "Activity test OR book", "ACT-OR-", 1, 10, 4, now, seed.UserId.ToString("N"), "test");
        var extraDocument = AccountableDocument.Register(extraBook, 1, "test");
        await using (var setup = db.CreateContext(seed.TenantId))
        {
            setup.AddRange(extraBook, extraDocument);
            await setup.SaveChangesAsync();
        }

        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);
        async Task<EcfPostOutcomeDto> PostAsync(EcfCollectionDraftDto draft, Guid documentId)
        {
            var selected = await workflow.SelectDocumentAsync(draft.DraftId,
                new SelectEcfDraftDocumentRequest(draft.Revision, documentId));
            Assert.True(selected.IsSuccess, selected.Error);
            var reviewed = await workflow.ReviewAsync(draft.DraftId,
                new EcfDraftRevisionRequest(selected.Value!.Revision));
            Assert.True(reviewed.IsSuccess, reviewed.Error);
            var posted = await workflow.PostAsync(draft.DraftId,
                new PostEcfCollectionDraftRequest(reviewed.Value!.Revision, Guid.NewGuid()));
            Assert.True(posted.IsSuccess, posted.Error);
            return posted.Value!;
        }

        var rentFirst = await workflow.AddRentAllocationAsync(new AddRentDraftAllocationRequest(
            rent.StallId, combinedRentPeriod.Year, combinedRentPeriod.Month, 100m));
        Assert.True(rentFirst.IsSuccess, rentFirst.Error);
        var combined = await workflow.AddEcfLineAsync(new AddEcfDraftLineRequest(
            seed.BillId, 200m, rentFirst.Value!.Revision));
        Assert.True(combined.IsSuccess, combined.Error);
        var combinedOutcome = await PostAsync(combined.Value!, seed.OrDocumentId);

        var ecfOnly = await workflow.AddEcfLineAsync(new AddEcfDraftLineRequest(seed.BillId, 100m));
        Assert.True(ecfOnly.IsSuccess, ecfOnly.Error);
        var ecfOnlyOutcome = await PostAsync(ecfOnly.Value!, seed.OtherOrDocumentId);

        var rentOnly = await workflow.AddRentAllocationAsync(new AddRentDraftAllocationRequest(
            rent.StallId, rentOnlyPeriod.Year, rentOnlyPeriod.Month, 100m));
        Assert.True(rentOnly.IsSuccess, rentOnly.Error);
        var rentOnlyOutcome = await PostAsync(rentOnly.Value!, extraDocument.Id);

        var from = seed.Period;
        var to = from.AddMonths(1).AddDays(-1);
        var ecfActivity = await workflow.GetEcfActivityAsync(from, to);
        var allActivity = await workflow.GetActivityAsync(from, to);
        Assert.True(ecfActivity.IsSuccess, ecfActivity.Error);
        Assert.True(allActivity.IsSuccess, allActivity.Error);
        Assert.Equal(3, allActivity.Value!.Count);
        Assert.Equal(2, ecfActivity.Value!.Count);
        Assert.Contains(ecfActivity.Value, x => x.CollectionId == combinedOutcome.CollectionId);
        Assert.Contains(ecfActivity.Value, x => x.CollectionId == ecfOnlyOutcome.CollectionId);
        Assert.DoesNotContain(ecfActivity.Value, x => x.CollectionId == rentOnlyOutcome.CollectionId);
        var combinedParent = Assert.Single(ecfActivity.Value, x => x.CollectionId == combinedOutcome.CollectionId);
        Assert.Equal(2, combinedParent.ItemCount);
        Assert.Equal(300m, combinedParent.TotalAmount);
    }

    [SkippableFact]
    public async Task SameDisplayNameDoesNotJoinRentToAnUnrelatedEcfPayor()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var period = seed.Period.AddMonths(-1);
        var unrelatedPayor = Payor.Create(seed.TenantId, "Lisa ECF", BusinessPayorKind.Person, "test");
        var rent = await SeedRentSourcesAsync(seed, [period], canonical: true, payorId: unrelatedPayor.Id,
            additionalPayor: unrelatedPayor);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);
        var draft = await workflow.AddEcfLineAsync(new AddEcfDraftLineRequest(seed.BillId, 100m));
        Assert.True(draft.IsSuccess, draft.Error);
        var rejected = await workflow.AddRentAllocationAsync(new AddRentDraftAllocationRequest(
            rent.StallId, period.Year, period.Month, 100m, draft.Value!.Revision));
        Assert.False(rejected.IsSuccess);
        Assert.Contains("Payor", rejected.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(seed.PayorId, draft.Value.PayorId);
        Assert.Equal(1, await context.WebCollectionDraftLines.CountAsync());
        Assert.Empty(await context.WebCollectionDraftAllocations
            .Where(x => x.SourceKind == CollectionSourceKind.PaymentRecord).ToListAsync());
        Assert.Empty(await context.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task PayorFirstComposerFindsOlderStillOwedEcfPeriod()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: false);
        var olderPeriod = seed.Period.AddMonths(-4);
        Guid stallId;
        await using (var lookup = db.CreateContext(seed.TenantId))
            stallId = await lookup.UtilityBills.Where(x => x.Id == seed.BillId).Select(x => x.StallId).SingleAsync();
        var oldBill = UtilityBill.Create(stallId, olderPeriod.Year, olderPeriod.Month,
            20m, 80m, 10m, 0m, 4m, 5m, "test");
        await using (var setup = db.CreateContext(seed.TenantId))
        {
            setup.UtilityBills.Add(oldBill);
            await setup.SaveChangesAsync();
        }

        await using var context = db.CreateContext(seed.TenantId);
        var candidates = await Workflow(context, seed).GetPayorObligationsAsync(seed.PayorId);
        Assert.True(candidates.IsSuccess, candidates.Error);
        var oldEcf = Assert.Single(candidates.Value!, x => x.SourceKind == CollectionSourceKind.UtilityBill
            && x.SourcePart == CollectionSourcePart.Electricity && x.SourceId == oldBill.Id);
        Assert.Equal(olderPeriod.Year, oldEcf.BillingYear);
        Assert.Equal(olderPeriod.Month, oldEcf.BillingMonth);
        Assert.Equal(600m, oldEcf.OutstandingAmount);
        Assert.True(oldEcf.CanAddToDraft);
    }

    [SkippableFact]
    public async Task StaleRentSourceRejectsTheWholeReviewedRentAndEcfCollection()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var period = seed.Period.AddMonths(-1);
        var rent = await SeedRentSourcesAsync(seed, [period], canonical: true);
        var actor = new TestActor(seed.UserId, seed.TenantId);
        Guid draftId;
        long revision;
        var operationId = Guid.NewGuid();
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var workflow = new CollectionComposerWorkflow(context, actor, new FixedTenant(seed.TenantId));
            var rentDraft = await workflow.AddRentAllocationAsync(new AddRentDraftAllocationRequest(
                rent.StallId, period.Year, period.Month, 300m));
            Assert.True(rentDraft.IsSuccess, rentDraft.Error);
            var ecfDraft = await workflow.AddEcfLineAsync(new AddEcfDraftLineRequest(
                seed.BillId, 300m, rentDraft.Value!.Revision));
            Assert.True(ecfDraft.IsSuccess, ecfDraft.Error);
            var selected = await workflow.SelectDocumentAsync(ecfDraft.Value!.DraftId,
                new SelectEcfDraftDocumentRequest(ecfDraft.Value.Revision, seed.OrDocumentId));
            Assert.True(selected.IsSuccess, selected.Error);
            var reviewed = await workflow.ReviewAsync(selected.Value!.DraftId,
                new EcfDraftRevisionRequest(selected.Value.Revision));
            Assert.True(reviewed.IsSuccess, reviewed.Error);
            draftId = reviewed.Value!.DraftId;
            revision = reviewed.Value.Revision;
        }

        await using (var mutation = db.CreateContext(seed.TenantId))
            await mutation.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"PaymentRecords\" SET \"SettlementVersion\" = \"SettlementVersion\" + 1 WHERE \"MunicipalityId\" = {seed.TenantId} AND \"Id\" = {rent.PaymentRecordIds[period]}");

        await using var postingContext = db.CreateContext(seed.TenantId);
        var posting = new CollectionComposerWorkflow(postingContext, actor, new FixedTenant(seed.TenantId));
        var rejected = await posting.PostAsync(draftId,
            new PostEcfCollectionDraftRequest(revision, operationId));
        Assert.False(rejected.IsSuccess);
        Assert.Contains("Rent source facts changed", rejected.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await postingContext.Collections.ToListAsync());
        Assert.Empty(await postingContext.CollectionAllocations.ToListAsync());
        Assert.Equal(AccountableDocumentState.InOffice,
            (await postingContext.AccountableDocuments.SingleAsync(x => x.Id == seed.OrDocumentId)).State);
        Assert.Equal(200m, (await postingContext.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).ElecAmountPaid);
        Assert.Equal(0m, (await postingContext.PaymentRecords.SingleAsync(x => x.Id == rent.PaymentRecordIds[period])).PartialAmount);
        Assert.Equal(CollectionDraftStatus.Draft,
            (await postingContext.WebCollectionDrafts.SingleAsync(x => x.Id == draftId)).Status);
        Assert.Equal(PostingOperationStatus.Rejected,
            (await postingContext.PostingOperations.SingleAsync(x => x.ClientOperationId == operationId)).Status);
    }

    [SkippableFact]
    public async Task ConcurrentIdenticalCombinedRentAndEcfPostingHasOneFinancialEffect()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var period = seed.Period.AddMonths(-1);
        var rent = await SeedRentSourcesAsync(seed, [period], canonical: true);
        var actor = new TestActor(seed.UserId, seed.TenantId);
        Guid draftId;
        long revision;
        var operationId = Guid.NewGuid();
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var workflow = new CollectionComposerWorkflow(context, actor, new FixedTenant(seed.TenantId));
            var rentDraft = await workflow.AddRentAllocationAsync(new AddRentDraftAllocationRequest(
                rent.StallId, period.Year, period.Month, 100m));
            Assert.True(rentDraft.IsSuccess, rentDraft.Error);
            var ecfDraft = await workflow.AddEcfLineAsync(new AddEcfDraftLineRequest(
                seed.BillId, 100m, rentDraft.Value!.Revision));
            Assert.True(ecfDraft.IsSuccess, ecfDraft.Error);
            var selected = await workflow.SelectDocumentAsync(ecfDraft.Value!.DraftId,
                new SelectEcfDraftDocumentRequest(ecfDraft.Value.Revision, seed.OrDocumentId));
            Assert.True(selected.IsSuccess, selected.Error);
            var reviewed = await workflow.ReviewAsync(selected.Value!.DraftId,
                new EcfDraftRevisionRequest(selected.Value.Revision));
            Assert.True(reviewed.IsSuccess, reviewed.Error);
            draftId = reviewed.Value!.DraftId;
            revision = reviewed.Value.Revision;
        }

        async Task<Result<EcfPostOutcomeDto>> AttemptAsync()
        {
            await using var context = db.CreateContext(seed.TenantId);
            var workflow = new CollectionComposerWorkflow(context, actor, new FixedTenant(seed.TenantId));
            return await workflow.PostAsync(draftId,
                new PostEcfCollectionDraftRequest(revision, operationId));
        }

        var outcomes = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        Assert.All(outcomes, x => Assert.True(x.IsSuccess, x.Error));
        Assert.Single(outcomes.Select(x => x.Value!.CollectionId).Distinct());
        await using var verify = db.CreateContext(seed.TenantId);
        var collection = await verify.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations).SingleAsync();
        Assert.Equal(200m, collection.TotalAmount);
        Assert.Equal(2, collection.Lines.Count);
        Assert.Equal(2, collection.Lines.Sum(x => x.Allocations.Count));
        Assert.Single(await verify.PostingOperations.ToListAsync(), x => x.ClientOperationId == operationId);
        Assert.Equal(AccountableDocumentState.InOffice,
            (await verify.AccountableDocuments.SingleAsync(x => x.Id == seed.OrDocumentId)).State);
        Assert.Equal(100m, (await verify.PaymentRecords.SingleAsync(x => x.Id == rent.PaymentRecordIds[period])).PartialAmount);
        Assert.Equal(300m, (await verify.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).ElecAmountPaid);
    }

    private CollectionComposerWorkflow Workflow(AppDbContext context, Seed seed, IClock? clock = null) =>
        new(context, new TestActor(seed.UserId, seed.TenantId), new FixedTenant(seed.TenantId), clock);

    /// <summary>Seeds the tenant's Penalties/Fines classification (OR) and one approved penalty version.</summary>
    private async Task<Guid> SeedFinesAsync(
        Seed seed, string code = "LATE_PAYMENT", GovernedServiceBasis basis = GovernedServiceBasis.FixedAmount,
        decimal? fixedAmount = 50m, decimal? ceiling = null)
    {
        await using var context = db.CreateContext(seed.TenantId);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.PenaltiesAndFines, seed.TenantId);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1),
            "Penalties/Fines", RevenueInstrumentType.OfficialReceipt, seed.TenantId);
        var version = PenaltyDefinition.Create(seed.TenantId, code, new DateOnly(2020, 1, 1), "Late payment penalty",
            "Stall rent arrears", basis, basis == GovernedServiceBasis.FixedAmount ? fixedAmount : null, ceiling, true, "head");
        context.AddRange(classification, policy, version);
        await context.SaveChangesAsync();
        return version.Id;
    }

    private static async Task<EcfCollectionDraftDto> ReviewedEcfDraftAsync(
        CollectionComposerWorkflow workflow, Seed seed, decimal ecfAmount)
    {
        var ecf = await workflow.AddEcfLineAsync(new AddEcfDraftLineRequest(seed.BillId, ecfAmount));
        Assert.True(ecf.IsSuccess, ecf.Error);
        return ecf.Value!;
    }

    [SkippableFact]
    public async Task AnApprovedPenaltyRidesOnTheSameOrAsEcf_AsItsOwnClassifiedLineWithNoAllocation()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        await SeedFinesAsync(seed);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);

        var ecf = await ReviewedEcfDraftAsync(workflow, seed, 500m);
        var withPenalty = await workflow.AddPenaltyLineAsync(
            new AddPenaltyDraftLineRequest("LATE_PAYMENT", 50m, "Stall ECF-01 · late August rent", ecf.Revision));
        Assert.True(withPenalty.IsSuccess, withPenalty.Error);
        Assert.Equal(550m, withPenalty.Value!.TotalAmount);
        Assert.Equal(2, withPenalty.Value.Lines.Count);
        var penaltyLine = Assert.Single(withPenalty.Value.Lines, x => x.SourceKind == CollectionSourceKind.PenaltyDefinition);
        Assert.Equal("Late payment penalty", penaltyLine.ClassificationName);
        Assert.Empty(penaltyLine.Allocations ?? []);

        var selected = await workflow.SelectDocumentAsync(withPenalty.Value.DraftId,
            new SelectEcfDraftDocumentRequest(withPenalty.Value.Revision, seed.OrDocumentId));
        var reviewed = await workflow.ReviewAsync(selected.Value!.DraftId, new EcfDraftRevisionRequest(selected.Value.Revision));
        Assert.True(reviewed.IsSuccess, reviewed.Error);
        var posted = await workflow.PostAsync(reviewed.Value!.DraftId,
            new PostEcfCollectionDraftRequest(reviewed.Value.Revision, Guid.NewGuid()));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(550m, posted.Value!.Amount);

        var collection = await context.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations).SingleAsync();
        Assert.Equal(550m, collection.TotalAmount);
        Assert.Equal(seed.PayorId, collection.PayorId);
        var finesId = await context.RevenueClassifications.Where(x =>
            x.SemanticCode == RevenueClassificationCodes.PenaltiesAndFines).Select(x => x.Id).SingleAsync();
        var fine = Assert.Single(collection.Lines, x => x.RevenueClassificationId == finesId);
        Assert.Equal(50m, fine.Amount);
        Assert.Equal(CollectionSourceKind.PenaltyDefinition, fine.SourceKind);
        Assert.Empty(fine.Allocations);                          // an immediate approved charge, not a receivable
        Assert.Contains("\"code\":\"LATE_PAYMENT\"", fine.CalculationSnapshot);
        Assert.Contains("late August rent", fine.CalculationSnapshot);
        var ecfLine = Assert.Single(collection.Lines, x => x.RevenueClassificationId != finesId);
        Assert.Equal(500m, ecfLine.Amount);
        Assert.Equal(AccountableDocumentState.InOffice,
            (await context.AccountableDocuments.SingleAsync(x => x.Id == seed.OrDocumentId)).State);

        // One OR, two independently classified official lines; the fines register reads the canonical line.
        var register = await new PenaltyDefinitionWorkflow(context, new TestActor(seed.UserId, seed.TenantId), new FixedTenant(seed.TenantId))
            .GetRegisterAsync(seed.Period, seed.Period.AddMonths(1).AddDays(-1));
        var row = Assert.Single(register.Value!);
        Assert.Equal("LATE_PAYMENT", row.PenaltyCode);
        Assert.Equal(50m, row.Amount);
        Assert.Equal("Lisa ECF", row.PayerName);
        Assert.Equal("Stall ECF-01 · late August rent", row.Origin);
        Assert.StartsWith("SRC-", row.ReferenceCode);
        Assert.Equal("Posted", row.Status);
    }

    [SkippableFact]
    public async Task APenaltyNeedsAnAllocatedItemFirst_AndIsHeldToItsApprovedAmount_AndCannotBeAddedTwice()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        await SeedFinesAsync(seed);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);

        var alone = await workflow.AddPenaltyLineAsync(new AddPenaltyDraftLineRequest("LATE_PAYMENT", 50m, null, 0L));
        Assert.False(alone.IsSuccess);
        Assert.Empty(await context.WebCollectionDrafts.ToListAsync());

        var ecf = await ReviewedEcfDraftAsync(workflow, seed, 500m);
        var wrongAmount = await workflow.AddPenaltyLineAsync(new AddPenaltyDraftLineRequest("LATE_PAYMENT", 75m, null, ecf.Revision));
        var unknown = await workflow.AddPenaltyLineAsync(new AddPenaltyDraftLineRequest("MADE_UP", 50m, null, ecf.Revision));
        var stale = await workflow.AddPenaltyLineAsync(new AddPenaltyDraftLineRequest("LATE_PAYMENT", 50m, null, ecf.Revision + 5));
        Assert.False(wrongAmount.IsSuccess);
        Assert.False(unknown.IsSuccess);
        Assert.False(stale.IsSuccess);
        Assert.Single(await context.WebCollectionDraftLines.ToListAsync());

        var added = await workflow.AddPenaltyLineAsync(new AddPenaltyDraftLineRequest("late_payment", 50m, null, ecf.Revision));
        Assert.True(added.IsSuccess, added.Error);
        var again = await workflow.AddPenaltyLineAsync(new AddPenaltyDraftLineRequest("LATE_PAYMENT", 50m, null, added.Value!.Revision));
        Assert.False(again.IsSuccess);
        Assert.Equal(2, (await context.WebCollectionDraftLines.ToListAsync()).Count);
    }

    [SkippableFact]
    public async Task ADirectApprovedPenaltyIsHeldToItsCeiling()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        await SeedFinesAsync(seed, "ILLEGAL_VENDING", GovernedServiceBasis.DirectApprovedAmount, null, 200m);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);
        var ecf = await ReviewedEcfDraftAsync(workflow, seed, 500m);

        var above = await workflow.AddPenaltyLineAsync(new AddPenaltyDraftLineRequest("ILLEGAL_VENDING", 200.01m, null, ecf.Revision));
        var within = await workflow.AddPenaltyLineAsync(new AddPenaltyDraftLineRequest("ILLEGAL_VENDING", 150m, null, ecf.Revision));

        Assert.False(above.IsSuccess);
        Assert.True(within.IsSuccess, within.Error);
        Assert.Equal(650m, within.Value!.TotalAmount);
    }

    [SkippableFact]
    public async Task IfThePenaltysApprovedTermsChangeOrItIsRetired_ReviewAndPostRefuse_NothingIsPosted()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        await SeedFinesAsync(seed);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed);
        var ecf = await ReviewedEcfDraftAsync(workflow, seed, 500m);
        var withPenalty = (await workflow.AddPenaltyLineAsync(
            new AddPenaltyDraftLineRequest("LATE_PAYMENT", 50m, null, ecf.Revision))).Value!;
        var selected = (await workflow.SelectDocumentAsync(withPenalty.DraftId,
            new SelectEcfDraftDocumentRequest(withPenalty.Revision, seed.OrDocumentId))).Value!;
        var reviewed = (await workflow.ReviewAsync(selected.DraftId, new EcfDraftRevisionRequest(selected.Revision))).Value!;

        // The Head appends a newer version effective today: the frozen version is no longer the one in force.
        await using (var head = db.CreateContext(seed.TenantId))
        {
            var define = await new PenaltyDefinitionWorkflow(head, new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"),
                new FixedTenant(seed.TenantId)).DefineAsync(new DefinePenaltyRequest(
                "LATE_PAYMENT", "Late payment penalty", null, PhilippineTime.Today, GovernedServiceBasis.FixedAmount, 60m, null, true));
            Assert.True(define.IsSuccess, define.Error);
        }

        var posted = await workflow.PostAsync(reviewed.DraftId, new PostEcfCollectionDraftRequest(reviewed.Revision, Guid.NewGuid()));
        var reReview = await workflow.ReviewAsync(reviewed.DraftId, new EcfDraftRevisionRequest(reviewed.Revision));

        Assert.False(posted.IsSuccess);
        Assert.False(reReview.IsSuccess);
        Assert.Empty(await context.Collections.ToListAsync());
        Assert.Equal(AccountableDocumentState.InOffice,
            (await context.AccountableDocuments.SingleAsync(x => x.Id == seed.OrDocumentId)).State);
    }

    [SkippableFact]
    public async Task OnlyTheHeadDefinesPenalties_AndVersionsAreAppendedNeverEdited()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        await using var context = db.CreateContext(seed.TenantId);
        var admin = new PenaltyDefinitionWorkflow(context, new TestActor(seed.UserId, seed.TenantId, "Admin"), new FixedTenant(seed.TenantId));
        var head = new PenaltyDefinitionWorkflow(context, new TestActor(seed.UserId, seed.TenantId, "SuperAdmin"), new FixedTenant(seed.TenantId));
        var request = new DefinePenaltyRequest("LATE_PAYMENT", "Late payment", null, PhilippineTime.Today.AddDays(-30),
            GovernedServiceBasis.FixedAmount, 50m, null, true);

        Assert.Equal(ResultStatus.Forbidden, (await admin.DefineAsync(request)).Status);
        Assert.True((await head.DefineAsync(request)).IsSuccess);
        Assert.True((await head.DefineAsync(request with { EffectiveDate = PhilippineTime.Today.AddDays(-1), FixedAmount = 55m })).IsSuccess);
        Assert.Equal(ResultStatus.Invalid, (await head.DefineAsync(request with { Code = "bad code!" })).Status);
        Assert.Equal(ResultStatus.Invalid, (await head.DefineAsync(request with { Basis = GovernedServiceBasis.FixedAmount, FixedAmount = null })).Status);

        Assert.Equal(2, await context.PenaltyDefinitions.CountAsync());
        var current = Assert.Single((await admin.GetDefinitionsAsync()).Value!);
        Assert.Equal(55m, current.FixedAmount);   // the newest effective version; the earlier one is untouched history
        Assert.Equal(50m, (await context.PenaltyDefinitions.OrderBy(x => x.EffectiveDate).FirstAsync()).FixedAmount);
    }

    private async Task<RentSeed> SeedRentSourcesAsync(
        Seed seed,
        IReadOnlyList<DateOnly> periods,
        bool canonical,
        decimal partialAmount = 0m,
        decimal legacyElectricityAmount = 0m,
        decimal legacyFishKilos = 0m,
        Guid? payorId = null,
        Payor? additionalPayor = null)
    {
        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC",
            municipalityId: seed.TenantId);
        var stall = Stall.Create(facility.Id, "RENT-01", 900m, ApplicableFees.BaseRental,
            municipalityId: seed.TenantId);
        var contract = Contract.Create(stall.Id, "Lisa ECF", "Lisa ECF",
            periods.Min(), 5, 900m, createdBy: "test");
        var linkedPayorId = payorId ?? seed.PayorId;
        contract.AssociatePayor(linkedPayorId, "test");
        var classification = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent,
            seed.TenantId);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1),
            "Permanent Stall Rent", RevenueInstrumentType.OfficialReceipt, seed.TenantId);
        var records = new Dictionary<DateOnly, PaymentRecord>();
        var cutovers = new List<CollectionSettlementCutover>();
        foreach (var period in periods)
        {
            var record = PaymentRecord.Create(stall.Id, period.Year, period.Month, 900m, "test");
            if (!canonical && (partialAmount > 0m || legacyElectricityAmount > 0m || legacyFishKilos > 0m))
                record.RecordPayment("LEGACY-RENT", seed.UserId, PaymentStatus.Partial,
                    partialAmount: partialAmount, elecAmount: legacyElectricityAmount,
                    fishKilos: legacyFishKilos, updatedBy: "test");
            if (canonical)
            {
                if (partialAmount > 0m) record.UpdateStatus(PaymentStatus.Partial, partialAmount, updatedBy: "test");
                record.MarkSettlementPendingCutover();
                var at = DateTime.UtcNow.AddMinutes(-3);
                var cutover = CollectionSettlementCutover.Freeze(seed.TenantId,
                    CollectionSourceKind.PaymentRecord, record.Id, null, record.SettlementVersion,
                    at, record.BaseRentalAmount, partialAmount, record.BaseRentalAmount - partialAmount,
                    "{\"pendingMobileOperations\":0,\"issuedDocuments\":0,\"legacyWritersQuiesced\":true}",
                    seed.UserId, at.AddMinutes(1));
                record.ActivateCanonicalSettlement(cutover);
                cutovers.Add(cutover);
            }
            records.Add(period, record);
        }

        await using var context = db.CreateContext(seed.TenantId);
        if (additionalPayor is not null) context.Payors.Add(additionalPayor);
        context.AddRange(facility, stall, contract, classification, policy);
        context.PaymentRecords.AddRange(records.Values);
        context.CollectionSettlementCutovers.AddRange(cutovers);
        await context.SaveChangesAsync();
        return new RentSeed(stall.Id, contract.Id, linkedPayorId, classification.Id,
            records.ToDictionary(x => x.Key, x => x.Value.Id));
    }

    private async Task<Seed> SeedEcfAsync(bool canonical)
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var period = PhilippineTime.Today;
        var periodStart = new DateOnly(period.Year, period.Month, 1);
        var municipalityCode = $"ECF-{Guid.NewGuid():N}"[..24];
        var municipality = Municipality.Create(municipalityCode, "ECF Test", "Surigao del Sur",
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

        return new Seed(tenant, user, bill.Id, payor.Id, orDocument.Id, secondOrDocument.Id, ctDocument.Id, periodStart);
    }

    // ── Collector Mobile canonical writers (IA-051 / IA-062): no physical serial, SRC returned, exactly-once ──

    private sealed class CollectorActor(Guid collectorId, Guid tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => collectorId;
        public string? Username => "mobile-collector";
        public string? Role => "Collector";
        public Guid? CollectorId => collectorId;
        public string? MunicipalityCode => "ecf-test";
        public Guid? MunicipalityId => tenantId;
    }

    private async Task<Guid> SeedCollectorAsync(Guid tenantId, FacilityCode facilityCode)
    {
        await using var context = db.CreateContext(tenantId);
        var facility = await context.Facilities.SingleAsync(x => x.Code == facilityCode);
        var collector = CollectorUser.Create("Mobile Collector", "MC-01", "mc-" + Guid.NewGuid().ToString("N")[..8],
            null, null, new HashedPassword("test-hash"), tenantId);
        collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, facility.Id, facilityCode));
        context.CollectorUsers.Add(collector);
        await context.SaveChangesAsync();
        return collector.Id;
    }

    private CollectionComposerWorkflow AsCollector(AppDbContext context, Guid tenantId, Guid collectorId) =>
        new(context, new CollectorActor(collectorId, tenantId), new FixedTenant(tenantId));

    [SkippableFact]
    public async Task MobileEcf_PostsWithNoSerial_ReturnsSrc_ReplaysTheSame_AndIsCountedOnce()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var collectorId = await SeedCollectorAsync(seed.TenantId, FacilityCode.NPM);
        long version;
        await using (var read = db.CreateContext(seed.TenantId))
            version = (await read.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).ElectricitySourceVersion;
        var request = new MobileEcfPostRequest(Guid.NewGuid(), seed.BillId, 100m, version, PhilippineTime.Today);

        Guid collectionId;
        string src;
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var first = await AsCollector(context, seed.TenantId, collectorId).PostMobileEcfAsync(request);
            Assert.True(first.IsSuccess, first.Error);
            Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", first.Value!.ReferenceCode);
            (collectionId, src) = (first.Value.CollectionId, first.Value.ReferenceCode);
        }
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var replay = await AsCollector(context, seed.TenantId, collectorId).PostMobileEcfAsync(request);
            Assert.True(replay.IsSuccess, replay.Error);
            Assert.Equal((collectionId, src, true), (replay.Value!.CollectionId, replay.Value.ReferenceCode, replay.Value.ReturnedExistingOutcome));
        }

        await using var verify = db.CreateContext(seed.TenantId);
        var collection = await verify.Collections.Include(x => x.Lines).SingleAsync();
        Assert.Equal((100m, collectorId, src), (collection.TotalAmount, collection.CollectorId!.Value, collection.ReferenceCode));
        var line = Assert.Single(collection.Lines);
        Assert.Equal((CollectionSourceKind.UtilityBill, CollectionSourcePart.Electricity), (line.SourceKind!.Value, line.SourcePart!.Value));
        var bill = await verify.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(300m, bill.ElecAmountPaid);
        Assert.Equal("LEGACY-E-001", bill.ElecORNumber);
        Assert.All(await verify.AccountableDocuments.ToListAsync(), d => Assert.Equal(AccountableDocumentState.InOffice, d.State));

        var activity = await new EEMOCantilanSDS.Infrastructure.Repositories.CollectionActivityReader(verify)
            .GetAsync(seed.TenantId, PhilippineTime.Today, PhilippineTime.Today);
        Assert.Equal(src, Assert.Single(activity, e => e.Authority == "Canonical").ReferenceCode);
        var facts = (await new RemittanceWorkflow(verify, new CollectorActor(collectorId, seed.TenantId), new FixedTenant(seed.TenantId))
            .GetMyCollectionsAsync(PhilippineTime.Today.AddDays(-1), PhilippineTime.Today)).Value!;
        Assert.Equal((src, 100m), (Assert.Single(facts).ReferenceCode, facts[0].NetAmount));
    }

    [SkippableFact]
    public async Task MobileEcf_IsRefusedWithoutWriting_WhenTheSourceIsStillLegacy_StaleOrOverAmount_OrNotACollector()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var legacy = await SeedEcfAsync(canonical: false);
        var legacyCollector = await SeedCollectorAsync(legacy.TenantId, FacilityCode.NPM);
        await using (var context = db.CreateContext(legacy.TenantId))
        {
            var refused = await AsCollector(context, legacy.TenantId, legacyCollector).PostMobileEcfAsync(
                new MobileEcfPostRequest(Guid.NewGuid(), legacy.BillId, 100m, 1, PhilippineTime.Today));
            Assert.False(refused.IsSuccess);
            Assert.Contains("canonical", refused.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await context.Collections.ToListAsync());
            Assert.Equal(200m, (await context.UtilityBills.SingleAsync(x => x.Id == legacy.BillId)).ElecAmountPaid);
        }

        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var collectorId = await SeedCollectorAsync(seed.TenantId, FacilityCode.NPM);
        long version;
        await using (var read = db.CreateContext(seed.TenantId))
            version = (await read.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).ElectricitySourceVersion;
        await using (var context = db.CreateContext(seed.TenantId))
        {
            Assert.False((await AsCollector(context, seed.TenantId, collectorId).PostMobileEcfAsync(
                new MobileEcfPostRequest(Guid.NewGuid(), seed.BillId, 100m, version + 5, PhilippineTime.Today))).IsSuccess);
            Assert.False((await AsCollector(context, seed.TenantId, collectorId).PostMobileEcfAsync(
                new MobileEcfPostRequest(Guid.NewGuid(), seed.BillId, 600.01m, version, PhilippineTime.Today))).IsSuccess);
            Assert.Empty(await context.Collections.ToListAsync());
        }
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var admin = await new CollectionComposerWorkflow(context, new TestActor(seed.UserId, seed.TenantId), new FixedTenant(seed.TenantId))
                .PostMobileEcfAsync(new MobileEcfPostRequest(Guid.NewGuid(), seed.BillId, 100m, version, PhilippineTime.Today));
            Assert.Equal(ResultStatus.Forbidden, admin.Status);
        }
    }

    [SkippableFact]
    public async Task MobileRent_PostsOnACanonicalRow_WithNoSerial_ReturnsSrc_ReplaysTheSame_AndLegacyRowsAreRefused()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedEcfAsync(canonical: true);
        var period = seed.Period;
        var rent = await SeedRentSourcesAsync(seed, [period], canonical: true);
        var collectorId = await SeedCollectorAsync(seed.TenantId, FacilityCode.TCC);
        long version;
        await using (var read = db.CreateContext(seed.TenantId))
            version = (await read.PaymentRecords.SingleAsync(x => x.Id == rent.PaymentRecordIds[period])).SettlementVersion;
        var request = new MobileRentPostRequest(Guid.NewGuid(), rent.StallId, period.Year, period.Month, 400m, version, PhilippineTime.Today);

        string src;
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var posted = await AsCollector(context, seed.TenantId, collectorId).PostMobileRentAsync(request);
            Assert.True(posted.IsSuccess, posted.Error);
            src = posted.Value!.ReferenceCode;
            Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", src);
        }
        await using (var context = db.CreateContext(seed.TenantId))
        {
            var replay = await AsCollector(context, seed.TenantId, collectorId).PostMobileRentAsync(request);
            Assert.True(replay.IsSuccess, replay.Error);
            Assert.Equal((src, true), (replay.Value!.ReferenceCode, replay.Value.ReturnedExistingOutcome));
        }
        await using (var verify = db.CreateContext(seed.TenantId))
        {
            var collection = await verify.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations).SingleAsync();
            Assert.Equal((400m, collectorId), (collection.TotalAmount, collection.CollectorId!.Value));
            Assert.Equal(rent.PaymentRecordIds[period], Assert.Single(Assert.Single(collection.Lines).Allocations).SourceId);
            var record = await verify.PaymentRecords.SingleAsync(x => x.Id == rent.PaymentRecordIds[period]);
            Assert.Equal(PaymentStatus.Partial, record.Status);
            Assert.Equal(400m, record.PartialAmount);
            Assert.Empty(await verify.AccountableDocuments.Where(x => x.CollectionId != null).ToListAsync());
        }

        await db.ResetAsync();
        var legacySeed = await SeedEcfAsync(canonical: true);
        var legacyRent = await SeedRentSourcesAsync(legacySeed, [legacySeed.Period], canonical: false);
        var legacyCollector = await SeedCollectorAsync(legacySeed.TenantId, FacilityCode.TCC);
        await using var legacyContext = db.CreateContext(legacySeed.TenantId);
        var refused = await AsCollector(legacyContext, legacySeed.TenantId, legacyCollector).PostMobileRentAsync(
            new MobileRentPostRequest(Guid.NewGuid(), legacyRent.StallId, legacySeed.Period.Year, legacySeed.Period.Month, 100m, 0, PhilippineTime.Today));
        Assert.False(refused.IsSuccess);
        Assert.Empty(await legacyContext.Collections.ToListAsync());
    }
}
