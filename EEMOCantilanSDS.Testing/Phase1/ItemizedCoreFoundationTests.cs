using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

public sealed class ItemizedCoreFoundationTests
{
    private static readonly DateOnly BusinessDate = new(2026, 9, 26);
    private static readonly DateTime EventTime = DateTime.SpecifyKind(new DateTime(2026, 9, 26, 10, 0, 0), DateTimeKind.Utc);

    private static (RevenueClassification Classification, RevenueClassificationPolicy Policy) Classification(Guid tenant)
    {
        var semanticCode = $"TEST_{Guid.NewGuid():N}".ToUpperInvariant();
        var classification = RevenueClassification.Create(semanticCode, tenant);
        var policy = RevenueClassificationPolicy.Create(
            classification.Id, BusinessDate, "Test Fee", RevenueInstrumentType.OfficialReceipt, tenant);
        return (classification, policy);
    }

    private static Collection Post(Guid tenant, decimal amount, Guid? payorId = null, string? payerName = null,
        IReadOnlyList<CollectionAllocationDraft>? allocations = null)
    {
        var (classification, policy) = Classification(tenant);
        return Collection.Post(BusinessDate, EventTime, "user-1", "Office User", "Admin",
            [new CollectionLineDraft(classification, policy, amount, Allocations: allocations)],
            payerName: payerName, payorId: payorId);
    }

    [Fact]
    public void PayorNamesAreNotUniqueAndCollectionsRetainFrozenPayerEvidence()
    {
        var tenant = Guid.NewGuid();
        var first = Payor.Create(tenant, "Lisa Cruz", BusinessPayorKind.Person, "admin");
        var second = Payor.Create(tenant, "Lisa Cruz", BusinessPayorKind.Person, "admin");
        var collection = Post(tenant, 500m, first.Id, first.DisplayName);

        first.Rename("Lisa Cruz Santos", "admin");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.Id, collection.PayorId);
        Assert.Equal("Lisa Cruz", collection.PayerName);
    }

    [Fact]
    public void ALinkedPayorRequiresHistoricalNameEvidence()
    {
        Assert.Throws<ArgumentException>(() => Post(Guid.NewGuid(), 10m, Guid.NewGuid()));
        var oneOff = Post(Guid.NewGuid(), 10m, payerName: "Walk-in customer");
        Assert.Null(oneOff.PayorId);
        Assert.Equal("Walk-in customer", oneOff.PayerName);
    }

    [Fact]
    public void DraftReviewBindsExactRevisionAndPostedDraftCannotPostAgain()
    {
        var tenant = Guid.NewGuid();
        var draft = WebCollectionDraft.Create(tenant, Guid.NewGuid(), BusinessDate,
            null, "Walk-in", RevenueInstrumentType.OfficialReceipt, null, "admin");
        draft.Review(1, new string('A', 64), draft.OwnerUserId, EventTime);
        Assert.True(draft.IsReviewedForCurrentRevision);

        draft.AdvanceRevision(1, "admin");
        Assert.False(draft.IsReviewedForCurrentRevision);
        Assert.Throws<InvalidOperationException>(() => draft.AdvanceRevision(1, "admin"));

        draft.Review(2, new string('B', 64), draft.OwnerUserId, EventTime.AddMinutes(1));
        var collectionId = Guid.NewGuid();
        draft.MarkPosted(2, collectionId, "admin");
        Assert.Equal(CollectionDraftStatus.Posted, draft.Status);
        Assert.Equal(collectionId, draft.CollectionId);
        Assert.Throws<InvalidOperationException>(() => draft.MarkPosted(2, Guid.NewGuid(), "admin"));
    }

    [Fact]
    public void DraftDocumentAndSettlementMarkersUsePersistenceConcurrencyTokens()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=stalltrack_model_only;Username=none;Password=none")
            .Options;
        using var context = new AppDbContext(options);

        Assert.True(context.Model.FindEntityType(typeof(WebCollectionDraft))!
            .FindProperty(nameof(WebCollectionDraft.Revision))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(AccountableDocument))!
            .FindProperty(nameof(AccountableDocument.State))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(PaymentRecord))!
            .FindProperty(nameof(PaymentRecord.SettlementAuthorityState))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(UtilityBill))!
            .FindProperty(nameof(UtilityBill.ElectricitySettlementAuthorityState))!.IsConcurrencyToken);
        Assert.True(context.Model.FindEntityType(typeof(UtilityBill))!
            .FindProperty(nameof(UtilityBill.WaterSettlementAuthorityState))!.IsConcurrencyToken);
    }

    [Fact]
    public void AllocationIsAnExplicitSourceApplicationWithoutAnOutstandingBalanceEntity()
    {
        var sourceId = Guid.NewGuid();
        var allocation = new CollectionAllocationDraft(CollectionSourceKind.UtilityBill, sourceId, 300m,
            CollectionSourcePart.Electricity, "{\"period\":\"2026-09\"}");
        var collection = Post(Guid.NewGuid(), 300m, allocations: [allocation]);

        var posted = Assert.Single(Assert.Single(collection.Lines).Allocations);
        Assert.Equal(sourceId, posted.SourceId);
        Assert.Equal(300m, posted.Amount);
        Assert.Throws<ArgumentException>(() => Post(Guid.NewGuid(), 400m, allocations: [allocation]));
    }

    [Fact]
    public void CutoverSnapshotRequiresAReconciledOpeningEquation()
    {
        var cutover = CollectionSettlementCutover.Freeze(Guid.NewGuid(), CollectionSourceKind.UtilityBill,
            Guid.NewGuid(), CollectionSourcePart.Water, 7, EventTime, 800m, 300m, 500m,
            "{\"issuedCt\":1,\"pendingOperations\":0}", Guid.NewGuid(), EventTime.AddMinutes(5));
        Assert.Equal(500m, cutover.OpeningOutstandingAmount);

        Assert.Throws<ArgumentException>(() => CollectionSettlementCutover.Freeze(Guid.NewGuid(),
            CollectionSourceKind.UtilityBill, Guid.NewGuid(), CollectionSourcePart.Water, 7,
            EventTime, 800m, 200m, 500m, "{}", Guid.NewGuid(), EventTime.AddMinutes(5)));
    }

    [Fact]
    public void PendingOrCanonicalPaymentRecordRejectsLegacySettlementWriters()
    {
        var payment = PaymentRecord.Create(Guid.NewGuid(), 2026, 9, 900m);
        payment.MarkSettlementPendingCutover();
        Assert.Throws<InvalidOperationException>(() => payment.UpdateStatus(PaymentStatus.Paid));

        var cutover = CollectionSettlementCutover.Freeze(Guid.NewGuid(), CollectionSourceKind.PaymentRecord,
            payment.Id, null, 1, EventTime, 900m, 0m, 900m, "{\"pending\":0}", Guid.NewGuid(), EventTime.AddMinutes(1));
        payment.ActivateCanonicalSettlement(cutover);
        Assert.Throws<InvalidOperationException>(() => payment.MarkUnpaid());
        Assert.Throws<InvalidOperationException>(() => payment.SetClientOperationId(Guid.NewGuid()));
    }

    [Fact]
    public void UtilitySettlementAuthorityCanQuiesceOnePartWithoutBlockingTheOther()
    {
        var bill = UtilityBill.Create(Guid.NewGuid(), 2026, 9,
            0m, 100m, 1m, 0m, 2m, 1m);
        bill.MarkElectricityPendingCutover();

        bill.RecordPayment(null, null, null,
            PaymentStatus.Unpaid, null,
            PaymentStatus.Paid, null);

        Assert.Equal(PaymentStatus.Unpaid, bill.ElecStatus);
        Assert.Equal(PaymentStatus.Paid, bill.WaterStatus);
        var cutover = CollectionSettlementCutover.Freeze(Guid.NewGuid(), CollectionSourceKind.UtilityBill,
            bill.Id, CollectionSourcePart.Electricity, 1, EventTime, 100m, 0m, 100m,
            "{\"pending\":0}", Guid.NewGuid(), EventTime.AddMinutes(1));
        bill.ActivateCanonicalElectricitySettlement(cutover);
        Assert.Throws<InvalidOperationException>(() => bill.RecordPayment(null, null, null,
            PaymentStatus.Paid, null,
            PaymentStatus.Paid, null));
    }

    [Fact]
    public void PostingOperationFingerprintUsesNormalizedIntentAndBindsOneOutcome()
    {
        var tenant = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();
        var normalized = "{\"amount\":100,\"instrument\":\"CT\",\"source\":\"WCF-1\"}";
        var first = PostingOperation.Record(tenant, operationId, 1, normalized, "Mobile", "collector-1",
            PostingOperationStatus.Succeeded, null, null, collectionId, null, EventTime);
        var equivalent = PostingOperation.Record(tenant, Guid.NewGuid(), 1, normalized, "Mobile", "collector-1",
            PostingOperationStatus.Succeeded, null, null, Guid.NewGuid(), null, EventTime);
        var changed = PostingOperation.Record(tenant, Guid.NewGuid(), 1,
            "{\"amount\":150,\"instrument\":\"CT\",\"source\":\"WCF-1\"}", "Mobile", "collector-1",
            PostingOperationStatus.Succeeded, null, null, Guid.NewGuid(), null, EventTime);
        var changedActor = PostingOperation.Record(tenant, Guid.NewGuid(), 1, normalized, "Mobile", "collector-2",
            PostingOperationStatus.Succeeded, null, null, Guid.NewGuid(), null, EventTime);

        Assert.Equal(first.IntentFingerprint, equivalent.IntentFingerprint);
        Assert.NotEqual(first.IntentFingerprint, changed.IntentFingerprint);
        Assert.NotEqual(first.IntentFingerprint, changedActor.IntentFingerprint);
        Assert.Equal(collectionId, first.CollectionId);
    }

    [Fact]
    public void IssuedOfflineDocumentStaysConsumedWhenSynchronizationNeedsReconciliation()
    {
        var book = AccountableFormBook.Receive(Guid.NewGuid(), RevenueInstrumentType.CashTicket,
            "CT-2026", "CT-", 4120, 4200, 6, EventTime, "head-1", "head");
        var document = AccountableDocument.Register(book, 4120, "head");
        var operationId = Guid.NewGuid();
        document.AssignTo(Guid.NewGuid(), "head");
        document.Consume(null, operationId, EventTime.AddMinutes(1), "collector");
        document.MarkSyncIssue("sync");

        Assert.Equal("CT-004120", document.DocumentNumber);
        Assert.Equal(AccountableDocumentState.ReconciliationRequired, document.State);
        Assert.Equal(operationId, document.ClientOperationId);
        Assert.Throws<InvalidOperationException>(() => document.Consume(null, Guid.NewGuid(), EventTime, "retry"));
    }

    [Fact]
    public void DocumentReplacementHasNoFinancialEffectAndReversalIsExplicit()
    {
        var originalCollection = Guid.NewGuid();
        var oldDocument = Guid.NewGuid();
        var replacementDocument = Guid.NewGuid();
        var documentCorrection = CollectionCorrection.Record(Guid.NewGuid(), originalCollection,
            oldDocument, replacementDocument, null, CollectionCorrectionType.DocumentCorrection,
            BusinessDate.AddDays(1), EventTime.AddDays(1), 0m, "Physical OR misprint", "admin-1", "Admin", []);
        var reversal = CollectionCorrection.Record(Guid.NewGuid(), originalCollection,
            null, null, null, CollectionCorrectionType.Reversal,
            BusinessDate.AddDays(1), EventTime.AddDays(1), -300m, "Amount reversed", "admin-1", "Admin",
            [new CollectionCorrectionLineDraft(Guid.NewGuid(), -300m)]);

        Assert.Equal(0m, documentCorrection.FinancialEffectAmount);
        Assert.Equal(-300m, reversal.FinancialEffectAmount);
        Assert.Throws<ArgumentException>(() => CollectionCorrection.Record(Guid.NewGuid(), originalCollection,
            oldDocument, replacementDocument, null, CollectionCorrectionType.DocumentCorrection,
            BusinessDate.AddDays(1), EventTime.AddDays(1), 300m, "Incorrect amount", "admin-1", "Admin", []));
    }
}
