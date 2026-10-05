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
using EEMOCantilanSDS.Infrastructure.Repositories;
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
        var seed = await SeedAsync(canonicalWater: true, assignTicket: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed, "Collector");

        var adminQuotes = await Workflow(context, seed, "Admin").GetObligationsAsync(seed.Period.Year, seed.Period.Month);
        Assert.True(adminQuotes.IsSuccess, adminQuotes.Error); // Head/Admin monitoring reads remain available.
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
        var request = new WcfCollectionPostRequest(1, operationId, businessDate,
            seed.BillId, 5m, quote.WaterSourceVersion, seed.CtDocumentId, seed.CtNumber,
            DateTime.UtcNow.AddMinutes(-1));
        var posted = await workflow.PostMobileAsync(request);
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(5m, posted.Value!.Amount);
        Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", posted.Value.ReferenceCode);

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
        Assert.Equal("LEGACY-W-001", bill.WaterORNumber); // the legacy-named field is left untouched; the SRC is on the Collection
        Assert.Equal(10m, bill.ElecAmountPaid);
        Assert.Equal(PaymentStatus.Partial, bill.ElecStatus);
        Assert.Equal("LEGACY-E-001", bill.ElecORNumber);
        Assert.Equal(SettlementAuthority.Legacy, bill.ElectricitySettlementAuthorityState);

        var document = await context.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId);
        Assert.Equal(AccountableDocumentState.Assigned, document.State);   // no physical Cash Ticket is consumed (IA-062)
        Assert.Null(document.CollectionId);
        Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", collection.ReferenceCode);
        var retry = await workflow.PostMobileAsync(request);
        Assert.True(retry.IsSuccess, retry.Error);
        Assert.True(retry.Value!.ExistingOutcome);
        Assert.Equal(collection.Id, retry.Value.CollectionId);
        var changedIntent = await workflow.PostMobileAsync(request with { ReceivedAmount = 6m });
        Assert.False(changedIntent.IsSuccess);
        Assert.Contains("IDEMPOTENCY CONFLICT", changedIntent.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await context.Collections.ToListAsync());

        var activity = await Workflow(context, seed, "Admin").GetActivityAsync(businessDate, businessDate);
        Assert.True(activity.IsSuccess, activity.Error);
        var row = Assert.Single(activity.Value!);
        Assert.Equal(collection.Id, row.CollectionId);
        Assert.Equal("Posted", row.Disposition);
        Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", row.ReferenceCode);
        Assert.Equal(1, row.ItemCount);
    }

    [SkippableFact]
    public async Task CanonicalWcfReportsOnceByBusinessDateWithoutCountingWaterProjectionAsCash()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true, assignTicket: true);
        Assert.NotNull(seed.CollectorId);
        await using var context = db.CreateContext(seed.TenantId);

        var bill = await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
        // The source row keeps its compatibility projection, but the collector report must count its CT from Collection.
        // Retain collector attribution on the source row to prove that it cannot duplicate the canonical receipt.
        bill.RecordPayment(bill.ElecORNumber, bill.WaterORNumber, seed.CollectorId,
            bill.ElecStatus, bill.ElecPartialAmount, bill.WaterStatus, bill.WaterPartialAmount,
            updatedBy: "report-test");

        // A separate Legacy Water source remains on the existing projection path in the same report.
        var legacyBill = UtilityBill.Create(
            (await context.UtilityBills.Where(x => x.Id == seed.BillId).Select(x => x.StallId).SingleAsync()),
            seed.Period.AddMonths(-1).Year, seed.Period.AddMonths(-1).Month,
            0m, 0m, 10m, 0m, 4m, 5m, "report-test");
        legacyBill.RecordPayment(null, "LEGACY-AUG-W-001", seed.CollectorId,
            PaymentStatus.Unpaid, null, PaymentStatus.Partial, 3m, updatedBy: "report-test");
        context.UtilityBills.Add(legacyBill);
        await context.SaveChangesAsync();

        var businessDate = seed.Period.AddDays(-1);
        var workflow = Workflow(context, seed, "Collector");
        var quote = Assert.Single((await workflow.GetObligationsAsync(seed.Period.Year, seed.Period.Month)).Value!,
            x => x.UtilityBillId == seed.BillId);
        var posted = await workflow.PostMobileAsync(new WcfCollectionPostRequest(
            1, Guid.NewGuid(), businessDate, seed.BillId, 5m, quote.WaterSourceVersion,
            seed.CtDocumentId, seed.CtNumber, DateTime.UtcNow.AddMinutes(-1)));
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(businessDate, posted.Value!.BusinessDate);

        var reportQuery = new CollectorReportQueries(context);
        var report = await reportQuery.GetCollectionsAsync(seed.CollectorId.Value, businessDate, PhilippineTime.Today);
        var receipt = Assert.Single(report.Lines);
        Assert.Equal(posted.Value.ReferenceCode, receipt.DocumentNumber);   // the collector report carries the SRC for a canonical line
        Assert.Equal(5m, receipt.Amount);
        Assert.Equal(businessDate, receipt.BusinessDate);
        Assert.Equal(seed.Period, receipt.BilledMonth);
        Assert.Contains("WCF", receipt.Nature, StringComparison.Ordinal);
        Assert.Equal(13m, report.UtilityCollected); // ECF legacy 10 + separate Legacy Water 3; canonical Water 12 excluded.
        Assert.Equal(120m, report.UtilityBilled);
        Assert.Equal(95m, report.UtilityOutstanding);

        var laterOnly = await reportQuery.GetCollectionsAsync(seed.CollectorId.Value,
            seed.Period, PhilippineTime.Today);
        Assert.Empty(laterOnly.Lines); // posting timestamp and billing month do not move an Aug 31 cash event into September.

        var facilityReports = new FacilityReportsRepository(context);
        var augustTotals = await facilityReports.GetNpmUtilityTotalsAsync(businessDate.Year, businessDate.Month);
        Assert.Equal(8m, augustTotals.WaterCollected); // Legacy Water 3 + canonical WCF line 5, exactly once.
        Assert.Equal(17m, augustTotals.Outstanding);
        var septemberTotals = await facilityReports.GetNpmUtilityTotalsAsync(seed.Period.Year, seed.Period.Month);
        Assert.Equal(10m, septemberTotals.ElecCollected); // unrelated Electricity projection remains on its existing path.
        Assert.Equal(0m, septemberTotals.WaterCollected); // WCF cash belongs to its Collection.BusinessDate in August.
        Assert.Equal(78m, septemberTotals.Outstanding); // source position still includes the canonical Water balance.

        Assert.Single(await context.Collections.ToListAsync());
        Assert.Single(await context.CollectionLines.ToListAsync());
        var activity = Assert.Single((await workflow.GetActivityAsync(businessDate, businessDate)).Value!);
        Assert.Equal(posted.Value.CollectionId, activity.CollectionId);
        Assert.Equal(5m, activity.Lines.Sum(x => x.Amount));
        Assert.Contains(activity.Lines, x => x.ClassificationName == "Water Consumption Fee");
        var canonicalBill = await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(12m, canonicalBill.WaterAmountPaid); // balance projection remains correct, but is not another receipt.
        Assert.Equal(8m, canonicalBill.WaterBalanceDue);
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
    public async Task WebWcfIsRetired_AndALegacySourceIsRejectedOnMobile_WithoutTouchingAnyPhysicalForm()
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
        Assert.DoesNotContain("RECONCILIATION_REQUIRED", mobileAttempt.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AccountableDocumentState.Assigned,
            (await mobileContext.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId)).State);   // the physical ticket is never touched (IA-062)
        Assert.Equal(PostingOperationStatus.Rejected,
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
        var seed = await SeedAsync(canonicalWater: true, assignTicket: true);
        await using var quoteContext = db.CreateContext(seed.TenantId);
        var quoteResult = await Workflow(quoteContext, seed, "Collector")
            .GetObligationsAsync(seed.Period.Year, seed.Period.Month);
        Assert.True(quoteResult.IsSuccess, quoteResult.Error);
        var quote = Assert.Single(quoteResult.Value!);
        var request = new WcfCollectionPostRequest(1, Guid.NewGuid(), seed.Period,
            seed.BillId, 5m, quote.WaterSourceVersion, seed.CtDocumentId, seed.CtNumber,
            DateTime.UtcNow.AddMinutes(-1));

        await using var contextA = db.CreateContext(seed.TenantId);
        await using var contextB = db.CreateContext(seed.TenantId);
        var attempts = await Task.WhenAll(
            Workflow(contextA, seed, "Collector").PostMobileAsync(request),
            Workflow(contextB, seed, "Collector").PostMobileAsync(request));

        Assert.All(attempts, result => Assert.True(result.IsSuccess, result.Error));
        Assert.Single(attempts.Select(x => x.Value!.CollectionId).Distinct());
        await using var verify = db.CreateContext(seed.TenantId);
        Assert.Single(await verify.Collections.ToListAsync());
        Assert.Single(await verify.PostingOperations.Where(x => x.ClientOperationId == request.ClientOperationId).ToListAsync());
        Assert.Equal(AccountableDocumentState.Assigned,
            (await verify.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId)).State);
    }

    [SkippableFact]
    public async Task RetiredWebChannelRejectsNewWcfPostingWhileMobileCollectsExactlyOnce()
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
        Assert.False(attempts[0].IsSuccess);
        Assert.Contains("Collector Mobile", attempts[0].Error, StringComparison.Ordinal);
        Assert.True(attempts[1].IsSuccess, attempts[1].Error);

        await using var verify = db.CreateContext(seed.TenantId);
        var collection = Assert.Single(await verify.Collections.ToListAsync());
        Assert.Equal(attempts[1].Value!.CollectionId, collection.Id);
        Assert.Equal(5m, collection.TotalAmount);
        var bill = await verify.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
        Assert.Equal(12m, bill.WaterAmountPaid);
        Assert.Equal(AccountableDocumentState.Assigned,
            (await verify.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId)).State);
        var officeTicket = await verify.AccountableDocuments.SingleAsync(x => x.Id == seed.OfficeCtDocumentId);
        Assert.Equal(AccountableDocumentState.InOffice, officeTicket.State); // never issued by the retired path
        Assert.Null(officeTicket.ClientOperationId);
        var webOperation = await verify.PostingOperations.SingleAsync(x => x.ClientOperationId == webRequest.ClientOperationId);
        Assert.Equal(PostingOperationStatus.Rejected, webOperation.Status);
        Assert.Equal("WEB_CHANNEL_RETIRED", webOperation.OutcomeCode);
        Assert.Null(webOperation.CollectionId);

        // Retrying the same retired Web intent returns the durable rejection; it never posts later.
        var webRetry = await Workflow(verify, seed, "Admin").PostWebAsync(webRequest);
        Assert.False(webRetry.IsSuccess);
        Assert.Single(await verify.Collections.ToListAsync());
        Assert.Single(await verify.PostingOperations.Where(x => x.ClientOperationId == webRequest.ClientOperationId).ToListAsync());
    }

    [SkippableFact]
    public async Task HistoricalWebWcfOutcomeStillReplaysAfterWebChannelRetirement()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true);
        var operationId = Guid.NewGuid();
        var businessDate = PhilippineTime.Today;
        Guid historicalCollectionId;
        long waterVersion;

        // Recreate a Web-origin Collection exactly as the pre-retirement Web path recorded it: same origin, actor
        // and normalized intent shape. Durable intents must stay replayable across later channel decisions.
        await using (var history = db.CreateContext(seed.TenantId))
        {
            var bill = await history.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
            waterVersion = bill.WaterSourceVersion;
            var classification = await history.RevenueClassifications.SingleAsync();
            var policy = await history.RevenueClassificationPolicies.SingleAsync();
            var document = await history.AccountableDocuments.SingleAsync(x => x.Id == seed.OfficeCtDocumentId);
            var actorId = seed.AdminId.ToString("N");
            var normalized = System.Text.Json.JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                OperationType = "WcfWaterCollection",
                TenantId = seed.TenantId,
                ActorId = actorId,
                BusinessDate = businessDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                SourceKind = CollectionSourceKind.UtilityBill.ToString(),
                SourceId = seed.BillId,
                SourcePart = CollectionSourcePart.Water.ToString(),
                ReceivedAmount = "5.00",
                WaterSourceVersion = waterVersion,
                Instrument = RevenueInstrumentType.CashTicket.ToString(),
                AccountableDocumentId = seed.OfficeCtDocumentId,
                DocumentNumber = seed.OfficeCtNumber,
                IssuedAtUtc = (string?)null
            }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            var line = new CollectionLineDraft(classification, policy, 5m,
                CollectionSourceKind.UtilityBill, seed.BillId, CollectionSourcePart.Water, "{}",
                [new CollectionAllocationDraft(CollectionSourceKind.UtilityBill, seed.BillId, 5m, CollectionSourcePart.Water, "{}")]);
            var collection = await new CanonicalCollectionPostingCoordinator(history).PostAsync(
                seed.TenantId, operationId, 1, normalized, "WebWcf", actorId, "historical-office", "Admin",
                businessDate, "historical-office", [line], document,
                sourceProjections: [now => bill.ApplyCanonicalWaterProjection(12m, document.DocumentNumber, now, "historical-office")]);
            historicalCollectionId = collection.Id;
        }

        await using var context = db.CreateContext(seed.TenantId);
        var request = new WcfCollectionPostRequest(1, operationId, businessDate, seed.BillId, 5m,
            waterVersion, seed.OfficeCtDocumentId, seed.OfficeCtNumber, null);
        var replay = await Workflow(context, seed, "Admin").PostWebAsync(request);

        Assert.True(replay.IsSuccess, replay.Error);
        Assert.True(replay.Value!.ExistingOutcome);
        Assert.Equal(historicalCollectionId, replay.Value.CollectionId);
        var changed = await Workflow(context, seed, "Admin").PostWebAsync(request with { ReceivedAmount = 6m });
        Assert.False(changed.IsSuccess);
        Assert.Contains("IDEMPOTENCY CONFLICT", changed.Error, StringComparison.Ordinal);
        var historical = Assert.Single(await context.PostingOperations.ToListAsync());
        Assert.Equal("WebWcf", historical.Origin); // Web-origin evidence is preserved, never relabelled Mobile.
        Assert.Single(await context.Collections.ToListAsync());
        Assert.Equal(12m, (await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).WaterAmountPaid);
    }

    [SkippableFact]
    public async Task MobileRejection_RecordsNothing_AndNeverTouchesAPhysicalTicket()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true, assignTicket: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed, "Collector");
        var quote = Assert.Single((await workflow.GetObligationsAsync(seed.Period.Year, seed.Period.Month)).Value!);

        var overOutstanding = await workflow.PostMobileAsync(new WcfCollectionPostRequest(
            1, Guid.NewGuid(), PhilippineTime.Today, seed.BillId, quote.OutstandingAmount + 1m,
            quote.WaterSourceVersion, seed.CtDocumentId, seed.CtNumber, DateTime.UtcNow.AddMinutes(-1)));

        Assert.False(overOutstanding.IsSuccess);
        Assert.Contains("exceeds", overOutstanding.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.Collections.ToListAsync());
        Assert.Equal(AccountableDocumentState.Assigned,
            (await context.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId)).State);
        Assert.Equal(7m, (await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId)).WaterAmountPaid);
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
        Assert.Equal(1, readiness.Value.ActiveAssignedDocuments);   // reported for information only (IA-062)

        var frozen = await workflow.FreezeOpeningPositionAsync(new SettlementCutoverFreezeRequest(
            scope, readiness.Value.SourceVersion, readiness.Value.ReadinessFingerprint, evidence));
        Assert.True(frozen.IsSuccess, frozen.Error);

        var beforeCustodyChange = await context.UtilityBills.AsNoTracking().SingleAsync(x => x.Id == seed.BillId);
        // A real financial/readiness change after the freeze: an unresolved posting operation now references the source.
        context.PostingOperations.Add(PostingOperation.Record(seed.TenantId, Guid.NewGuid(), 1,
            $"{{\"sourceId\":\"{seed.BillId:D}\",\"sourcePart\":\"Water\"}}", "Mobile/Wcf", "collector",
            PostingOperationStatus.ReconciliationRequired, "SYNC_ISSUE", "Awaiting office review.", null, null, DateTime.UtcNow));
        await context.SaveChangesAsync();

        var reevaluated = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, evidence));
        Assert.True(reevaluated.IsSuccess, reevaluated.Error);
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
    public async Task PhysicalFormInventoryNeverBlocksACanonicalCutover_EvenWhenNotAttestedReconciled()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: false, assignTicket: true);
        await using var context = db.CreateContext(seed.TenantId);
        var scope = new SettlementCutoverScope(CollectionSourceKind.UtilityBill, seed.BillId, CollectionSourcePart.Water);
        var workflow = new SettlementCutoverWorkflow(context,
            new TestActor(seed.AdminId, seed.TenantId, "Admin"), new FixedTenant(seed.TenantId));
        Assert.True((await workflow.BeginPendingCutoverAsync(scope)).IsSuccess);
        // The ticket in collector custody is a physical-stock fact; the inventory flag is NOT attested.
        var evidence = new SettlementCutoverReconciliationEvidence(true, true, true, true, false, true,
            "isolated-integration-test:stock-informational", new[]
            {
                new CutoverCollectorEvidence(seed.CollectorId!.Value, "1.1.11", 1, true, DateTime.UtcNow, "device-a"),
                new CutoverCollectorEvidence(seed.OtherCollectorId!.Value, "1.1.11", 1, true, DateTime.UtcNow, "device-b")
            });

        var ready = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, evidence));

        Assert.True(ready.Value!.Ready, string.Join(" | ", ready.Value.BlockingReasons));
        Assert.Equal(1, ready.Value.ActiveAssignedDocuments);
        Assert.DoesNotContain(ready.Value.BlockingReasons, r => r.Contains("accountable-document", StringComparison.OrdinalIgnoreCase));

        // Device/offline safety still gates: an undrained queue blocks regardless of paper stock.
        var undrained = await workflow.EvaluateReadinessAsync(new SettlementCutoverReadinessRequest(scope, evidence with { MobileQueuesDrained = false }));
        Assert.False(undrained.Value!.Ready);
        Assert.Contains(undrained.Value.BlockingReasons, r => r.Contains("offline queues", StringComparison.OrdinalIgnoreCase));
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
        await context.Entry(assignment).ReloadAsync();
        returnedAt = assignment.ReturnedAtUtc!.Value; // PostgreSQL timestamps store microsecond precision.

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

    [SkippableFact]
    public async Task MobileWcfPostingNeedsExplicitOperationAssignment_AndARefusalIsDurableWithoutTouchingATicket()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true, assignTicket: true, assignWcfOperation: false);
        await using var context = db.CreateContext(seed.TenantId);
        var bill = await context.UtilityBills.SingleAsync(x => x.Id == seed.BillId);
        var workflow = Workflow(context, seed, "Collector");
        var request = new WcfCollectionPostRequest(1, Guid.NewGuid(), PhilippineTime.Today,
            seed.BillId, 1m, bill.WaterSourceVersion, seed.CtDocumentId, seed.CtNumber,
            DateTime.UtcNow.AddSeconds(-1));

        var result = await workflow.PostMobileAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Contains("explicit WCF operation assignment", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await context.Collections.ToListAsync());
        var document = await context.AccountableDocuments.SingleAsync(x => x.Id == seed.CtDocumentId);
        Assert.Equal(AccountableDocumentState.Assigned, document.State);
        Assert.Null(document.ClientOperationId);
        Assert.Equal(PostingOperationStatus.Rejected,
            (await context.PostingOperations.SingleAsync()).Status);
    }

    [SkippableFact]
    public async Task MobileWcfRetryReturnsOriginalCollectionAfterOperationAssignmentIsRemoved()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true, assignTicket: true);
        await using var context = db.CreateContext(seed.TenantId);
        var workflow = Workflow(context, seed, "Collector");
        var quotes = await workflow.GetObligationsAsync(seed.Period.Year, seed.Period.Month);
        Assert.True(quotes.IsSuccess, quotes.Error);
        var quote = Assert.Single(quotes.Value!);
        var request = new WcfCollectionPostRequest(1, Guid.NewGuid(), PhilippineTime.Today,
            seed.BillId, 1m, quote.WaterSourceVersion, seed.CtDocumentId, seed.CtNumber,
            DateTime.UtcNow.AddSeconds(-1));

        var posted = await workflow.PostMobileAsync(request);
        Assert.True(posted.IsSuccess, posted.Error);
        var assignments = await context.CollectorOperationAssignments
            .Where(x => x.CollectorId == seed.CollectorId).ToListAsync();
        context.CollectorOperationAssignments.RemoveRange(assignments);
        await context.SaveChangesAsync();

        var retried = await workflow.PostMobileAsync(request);

        Assert.True(retried.IsSuccess, retried.Error);
        Assert.Equal(posted.Value!.CollectionId, retried.Value!.CollectionId);
        Assert.Single(await context.Collections.ToListAsync());
    }

    private sealed class NowClock : EEMOCantilanSDS.Application.Common.Interface.Time.IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
        public DateOnly PhilippineToday => PhilippineTime.Today;
    }

    [SkippableFact]
    public async Task MobileCapabilityReportsWcfReadyOnlyForACanonicalSourceAndThePostingThenSucceeds()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var legacySeed = await SeedAsync(canonicalWater: false, assignTicket: true);
        await using (var legacyContext = db.CreateContext(legacySeed.TenantId))
        {
            var legacy = await new EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities.GetCollectorOperationCapabilitiesQueryHandler(
                    legacyContext, new TestActor(legacySeed.CollectorId!.Value, legacySeed.TenantId, "Collector"),
                    new FixedTenant(legacySeed.TenantId), new NowClock())
                .Handle(new EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities.GetCollectorOperationCapabilitiesQuery(), default);
            Assert.True(legacy.IsSuccess, legacy.Error);
            var wcf = legacy.Value!.Operations.Single(x => x.OperationCode == CollectorOperationCodes.Wcf);
            Assert.Equal(CollectorOperationCapabilityStatus.PendingCutover, wcf.Status);
            Assert.False(wcf.IsCollectible);
        }

        await db.ResetAsync();
        var seed = await SeedAsync(canonicalWater: true, assignTicket: true);
        await using var context = db.CreateContext(seed.TenantId);
        var capabilities = await new EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities.GetCollectorOperationCapabilitiesQueryHandler(
                context, new TestActor(seed.CollectorId!.Value, seed.TenantId, "Collector"),
                new FixedTenant(seed.TenantId), new NowClock())
            .Handle(new EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities.GetCollectorOperationCapabilitiesQuery(), default);
        Assert.True(capabilities.IsSuccess, capabilities.Error);
        Assert.Equal(CollectorOperationCapabilityStatus.Ready,
            capabilities.Value!.Operations.Single(x => x.OperationCode == CollectorOperationCodes.Wcf).Status);
        Assert.Empty(await context.PostingOperations.ToListAsync()); // the capability read wrote nothing

        var workflow = Workflow(context, seed, "Collector");
        var quote = Assert.Single((await workflow.GetObligationsAsync(seed.Period.Year, seed.Period.Month)).Value!);
        var posted = await workflow.PostMobileAsync(new WcfCollectionPostRequest(1, Guid.NewGuid(), PhilippineTime.Today,
            seed.BillId, 1m, quote.WaterSourceVersion, seed.CtDocumentId, seed.CtNumber, DateTime.UtcNow.AddMinutes(-1)));
        Assert.True(posted.IsSuccess, posted.Error);
    }

    private async Task<Seed> SeedAsync(bool canonicalWater, bool assignTicket = false, bool assignWcfOperation = true)
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
            if (assignWcfOperation)
                collector.OperationAssignments.Add(CollectorOperationAssignment.Assign(
                    tenantId, collector.Id, CollectorOperationCodes.Wcf, "test-admin"));
            ct.AssignTo(collector.Id, "test-admin");
            assignment = AccountableFormAssignment.Assign(ct, collector.Id, adminId.ToString("N"),
                DateTime.UtcNow, "test");
            otherCollector = CollectorUser.Create("Other Test Collector", "WCF-02", "wcf-" + Guid.NewGuid().ToString("N")[..8],
                null, null, new HashedPassword("test-hash"), tenantId);
            otherCollector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(
                otherCollector.Id, facility.Id, FacilityCode.NPM));
            if (assignWcfOperation)
                otherCollector.OperationAssignments.Add(CollectorOperationAssignment.Assign(
                    tenantId, otherCollector.Id, CollectorOperationCodes.Wcf, "test-admin"));
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
