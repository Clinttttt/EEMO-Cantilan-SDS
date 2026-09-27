using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
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
public sealed class WcfCollectionWorkflowTests(PostgresFixture db)
{
    private sealed record Seed(Guid TenantId, Guid AdminId, Guid BillId, Guid PayorId,
        Guid CtDocumentId, string CtNumber, Guid OfficeCtDocumentId, string OfficeCtNumber,
        Guid OrDocumentId, string OrNumber, DateOnly Period, Guid? CollectorId, Guid? OtherCollectorId);

    private sealed class TestActor(Guid userId, Guid tenantId, string role = "Admin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "wcf-test-" + userId.ToString("N")[..8];
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "wcf-test";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    [SkippableFact]
    public async Task WaterQuoteAndCanonicalPostingUseOnlyWaterAndPreserveOneCtOutcome()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed, "Admin");

        var quotes = await workflow.GetObligationsAsync(seed.Period.Year, seed.Period.Month);
        Assert.True(quotes.IsSuccess, quotes.Error);
        var quote = Assert.Single(quotes.Value!);
        Assert.Equal(seed.BillId, quote.UtilityBillId);
        Assert.Equal(seed.PayorId, quote.PayorId);
        Assert.Equal(20m, quote.AssessedAmount);
        Assert.Equal(7m, quote.CumulativeSettledEvidence);
        Assert.Equal(13m, quote.OutstandingAmount);
        Assert.Equal(4m, quote.Consumption);
        Assert.Equal(5m, quote.RatePerCubicMeter);
        Assert.Equal(RevenueInstrumentType.CashTicket, quote.Instrument);
        Assert.Equal(SettlementAuthority.Canonical, quote.SettlementAuthority);

        var operationId = Guid.NewGuid();
        var businessDate = PhilippineTime.Today;
        var overOutstanding = await workflow.PostWebAsync(new WcfCollectionPostRequest(
            1, Guid.NewGuid(), businessDate, seed.BillId, quote.OutstandingAmount + 1m,
            quote.WaterSourceVersion, seed.CtDocumentId, seed.CtNumber, null));
        Assert.False(overOutstanding.IsSuccess);
        Assert.Contains("exceeds", overOutstanding.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.Collections.ToListAsync());
        Assert.Equal(AccountableDocumentState.InOffice,
            (await context.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId)).State);

        var stale = await workflow.PostWebAsync(new WcfCollectionPostRequest(
            1, Guid.NewGuid(), businessDate, seed.BillId, 1m,
            quote.WaterSourceVersion - 1, seed.CtDocumentId, seed.CtNumber, null));
        Assert.False(stale.IsSuccess);
        Assert.Contains("changed after it was quoted", stale.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.Collections.ToListAsync());

        var request = new WcfCollectionPostRequest(1, operationId, businessDate,
            seed.BillId, 5m, quote.WaterSourceVersion, seed.CtDocumentId, seed.CtNumber, null);
        var posted = await workflow.PostWebAsync(request);
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(5m, posted.Value!.Amount);
        Assert.Equal(seed.CtNumber, posted.Value.DocumentNumber);

        var collection = await context.Collections.Include(x => x.Lines)
            .ThenInclude(x => x.Allocations).SingleAsync();
        Assert.Equal(businessDate, collection.BusinessDate);
        var bill = await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(seed.Period.Year, bill.BillingYear);
        Assert.Equal(seed.Period.Month, bill.BillingMonth);
        Assert.Equal(5m, collection.TotalAmount);
        var line = Assert.Single(collection.Lines);
        Assert.Equal(CollectionSourceKind.UtilityBill, line.SourceKind);
        Assert.Equal(CollectionSourcePart.Water, line.SourcePart);
        Assert.Equal(5m, line.Amount);
        var allocation = Assert.Single(line.Allocations);
        Assert.Equal(CollectionSourceKind.UtilityBill, allocation.SourceKind);
        Assert.Equal(CollectionSourcePart.Water, allocation.SourcePart);
        Assert.Equal(seed.BillId, allocation.SourceId);
        Assert.Equal(5m, allocation.Amount);

        Assert.Equal(12m, bill.WaterAmountPaid);
        Assert.Equal(PaymentStatus.Partial, bill.WaterStatus);
        Assert.Equal(12m, bill.WaterPartialAmount);
        Assert.Equal(seed.CtNumber, bill.WaterORNumber); // legacy-named compatibility projection only
        Assert.Equal(10m, bill.ElecAmountPaid);
        Assert.Equal(PaymentStatus.Partial, bill.ElecStatus);
        Assert.Equal("LEGACY-E-001", bill.ElecORNumber);
        Assert.Equal(SettlementAuthority.Legacy, bill.ElectricitySettlementAuthorityState);

        var document = await context.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId);
        Assert.Equal(AccountableDocumentState.Consumed, document.State);
        Assert.Equal(operationId, document.ClientOperationId);
        Assert.Equal(collection.Id, document.CollectionId);
        var retry = await workflow.PostWebAsync(request);
        Assert.True(retry.IsSuccess, retry.Error);
        Assert.True(retry.Value!.ExistingOutcome);
        Assert.Equal(collection.Id, retry.Value.CollectionId);
        var changedIntent = await workflow.PostWebAsync(request with { ReceivedAmount = 6m });
        Assert.False(changedIntent.IsSuccess);
        Assert.Contains("IDEMPOTENCY CONFLICT", changedIntent.Error, StringComparison.OrdinalIgnoreCase);
        var differentOperationForSameTicket = await workflow.PostWebAsync(request with { ClientOperationId = Guid.NewGuid() });
        Assert.False(differentOperationForSameTicket.IsSuccess);
        Assert.Contains("not available", differentOperationForSameTicket.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await context.Collections.ToListAsync());

        var activity = await workflow.GetActivityAsync(businessDate, businessDate);
        Assert.True(activity.IsSuccess, activity.Error);
        var row = Assert.Single(activity.Value!);
        Assert.Equal(collection.Id, row.CollectionId);
        Assert.Equal("Posted", row.Disposition);
        Assert.Equal(seed.CtNumber, row.DocumentNumber);
        Assert.Equal(1, row.ItemCount);
    }

    [SkippableFact]
    public async Task UnchangedCanonicalWaterPartDoesNotBlockLegacyElectricityWriter()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed, "Admin");
        var operationId = Guid.NewGuid();
        var waterVersion = (await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).WaterSourceVersion;

        var resolution = await workflow.ReconcileLegacyMobileWaterAsync(new SyncOfflineOperationDto(
            operationId, OfflineOperationKind.NpmUtility, PhilippineTime.Today,
            UtilityBillId: seed.BillId,
            ElecStatus: PaymentStatus.Paid,
            ElecORNumber: "LEGACY-E-002",
            WaterStatus: PaymentStatus.Partial,
            WaterPartialAmount: 7m,
            WaterORNumber: "LEGACY-W-001"));
        Assert.True(resolution.IsSuccess, resolution.Error);
        Assert.False(resolution.Value!.RequiresReconciliation);

        var bill = await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
        bill.RecordPayment("LEGACY-E-002", "LEGACY-W-001", null,
            PaymentStatus.Paid, null, PaymentStatus.Partial, 7m, updatedBy: "legacy-test");
        bill.SetClientOperationId(operationId);
        await context.SaveChangesAsync();

        Assert.Equal(PaymentStatus.Paid, bill.ElecStatus);
        Assert.Equal("LEGACY-E-002", bill.ElecORNumber);
        Assert.Equal(waterVersion, bill.WaterSourceVersion);
        Assert.Equal(PaymentStatus.Partial, bill.WaterStatus);
        Assert.Equal(7m, bill.WaterPartialAmount);
        Assert.Equal("LEGACY-W-001", bill.WaterORNumber);
        Assert.Equal(SettlementAuthority.Canonical, bill.WaterSettlementAuthorityState);
        Assert.Empty(await context.Collections.ToListAsync());
        Assert.Empty(await context.PostingOperations.ToListAsync());
    }

    [SkippableFact]
    public async Task OldCumulativeWaterPayloadAfterCutoverIsPreservedAsReconciliationEvidence()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true, assignTicket: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed, "Collector");
        var operationId = Guid.NewGuid();
        var source = await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
        var originalWaterVersion = source.WaterSourceVersion;
        var originalElectricityVersion = source.ElectricitySourceVersion;

        var oldPayload = new SyncOfflineOperationDto(operationId, OfflineOperationKind.NpmUtility,
            PhilippineTime.Today, UtilityBillId: seed.BillId,
            ElecStatus: PaymentStatus.Paid, ElecORNumber: "UNSYNCED-ECF-OR",
            WaterStatus: PaymentStatus.Paid, WaterORNumber: seed.CtNumber);
        var resolution = await workflow.ReconcileLegacyMobileWaterAsync(oldPayload);
        Assert.True(resolution.IsSuccess, resolution.Error);
        Assert.True(resolution.Value!.RequiresReconciliation);
        Assert.Contains("preserved", resolution.Value.Message, StringComparison.OrdinalIgnoreCase);

        var operation = await context.PostingOperations.SingleAsync(x => x.ClientOperationId == operationId);
        Assert.Equal(PostingOperationStatus.ReconciliationRequired, operation.Status);
        Assert.Equal(seed.CtDocumentId, operation.AccountableDocumentId);
        var document = await context.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId);
        Assert.Equal(AccountableDocumentState.ReconciliationRequired, document.State);
        Assert.Equal(operationId, document.ClientOperationId);
        Assert.Empty(await context.Collections.ToListAsync());
        Assert.Equal(originalWaterVersion, source.WaterSourceVersion);
        Assert.Equal(originalElectricityVersion, source.ElectricitySourceVersion);
        Assert.Equal(PaymentStatus.Partial, source.WaterStatus);
        Assert.Equal(7m, source.WaterPartialAmount);
        Assert.Equal("LEGACY-W-001", source.WaterORNumber);

        var retry = await workflow.ReconcileLegacyMobileWaterAsync(oldPayload);
        Assert.True(retry.IsSuccess, retry.Error);
        Assert.True(retry.Value!.RequiresReconciliation);
        Assert.Single(await context.PostingOperations.ToListAsync());
        Assert.Empty(await context.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task OrCannotPostAsWcfAndLegacyMobileIssueIsRetainedForReconciliation()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: false, assignTicket: true);
        await using var adminContext = db.CreateContext(seed.TenantId);
        var adminWorkflow = Workflow(adminContext, seed, "Admin");
        var quoteResult = await adminWorkflow.GetObligationsAsync(seed.Period.Year, seed.Period.Month);
        Assert.True(quoteResult.IsSuccess, quoteResult.Error);
        var quote = Assert.Single(quoteResult.Value!);
        Assert.Equal(SettlementAuthority.Legacy, quote.SettlementAuthority);
        Assert.False(quote.CanCollectCanonical);

        var orAttempt = await adminWorkflow.PostWebAsync(new WcfCollectionPostRequest(
            1, Guid.NewGuid(), seed.Period, seed.BillId, 1m, quote.WaterSourceVersion,
            seed.OrDocumentId, seed.OrNumber, null));
        Assert.False(orAttempt.IsSuccess);
        Assert.Empty(await adminContext.Collections.ToListAsync());
        Assert.Equal(AccountableDocumentState.InOffice,
            (await adminContext.AccountableDocuments.SingleAsync(x => x.Id == seed.OrDocumentId)).State);

        await using var mobileContext = db.CreateContext(seed.TenantId);
        var mobileWorkflow = Workflow(mobileContext, seed, "Collector");
        var clientOperationId = Guid.NewGuid();
        var mobileAttempt = await mobileWorkflow.PostMobileAsync(new WcfCollectionPostRequest(
            1, clientOperationId, seed.Period, seed.BillId, 1m, quote.WaterSourceVersion,
            seed.CtDocumentId, seed.CtNumber, DateTime.UtcNow.AddMinutes(-1)));
        Assert.False(mobileAttempt.IsSuccess);
        Assert.Contains("RECONCILIATION_REQUIRED", mobileAttempt.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AccountableDocumentState.ReconciliationRequired,
            (await mobileContext.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId)).State);
        Assert.Equal(clientOperationId,
            (await mobileContext.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId)).ClientOperationId);
        Assert.Equal(PostingOperationStatus.ReconciliationRequired,
            (await mobileContext.PostingOperations.SingleAsync(x => x.ClientOperationId == clientOperationId)).Status);

        var differentOperation = await mobileWorkflow.PostMobileAsync(new WcfCollectionPostRequest(
            1, Guid.NewGuid(), seed.Period, seed.BillId, 1m, quote.WaterSourceVersion,
            seed.CtDocumentId, seed.CtNumber, DateTime.UtcNow));
        Assert.False(differentOperation.IsSuccess);
        Assert.Empty(await mobileContext.Collections.ToListAsync());
        Assert.Equal(PaymentStatus.Partial,
            (await mobileContext.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).WaterStatus);
    }

    [SkippableFact]
    public async Task ConcurrentIdenticalWcfAttemptsCreateOneCollectionAndConsumeOneTicket()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true);
        await using var quoteContext = db.CreateContext(seed.TenantId);
        var quoteResult = await Workflow(quoteContext, seed, "Admin")
            .GetObligationsAsync(seed.Period.Year, seed.Period.Month);
        Assert.True(quoteResult.IsSuccess, quoteResult.Error);
        var quote = Assert.Single(quoteResult.Value!);
        var request = new WcfCollectionPostRequest(1, Guid.NewGuid(), seed.Period,
            seed.BillId, 5m, quote.WaterSourceVersion, seed.CtDocumentId, seed.CtNumber, null);

        await using var contextA = db.CreateContext(seed.TenantId);
        await using var contextB = db.CreateContext(seed.TenantId);
        var attempts = await Task.WhenAll(
            Workflow(contextA, seed, "Admin").PostWebAsync(request),
            Workflow(contextB, seed, "Admin").PostWebAsync(request));

        Assert.All(attempts, result => Assert.True(result.IsSuccess, result.Error));
        Assert.Single(attempts.Select(x => x.Value!.CollectionId).Distinct());
        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Single(await verify.Collections.ToListAsync());
        Assert.Single(await verify.PostingOperations.Where(x => x.ClientOperationId == request.ClientOperationId).ToListAsync());
        Assert.Equal(AccountableDocumentState.Consumed,
            (await verify.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId)).State);
    }

    [SkippableFact]
    public async Task WebAndMobileRaceCannotDoubleSettleWater()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true, assignTicket: true);
        await using var quoteContext = db.CreateContext(seed.TenantId);
        var quoteResult = await Workflow(quoteContext, seed, "Admin")
            .GetObligationsAsync(seed.Period.Year, seed.Period.Month);
        Assert.True(quoteResult.IsSuccess, quoteResult.Error);
        var quote = Assert.Single(quoteResult.Value!);
        var businessDate = PhilippineTime.Today;
        var webRequest = new WcfCollectionPostRequest(1, Guid.NewGuid(), businessDate,
            seed.BillId, 5m, quote.WaterSourceVersion, seed.OfficeCtDocumentId, seed.OfficeCtNumber, null);
        var mobileRequest = new WcfCollectionPostRequest(1, Guid.NewGuid(), businessDate,
            seed.BillId, 5m, quote.WaterSourceVersion, seed.CtDocumentId, seed.CtNumber,
            DateTime.UtcNow.AddMinutes(-1));

        await using var webContext = db.CreateContext(seed.TenantId);
        await using var mobileContext = db.CreateContext(seed.TenantId);
        var attempts = await Task.WhenAll(
            Workflow(webContext, seed, "Admin").PostWebAsync(webRequest),
            Workflow(mobileContext, seed, "Collector").PostMobileAsync(mobileRequest));
        Assert.Single(attempts, x => x.IsSuccess);

        await using var verify = db.CreateContext(seed.TenantId);
        var collection = Assert.Single(await verify.Collections.ToListAsync());
        Assert.Equal(5m, collection.TotalAmount);
        var bill = await verify.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(12m, bill.WaterAmountPaid);
        var mobileTicket = await verify.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId);
        Assert.Contains(mobileTicket.State, new[] { AccountableDocumentState.Consumed, AccountableDocumentState.ReconciliationRequired });
        var officeTicket = await verify.AccountableDocuments.SingleAsync(x => x.Id == seed.OfficeCtDocumentId);
        Assert.Contains(officeTicket.State, new[] { AccountableDocumentState.InOffice, AccountableDocumentState.Consumed });
    }

    [SkippableFact]
    public async Task CollectorCannotPostAgainstAnotherCollectorsAssignedCashTicket()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true, assignTicket: true);
        Assert.NotNull(seed.OtherCollectorId);
        await using var context = db.CreateContext(seed.TenantId);
        var quoteResult = await Workflow(context, seed, "Admin")
            .GetObligationsAsync(seed.Period.Year, seed.Period.Month);
        Assert.True(quoteResult.IsSuccess, quoteResult.Error);
        var quote = Assert.Single(quoteResult.Value!);
        var actor = new TestActor(seed.OtherCollectorId!.Value, seed.TenantId, "Collector");
        var otherCollectorWorkflow = new WcfCollectionWorkflow(context, actor, new FixedTenant(seed.TenantId));

        var available = await otherCollectorWorkflow.GetAvailableCashTicketsAsync();
        Assert.True(available.IsSuccess, available.Error);
        Assert.DoesNotContain(available.Value!, x => x.DocumentId == seed.CtDocumentId);
        var posted = await otherCollectorWorkflow.PostMobileAsync(new WcfCollectionPostRequest(
            1, Guid.NewGuid(), PhilippineTime.Today, seed.BillId, 1m,
            quote.WaterSourceVersion, seed.CtDocumentId, seed.CtNumber, DateTime.UtcNow.AddMinutes(-1)));
        Assert.False(posted.IsSuccess);
        Assert.Empty(await context.Collections.ToListAsync());
        var ticket = await context.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId);
        Assert.Equal(AccountableDocumentState.Assigned, ticket.State);
        Assert.Equal(seed.CollectorId, ticket.AssignedUserId);
    }

    [SkippableFact]
    public async Task OlderStillOwedWaterObligationRemainsDiscoverable()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: false);
        await using var context = db.CreateContext(seed.TenantId);
        var current = await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
        var olderPeriod = seed.Period.AddMonths(-3);
        var older = UtilityBill.Create(current.StallId, olderPeriod.Year, olderPeriod.Month,
            0m, 0m, 10m, 0m, 4m, 5m, "test");
        context.UtilityBills.Add(older);
        await context.SaveChangesAsync();

        var quotes = await Workflow(context, seed, "Admin")
            .GetObligationsAsync(seed.Period.Year, seed.Period.Month);
        Assert.True(quotes.IsSuccess, quotes.Error);
        Assert.Contains(quotes.Value!, x => x.BillingYear == olderPeriod.Year
            && x.BillingMonth == olderPeriod.Month && x.OutstandingAmount > 0m);
    }

    [SkippableFact]
    public async Task ScopedCutoverDryRunFreezeAndActivationAreTenantBoundedAndNonRevenue()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: false);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = new SettlementCutoverWorkflow(context,
            new TestActor(seed.AdminId, seed.TenantId, "Admin"), new FixedTenant(seed.TenantId));
        var scope = new SettlementCutoverScope(CollectionSourceKind.UtilityBill, seed.BillId, CollectionSourcePart.Water);

        var before = await context.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == seed.BillId);
        var dryRun = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope));
        Assert.True(dryRun.IsSuccess, dryRun.Error);
        Assert.False(dryRun.Value!.Ready);
        Assert.Contains(dryRun.Value.BlockingReasons, x => x.Contains("Pending Cutover", StringComparison.Ordinal));
        var afterDryRun = await context.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(before.WaterSettlementAuthorityState, afterDryRun.WaterSettlementAuthorityState);
        Assert.Equal(before.WaterSourceVersion, afterDryRun.WaterSourceVersion);
        Assert.Empty(await context.CollectionSettlementCutovers.ToListAsync());
        Assert.Empty(await context.Collections.ToListAsync());
        Assert.False((await workflow.ActivateCanonicalAsync(scope, before.WaterSourceVersion)).IsSuccess);

        var pending = await workflow.BeginPendingCutoverAsync(scope);
        Assert.True(pending.IsSuccess, pending.Error);
        Assert.Equal(SettlementAuthority.PendingCutover, pending.Value!.Authority);
        var stillLegacyElectricity = await context.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(SettlementAuthority.Legacy, stillLegacyElectricity.ElectricitySettlementAuthorityState);

        var evidence = new SettlementCutoverReconciliationEvidence(
            LegacyWritersQuiesced: true,
            MobileQueuesDrained: true,
            NoUnregisteredFieldDevices: true,
            OnlinePaymentsDrained: true,
            AccountableDocumentInventoryReconciled: true,
            ReportingPathVerified: true,
            EvidenceReference: "isolated-integration-test:water-cutover",
            CollectorEvidence: []);
        var evaluated = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, evidence));
        Assert.True(evaluated.IsSuccess, evaluated.Error);
        Assert.True(evaluated.Value!.Ready, string.Join(" | ", evaluated.Value.BlockingReasons));
        Assert.Equal(20m, evaluated.Value.AssessmentAmount);
        Assert.Equal(7m, evaluated.Value.LegacySettledEvidence);
        Assert.Equal(13m, evaluated.Value.OutstandingAmount);
        Assert.Equal(RevenueInstrumentType.CashTicket, evaluated.Value.RequiredInstrument);

        var frozen = await workflow.FreezeOpeningPositionAsync(new SettlementCutoverFreezeRequest(
            scope, evaluated.Value.SourceVersion, evaluated.Value.ReadinessFingerprint, evidence));
        Assert.True(frozen.IsSuccess, frozen.Error);
        Assert.Equal(SettlementAuthority.PendingCutover, frozen.Value!.Authority);
        Assert.NotNull(frozen.Value.CutoverId);
        Assert.Equal(20m, frozen.Value.OpeningAssessmentAmount);
        Assert.Equal(7m, frozen.Value.OpeningLegacySettledAmount);
        Assert.Equal(13m, frozen.Value.OpeningOutstandingAmount);
        Assert.Empty(await context.Collections.ToListAsync());
        var cutover = await context.CollectionSettlementCutovers.SingleAsync();
        Assert.Contains("isolated-integration-test:water-cutover", cutover.ReconciliationEvidence, StringComparison.Ordinal);
        Assert.Equal(frozen.Value.SourceVersion, cutover.BoundaryVersion);

        var activated = await workflow.ActivateCanonicalAsync(scope, frozen.Value.SourceVersion);
        Assert.True(activated.IsSuccess, activated.Error);
        Assert.Equal(SettlementAuthority.Canonical, activated.Value!.Authority);
        var final = await context.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(SettlementAuthority.Canonical, final.WaterSettlementAuthorityState);
        Assert.Equal(SettlementAuthority.Legacy, final.ElectricitySettlementAuthorityState);
        Assert.Equal(7m, final.WaterAmountPaid);
        Assert.Equal(10m, final.ElecAmountPaid);
        Assert.Empty(await context.Collections.ToListAsync());
        var duplicate = await workflow.ActivateCanonicalAsync(scope, activated.Value.SourceVersion);
        Assert.False(duplicate.IsSuccess);
    }

    [SkippableFact]
    public async Task MonthlyRentCutoverFreezesOnlyBaseRentalSettlementEvidence()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: false);
        var facility = Facility.Create(FacilityCode.TCC, "Test Terminal", "TCC",
            municipalityId: seed.TenantId);
        var stall = Stall.Create(facility.Id, "RENT-01", 900m, ApplicableFees.BaseRental,
            createdBy: "test", municipalityId: seed.TenantId);
        var rent = PaymentRecord.Create(stall.Id, seed.Period.Year, seed.Period.Month, 900m, "test");
        rent.UpdateStatus(PaymentStatus.Partial, 200m, updatedBy: "test");
        var classification = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, seed.TenantId);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1),
            "Stall Rental", RevenueInstrumentType.OfficialReceipt, seed.TenantId);
        await using (var setup = db.CreateContext(seed.TenantId))
        {
            setup.AddRange(facility, stall, rent, classification, policy);
            await setup.SaveChangesAsync();
        }

        await using var context = db.CreateContext(seed.TenantId);
        var scope = new SettlementCutoverScope(CollectionSourceKind.PaymentRecord, rent.Id, null);
        var workflow = new SettlementCutoverWorkflow(context,
            new TestActor(seed.AdminId, seed.TenantId, "Admin"), new FixedTenant(seed.TenantId));
        Assert.True((await workflow.BeginPendingCutoverAsync(scope)).IsSuccess);
        var evidence = new SettlementCutoverReconciliationEvidence(true, true, true, true, true, true,
            "isolated-integration-test:monthly-rent", []);
        var readiness = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, evidence));
        Assert.True(readiness.IsSuccess, readiness.Error);
        Assert.True(readiness.Value!.Ready, string.Join(" | ", readiness.Value.BlockingReasons));
        Assert.Equal(900m, readiness.Value.AssessmentAmount);
        Assert.Equal(200m, readiness.Value.LegacySettledEvidence);
        Assert.Equal(700m, readiness.Value.OutstandingAmount);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, readiness.Value.RequiredInstrument);

        var frozen = await workflow.FreezeOpeningPositionAsync(new SettlementCutoverFreezeRequest(
            scope, readiness.Value.SourceVersion, readiness.Value.ReadinessFingerprint, evidence));
        Assert.True(frozen.IsSuccess, frozen.Error);
        Assert.Empty(await context.Collections.ToListAsync());
        var persisted = await context.PaymentRecords.AsNoTracking().SingleAsync(x => x.Id == rent.Id);
        Assert.Equal(SettlementAuthority.PendingCutover, persisted.SettlementAuthorityState);
        Assert.Equal(200m, persisted.PartialAmount);
        Assert.Equal(700m, persisted.BaseRentalAmount - persisted.PartialAmount);
    }

    [SkippableFact]
    public async Task ReadinessChangeAfterFreezeBlocksActivationWithoutCreatingRevenue()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: false, assignTicket: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = new SettlementCutoverWorkflow(context,
            new TestActor(seed.AdminId, seed.TenantId, "Admin"), new FixedTenant(seed.TenantId));
        var scope = new SettlementCutoverScope(CollectionSourceKind.UtilityBill, seed.BillId, CollectionSourcePart.Water);
        var evidence = new SettlementCutoverReconciliationEvidence(
            LegacyWritersQuiesced: true,
            MobileQueuesDrained: true,
            NoUnregisteredFieldDevices: true,
            OnlinePaymentsDrained: true,
            AccountableDocumentInventoryReconciled: true,
            ReportingPathVerified: true,
            EvidenceReference: "isolated-integration-test:readiness-change-after-freeze",
            CollectorEvidence:
            [
                new CutoverCollectorEvidence(seed.CollectorId!.Value, "1.1.11", 1, true,
                    DateTime.UtcNow, "device-a"),
                new CutoverCollectorEvidence(seed.OtherCollectorId!.Value, "1.1.11", 1, true,
                    DateTime.UtcNow, "device-b")
            ]);

        Assert.True((await workflow.BeginPendingCutoverAsync(scope)).IsSuccess);
        var readiness = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, evidence));
        Assert.True(readiness.IsSuccess, readiness.Error);
        Assert.True(readiness.Value!.Ready, string.Join(" | ", readiness.Value.BlockingReasons));
        Assert.Equal(1, readiness.Value.ActiveAssignedDocuments);

        var frozen = await workflow.FreezeOpeningPositionAsync(new SettlementCutoverFreezeRequest(
            scope, readiness.Value.SourceVersion, readiness.Value.ReadinessFingerprint, evidence));
        Assert.True(frozen.IsSuccess, frozen.Error);

        var document = await context.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId);
        var assignment = await context.AccountableFormAssignments.SingleAsync(x => x.AccountableDocumentId == seed.CtDocumentId);
        var beforeCustodyChange = await context.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == seed.BillId);
        assignment.RecordReturn("test-office-admin", DateTime.UtcNow);
        document.ReturnToOffice("test-office-admin");
        await context.SaveChangesAsync();

        var reevaluated = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, evidence));
        Assert.True(reevaluated.IsSuccess, reevaluated.Error);
        Assert.Equal(0, reevaluated.Value!.ActiveAssignedDocuments);
        Assert.NotEqual(readiness.Value.ReadinessFingerprint, reevaluated.Value.ReadinessFingerprint);
        var afterCustodyChange = await context.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(beforeCustodyChange.WaterSourceVersion, afterCustodyChange.WaterSourceVersion);
        Assert.Equal(beforeCustodyChange.WaterCharge, afterCustodyChange.WaterCharge);
        Assert.Equal(beforeCustodyChange.WaterAmountPaid, afterCustodyChange.WaterAmountPaid);

        var activation = await workflow.ActivateCanonicalAsync(scope, frozen.Value!.SourceVersion);

        Assert.False(activation.IsSuccess);
        context.ChangeTracker.Clear();
        var final = await context.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(SettlementAuthority.PendingCutover, final.WaterSettlementAuthorityState);
        Assert.Equal(SettlementAuthority.Legacy, final.ElectricitySettlementAuthorityState);
        Assert.Equal(beforeCustodyChange.WaterSourceVersion, final.WaterSourceVersion);
        Assert.Equal(beforeCustodyChange.WaterCharge, final.WaterCharge);
        Assert.Equal(beforeCustodyChange.WaterAmountPaid, final.WaterAmountPaid);
        Assert.Empty(await context.Collections.AsNoTracking().ToListAsync());
        Assert.Empty(await context.CollectionLines.AsNoTracking().ToListAsync());
        Assert.Empty(await context.CollectionAllocations.AsNoTracking().ToListAsync());
    }

    [SkippableFact]
    public async Task CutoverSourceLookupCannotCrossTenantBoundary()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: false);
        var otherTenantId = Guid.NewGuid();
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = new SettlementCutoverWorkflow(context,
            new TestActor(seed.AdminId, otherTenantId, "Admin"), new FixedTenant(otherTenantId));
        var scope = new SettlementCutoverScope(CollectionSourceKind.UtilityBill, seed.BillId, CollectionSourcePart.Water);

        var readiness = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope));
        var pending = await workflow.BeginPendingCutoverAsync(scope);

        Assert.False(readiness.IsSuccess);
        Assert.False(pending.IsSuccess);
        var source = await context.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(SettlementAuthority.Legacy, source.WaterSettlementAuthorityState);
        Assert.Empty(await context.CollectionSettlementCutovers.ToListAsync());
    }

    [SkippableFact]
    public async Task StaleSourceVersionPreventsCutoverActivationAndLeavesPartPending()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: false);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = new SettlementCutoverWorkflow(context,
            new TestActor(seed.AdminId, seed.TenantId, "Admin"), new FixedTenant(seed.TenantId));
        var scope = new SettlementCutoverScope(CollectionSourceKind.UtilityBill, seed.BillId, CollectionSourcePart.Water);
        Assert.True((await workflow.BeginPendingCutoverAsync(scope)).IsSuccess);
        var evidence = new SettlementCutoverReconciliationEvidence(true, true, true, true, true, true,
            "isolated-integration-test:stale-version", []);
        var report = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, evidence));
        Assert.True(report.IsSuccess, report.Error);
        var frozen = await workflow.FreezeOpeningPositionAsync(new SettlementCutoverFreezeRequest(
            scope, report.Value!.SourceVersion, report.Value.ReadinessFingerprint, evidence));
        Assert.True(frozen.IsSuccess, frozen.Error);

        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UtilityBills\" SET \"WaterSourceVersion\" = \"WaterSourceVersion\" + 1 WHERE \"MunicipalityId\" = {seed.TenantId} AND \"Id\" = {seed.BillId}");
        var activation = await workflow.ActivateCanonicalAsync(scope, frozen.Value!.SourceVersion);
        Assert.False(activation.IsSuccess);
        context.ChangeTracker.Clear();
        var bill = await context.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(SettlementAuthority.PendingCutover, bill.WaterSettlementAuthorityState);
        Assert.Equal(SettlementAuthority.Legacy, bill.ElectricitySettlementAuthorityState);
        Assert.Empty(await context.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task ReturnedHistoricalCustodyDoesNotAuthorizeCollectorToQuarantineTicket()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true, assignTicket: true);
        await using var context = db.CreateContext(seed.TenantId);
        var document = await context.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId);
        var assignment = await context.AccountableFormAssignments.SingleAsync(x => x.AccountableDocumentId == seed.CtDocumentId);
        var returnedAt = DateTime.UtcNow;
        assignment.RecordReturn("office-admin", returnedAt);
        document.ReturnToOffice("office-admin");
        await context.SaveChangesAsync();

        var workflow = Workflow(context, seed, "Collector");
        var operationId = Guid.NewGuid();
        var result = await workflow.PostMobileAsync(new WcfCollectionPostRequest(
            1, operationId, seed.Period, seed.BillId, 1m, 99,
            seed.CtDocumentId, seed.CtNumber, DateTime.UtcNow.AddMinutes(-1)));
        Assert.False(result.IsSuccess);
        var current = await context.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId);
        Assert.Equal(AccountableDocumentState.InOffice, current.State);
        Assert.Null(current.ClientOperationId);
        Assert.Equal(returnedAt, (await context.AccountableFormAssignments.SingleAsync(x => x.Id == assignment.Id)).ReturnedAtUtc);
    }

    [SkippableFact]
    public async Task WaterCutoverRequiresPerCollectorWcfCapabilityVersionOne()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: false, assignTicket: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = new SettlementCutoverWorkflow(context,
            new TestActor(seed.AdminId, seed.TenantId, "Admin"), new FixedTenant(seed.TenantId));
        var scope = new SettlementCutoverScope(CollectionSourceKind.UtilityBill, seed.BillId, CollectionSourcePart.Water);
        Assert.True((await workflow.BeginPendingCutoverAsync(scope)).IsSuccess);

        var evidence = new SettlementCutoverReconciliationEvidence(true, true, true, true, true, true,
            "isolated-integration-test:collector-capability", new[]
            {
                new CutoverCollectorEvidence(seed.CollectorId!.Value, "1.1.11", 0, true, DateTime.UtcNow, "device-a"),
                new CutoverCollectorEvidence(seed.OtherCollectorId!.Value, "1.1.11", 0, true, DateTime.UtcNow, "device-b")
            });
        var readiness = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, evidence));
        Assert.True(readiness.IsSuccess, readiness.Error);
        Assert.False(readiness.Value!.Ready);
        Assert.Equal(1, readiness.Value.RequiredMobilePayloadVersion);
        Assert.Equal(2, readiness.Value.CollectorsMissingCapabilityEvidence.Count);
        Assert.Contains(readiness.Value.BlockingReasons, x => x.Contains("WCF payload v1", StringComparison.Ordinal));
        Assert.Empty(await context.CollectionSettlementCutovers.ToListAsync());
        Assert.Empty(await context.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task UnresolvedOnlineWaterPaymentBlocksOpeningFreeze()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: false, assignTicket: true);
        await using var context = db.CreateContext(seed.TenantId);
        context.OnlinePaymentTransactions.Add(OnlinePaymentTransaction.CreateForNpmUtility(
            "CUTOVER-" + Guid.NewGuid().ToString("N"), seed.CollectorId!.Value,
            (await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).StallId,
            seed.Period.Year, seed.Period.Month, 7m, "test-provider"));
        await context.SaveChangesAsync();

        var workflow = new SettlementCutoverWorkflow(context,
            new TestActor(seed.AdminId, seed.TenantId, "Admin"), new FixedTenant(seed.TenantId));
        var scope = new SettlementCutoverScope(CollectionSourceKind.UtilityBill, seed.BillId, CollectionSourcePart.Water);
        Assert.True((await workflow.BeginPendingCutoverAsync(scope)).IsSuccess);
        var evidence = new SettlementCutoverReconciliationEvidence(true, true, true, true, true, true,
            "isolated-integration-test:online-drain", new[]
            {
                new CutoverCollectorEvidence(seed.CollectorId.Value, "1.1.11", 1, true, DateTime.UtcNow, "device-a"),
                new CutoverCollectorEvidence(seed.OtherCollectorId!.Value, "1.1.11", 1, true, DateTime.UtcNow, "device-b")
            });
        var readiness = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, evidence));
        Assert.True(readiness.IsSuccess, readiness.Error);
        Assert.False(readiness.Value!.Ready);
        Assert.Equal(1, readiness.Value.UnresolvedOnlinePayments);
        Assert.Contains(readiness.Value.BlockingReasons, x => x.Contains("online payment", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await context.CollectionSettlementCutovers.ToListAsync());
        Assert.Empty(await context.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task PhysicallyIssuedCashTicketExceptionBlocksWaterCutoverFreeze()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: false, assignTicket: true);
        await using var context = db.CreateContext(seed.TenantId);
        var scope = new SettlementCutoverScope(CollectionSourceKind.UtilityBill, seed.BillId, CollectionSourcePart.Water);
        var workflow = new SettlementCutoverWorkflow(context,
            new TestActor(seed.AdminId, seed.TenantId, "Admin"), new FixedTenant(seed.TenantId));
        Assert.True((await workflow.BeginPendingCutoverAsync(scope)).IsSuccess);

        var operationId = Guid.NewGuid();
        var document = await context.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId);
        document.MarkPhysicalIssueReconciliationRequired(operationId, DateTime.UtcNow.AddMinutes(-1), "collector");
        context.PostingOperations.Add(PostingOperation.Record(seed.TenantId, operationId, 1,
            $"{{\"sourceId\":\"{seed.BillId:D}\",\"sourcePart\":\"Water\"}}", "Mobile/Wcf", "collector",
            PostingOperationStatus.ReconciliationRequired, "SYNC_ISSUE", "Physical ticket awaits reconciliation.",
            null, seed.CtDocumentId, DateTime.UtcNow));
        await context.SaveChangesAsync();

        var evidence = new SettlementCutoverReconciliationEvidence(true, true, true, true, true, true,
            "isolated-integration-test:issued-ct", new[]
            {
                new CutoverCollectorEvidence(seed.CollectorId!.Value, "1.1.11", 1, true, DateTime.UtcNow, "device-a"),
                new CutoverCollectorEvidence(seed.OtherCollectorId!.Value, "1.1.11", 1, true, DateTime.UtcNow, "device-b")
            });
        var readiness = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, evidence));
        Assert.True(readiness.IsSuccess, readiness.Error);
        Assert.False(readiness.Value!.Ready);
        Assert.Equal(1, readiness.Value.ReconciliationDocuments);
        var exception = Assert.Single(readiness.Value.ReconciliationExceptions);
        Assert.Equal(operationId, exception.ClientOperationId);
        Assert.Equal(seed.CtDocumentId, exception.AccountableDocumentId);
        Assert.Equal(seed.CtNumber, exception.DocumentNumber);
        Assert.Contains(readiness.Value.BlockingReasons, x => x.Contains("reconciliation", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(AccountableDocumentState.ReconciliationRequired, document.State);
        Assert.Empty(await context.Collections.ToListAsync());
    }

    private WcfCollectionWorkflow Workflow(AppDbContext context, Seed seed, string role) =>
        new(context, new TestActor(role == "Collector" ? seed.CollectorId!.Value : seed.AdminId,
            seed.TenantId, role), new FixedTenant(seed.TenantId));

    private async Task<Seed> SeedAsync(bool canonicalWater, bool assignTicket = false)
    {
        var today = PhilippineTime.Today;
        var period = new DateOnly(today.Year, today.Month, 1);
        var municipalityCode = $"WCF-{Guid.NewGuid():N}"[..24];
        var municipality = Municipality.Create(municipalityCode, "WCF Test", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"wcf-{Guid.NewGuid():N}"[..32]);
        var tenantId = municipality.Id;
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var adminId = Guid.NewGuid();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM",
            archetype: BillingArchetype.DailyStall, municipalityId: tenantId);
        var stall = Stall.Create(facility.Id, "WCF-01", 0m,
            ApplicableFees.Electricity | ApplicableFees.Water, MarketSection.FishSection,
            createdBy: "test", municipalityId: tenantId);
        var payor = Payor.Create(tenantId, "Maria WCF", BusinessPayorKind.Person, "test");
        var contract = Contract.Create(stall.Id, "Maria WCF", "Maria WCF",
            period.AddYears(-5), 20, 0m, createdBy: "test");
        contract.AssociatePayor(payor.Id, "test");
        var bill = UtilityBill.Create(stall.Id, period.Year, period.Month,
            0m, 8m, 10m, 0m, 4m, 5m, "test");
        bill.RecordPayment("LEGACY-E-001", "LEGACY-W-001", null,
            PaymentStatus.Partial, 10m, PaymentStatus.Partial, 7m, updatedBy: "test");

        CollectionSettlementCutover? cutover = null;
        if (canonicalWater)
        {
            bill.MarkWaterPendingCutover();
            var cutoverAt = DateTime.UtcNow.AddMinutes(-2);
            cutover = CollectionSettlementCutover.Freeze(tenantId, CollectionSourceKind.UtilityBill,
                bill.Id, CollectionSourcePart.Water, bill.WaterSourceVersion, cutoverAt,
                bill.WaterCharge, bill.WaterAmountPaid, bill.WaterCharge - bill.WaterAmountPaid,
                "{\"pendingMobileOperations\":0,\"issuedDocuments\":0,\"legacyWritersQuiesced\":true}",
                adminId, cutoverAt.AddMinutes(1));
            bill.ActivateCanonicalWaterSettlement(cutover);
        }

        var classification = RevenueClassification.Create(RevenueClassificationCodes.Wcf, tenantId);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2000, 1, 1),
            "Water Consumption Fee", RevenueInstrumentType.CashTicket, tenantId);
        var receivedAt = DateTime.UtcNow.AddMinutes(-5);
        var ctBook = AccountableFormBook.Receive(tenantId, RevenueInstrumentType.CashTicket,
            "WCF TEST CT", "CT-", 1, 2, 4, receivedAt, adminId.ToString("N"), "test");
        var ct = AccountableDocument.Register(ctBook, 1, "test");
        var officeCt = AccountableDocument.Register(ctBook, 2, "test");
        var orBook = AccountableFormBook.Receive(tenantId, RevenueInstrumentType.OfficialReceipt,
            "WCF TEST OR", "OR-", 1, 1, 4, receivedAt, adminId.ToString("N"), "test");
        var or = AccountableDocument.Register(orBook, 1, "test");
        CollectorUser? collector = null;
        CollectorUser? otherCollector = null;
        AccountableFormAssignment? assignment = null;
        if (assignTicket)
        {
            collector = CollectorUser.Create("Test Collector", "WCF-01", "wcf-" + Guid.NewGuid().ToString("N")[..8],
                null, null, new HashedPassword("test-hash"), tenantId);
            collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, facility.Id, FacilityCode.NPM));
            ct.AssignTo(collector.Id, "test-admin");
            assignment = AccountableFormAssignment.Assign(ct, collector.Id, adminId.ToString("N"),
                DateTime.UtcNow, "test");
            otherCollector = CollectorUser.Create("Other Test Collector", "WCF-02", "wcf-" + Guid.NewGuid().ToString("N")[..8],
                null, null, new HashedPassword("test-hash"), tenantId);
            otherCollector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(
                otherCollector.Id, facility.Id, FacilityCode.NPM));
        }

        await using (var setup = db.CreateContext(tenantId))
        {
            setup.AddRange(facility, stall, payor, contract, bill, classification, policy,
                ctBook, ct, officeCt, orBook, or);
            if (collector is not null) setup.CollectorUsers.Add(collector);
            if (otherCollector is not null) setup.CollectorUsers.Add(otherCollector);
            if (assignment is not null) setup.AccountableFormAssignments.Add(assignment);
            if (cutover is not null) setup.CollectionSettlementCutovers.Add(cutover);
            await setup.SaveChangesAsync();
        }

        return new Seed(tenantId, adminId, bill.Id, payor.Id, ct.Id, ct.DocumentNumber,
            officeCt.Id, officeCt.DocumentNumber, or.Id, or.DocumentNumber, period,
            collector?.Id, otherCollector?.Id);
    }
}
