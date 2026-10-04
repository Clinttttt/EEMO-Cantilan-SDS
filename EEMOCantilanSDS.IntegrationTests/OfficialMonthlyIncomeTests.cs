using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
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

/// <summary>
/// The official Monthly Income assembler (IA-051): authoritative legacy cash before a source's cutover plus authoritative
/// canonical cash after it, exactly once, in the office statement's rows. A converted row's legacy projection is never
/// added beside its Collection, and a remittance never changes the statement.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OfficialMonthlyIncomeTests(PostgresFixture db)
{
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Caller(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "collector" : "office";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "pg-income";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private static readonly DateOnly Today = PhilippineTime.Today;

    private sealed record World(Municipality Tenant, CollectorUser Collector, Guid HeadId, Guid IceStallId, Guid[] OrIds, AccountableDocument[] CtDocuments);

    private async Task<World> SeedAsync()
    {
        var tenant = Municipality.Create($"inc-{Guid.NewGuid():N}"[..12], "Income Test", "Province", MunicipalityStatus.Active,
            tenantCode: $"income-{Guid.NewGuid():N}"[..28].ToLowerInvariant());
        var collector = CollectorUser.Create("Ana Reyes", "C-01", $"pg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, collector);
            await setup.SaveChangesAsync();
        }
        var head = Guid.NewGuid();
        var period = new DateOnly(Today.Year, Today.Month, 1);
        var effective = new DateOnly(2000, 1, 1);
        Guid iceStall;
        Guid[] orIds;
        AccountableDocument[] ctDocs;
        await using (var ctx = db.CreateContext(tenant.Id))
        {
            // Classifications and OR/CT policies.
            foreach (var (code, instrument) in new[]
                     {
                         (RevenueClassificationCodes.IcePlant, RevenueInstrumentType.OfficialReceipt),
                         (RevenueClassificationCodes.PermanentStallRent, RevenueInstrumentType.OfficialReceipt),
                         (RevenueClassificationCodes.MarketFees, RevenueInstrumentType.CashTicket)
                     })
            {
                var classification = RevenueClassification.Create(code, tenant.Id);
                ctx.Add(classification);
                ctx.Add(RevenueClassificationPolicy.Create(classification.Id, effective, code, instrument, tenant.Id));
            }
            // A canonical ICE rent source (converted) and a legacy TCC rent source (still legacy).
            var iceFacility = Facility.Create(FacilityCode.ICE, "Ice Plant", "ICE", municipalityId: tenant.Id);
            var tccFacility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC", municipalityId: tenant.Id);
            var payor = Payor.Create(tenant.Id, "Pedro Vendor", BusinessPayorKind.Person, "test");
            var iceStallEntity = Stall.Create(iceFacility.Id, "ICE-01", 1000m, ApplicableFees.BaseRental, municipalityId: tenant.Id);
            var contract = Contract.Create(iceStallEntity.Id, "Pedro Vendor", "Pedro Vendor", period, 5, 1000m, createdBy: "test");
            contract.AssociatePayor(payor.Id, "test");
            var tccStall = Stall.Create(tccFacility.Id, "TCC-01", 900m, ApplicableFees.BaseRental | ApplicableFees.Electricity, municipalityId: tenant.Id);
            var iceRecord = PaymentRecord.Create(iceStallEntity.Id, period.Year, period.Month, 1000m, "test");
            iceRecord.MarkSettlementPendingCutover();
            var at = DateTime.UtcNow.AddMinutes(-3);
            var cutover = CollectionSettlementCutover.Freeze(tenant.Id, CollectionSourceKind.PaymentRecord, iceRecord.Id, null,
                iceRecord.SettlementVersion, at, 1000m, 0m, 1000m,
                "{\"pendingMobileOperations\":0,\"issuedDocuments\":0,\"legacyWritersQuiesced\":true}", head, at.AddMinutes(1));
            iceRecord.ActivateCanonicalSettlement(cutover);
            var tccRecord = PaymentRecord.Create(tccStall.Id, period.Year, period.Month, 900m, "test");
            tccRecord.RecordPayment("LEGACY-1", collector.Id, PaymentStatus.Paid, updatedBy: "test");
            var bill = UtilityBill.Create(tccStall.Id, period.Year, period.Month, 0m, 80m, 10m, 0m, 0m, 0m, "test");
            bill.RecordPayment("LEGACY-E", null, null, PaymentStatus.Partial, 100m, PaymentStatus.Unpaid, null, updatedBy: "test");
            ctx.AddRange(iceFacility, tccFacility, payor, iceStallEntity, contract, tccStall, iceRecord, tccRecord, bill);
            ctx.CollectionSettlementCutovers.Add(cutover);
            // Governed Market Fees (CT, fixed 30) and custody: OR docs in office, CT docs assigned to the collector.
            var service = GovernedService.Create(tenant.Id, CollectorOperationCodes.MarketFees, "head");
            ctx.Add(service);
            ctx.Add(GovernedServiceSetting.Create(tenant.Id, service.Id, effective, GovernedServiceBasis.FixedAmount, 30m, null, true, true, "head"));
            ctx.Add(CollectorOperationAssignment.Assign(tenant.Id, collector.Id, CollectorOperationCodes.MarketFees, "head"));
            await ctx.SaveChangesAsync();
            var custody = new AccountableFormCustodyWorkflow(ctx, new Caller(head, tenant.Id, "SuperAdmin"), new FixedTenant(tenant.Id));
            var orBook = (await custody.ReceiveAsync(new ReceiveAccountableFormBookRequest(RevenueInstrumentType.OfficialReceipt, "OR", "OR-", 1, 5, 4))).Value!;
            var ctBook = (await custody.ReceiveAsync(new ReceiveAccountableFormBookRequest(RevenueInstrumentType.CashTicket, "CT", "CT-", 1, 5, 4))).Value!;
            Assert.True((await custody.AssignRangeAsync(new AssignAccountableFormRangeRequest(ctBook.BookId, collector.Id, 1, 5))).IsSuccess);
            orIds = (await ctx.AccountableDocuments.Where(x => x.FormBookId == orBook.BookId).OrderBy(x => x.SerialNumber).ToListAsync()).Select(x => x.Id).ToArray();
            ctDocs = (await ctx.AccountableDocuments.Where(x => x.FormBookId == ctBook.BookId).OrderBy(x => x.SerialNumber).ToListAsync()).ToArray();
            iceStall = iceStallEntity.Id;
        }
        return new World(tenant, collector, head, iceStall, orIds, ctDocs);
    }

    private OfficialMonthlyIncomeDto Statement(World w, AppDbContext context, string role = "Admin") =>
        new GetOfficialMonthlyIncomeQueryHandler(context, new LegacyMonthlyIncomeReader(context),
            new Caller(w.HeadId, w.Tenant.Id, role), new FixedTenant(w.Tenant.Id), new Clock())
            .Handle(new GetOfficialMonthlyIncomeQuery(Today.Year, null), CancellationToken.None).GetAwaiter().GetResult().Value!;

    private static decimal Cell(OfficialMonthlyIncomeDto dto, string rowKey, Func<MonthlyIncomeCellDto, decimal> pick) =>
        pick(dto.Groups.SelectMany(g => g.Rows).Single(r => r.Key == rowKey).Total);

    [SkippableFact]
    public async Task AMixedPeriod_CountsLegacyBeforeCutoverAndCanonicalAfter_ExactlyOnce_AndARemittanceChangesNothing()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var period = new DateOnly(Today.Year, Today.Month, 1);

        // Canonical: the converted ICE rent (250 on an OR) and a Market Fees CT collected on Mobile (30).
        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            var composer = new CollectionComposerWorkflow(ctx, new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id));
            var draft = (await composer.AddRentAllocationAsync(new AddRentDraftAllocationRequest(w.IceStallId, period.Year, period.Month, 250m))).Value!;
            var selected = (await composer.SelectDocumentAsync(draft.DraftId, new SelectEcfDraftDocumentRequest(draft.Revision, w.OrIds[0]))).Value!;
            var reviewed = (await composer.ReviewAsync(draft.DraftId, new EcfDraftRevisionRequest(selected.Revision))).Value!;
            Assert.True((await composer.PostAsync(draft.DraftId, new PostEcfCollectionDraftRequest(reviewed.Revision, Guid.NewGuid()))).IsSuccess);
        }
        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            var workflow = new GovernedServiceWorkflow(ctx, new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id));
            var doc = w.CtDocuments[0];
            Assert.True((await workflow.PostMobileAsync(new GovernedServicePostRequest(
                1, Guid.NewGuid(), CollectorOperationCodes.MarketFees, Today, 30m, null, "Walk-up", null, doc.Id, doc.DocumentNumber,
                DateTime.UtcNow.AddMinutes(-1)))).IsSuccess);
        }

        await using var read = db.CreateContext(w.Tenant.Id);
        var before = Statement(w, read);

        // Legacy-authoritative rows are counted from their sources ...
        Assert.Equal(900m, Cell(before, "RENT_TCC", c => c.Legacy));
        Assert.Equal(100m, Cell(before, "ECF", c => c.Legacy));
        // ... canonical-authoritative rows from their Collections ...
        Assert.Equal(30m, Cell(before, "MARKET_FEES", c => c.Canonical));
        Assert.Equal(250m, Cell(before, "ICE_PLANT", c => c.Canonical));
        // ... and the converted ICE row's legacy projection (status/partial 250) is NOT added beside its Collection.
        Assert.Equal(0m, Cell(before, "ICE_PLANT", c => c.Legacy));
        Assert.Equal(900m + 100m + 30m + 250m, before.GrandTotal.Total);
        // Rows follow the statement: ICE is Income from Market, TCC rent is Rent Income, nothing is placed by facility guess.
        Assert.Contains(before.Groups.Single(g => g.Key == OfficialMonthlyIncomeStructure.Market).Rows, r => r.Key == "ICE_PLANT" && r.Total.Total == 250m);
        Assert.Contains(before.Groups.Single(g => g.Key == OfficialMonthlyIncomeStructure.Rent).Rows, r => r.Key == "RENT_TCC" && r.Total.Total == 900m);
        Assert.Equal("Canonical", before.Groups.SelectMany(g => g.Rows).Single(r => r.Key == "ICE_PLANT").Authority);
        Assert.Equal("Legacy", before.Groups.SelectMany(g => g.Rows).Single(r => r.Key == "RENT_TCC").Authority);

        // A remittance is not income: covering the Market Fees collection changes the statement not at all.
        var remit = new RemittanceWorkflow(read, new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id));
        var recorded = await remit.RecordAsync(new RecordRemittanceRequest(Guid.NewGuid(), w.Collector.Id, Today, Today.AddDays(-1), Today,
            RevenueInstrumentType.CashTicket, null, 30m, "ACK", null));
        Assert.True(recorded.IsSuccess, recorded.Error);
        var after = Statement(w, read);
        Assert.Equal(before.GrandTotal.Total, after.GrandTotal.Total);
        Assert.Equal(before.MonthTotals.Select(x => x.Total), after.MonthTotals.Select(x => x.Total));
    }

    [SkippableFact]
    public async Task ACorrectionReducesTheRowOfTheOriginalPeriod_AndAnUnknownClassificationIsKeptNotDropped()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            var workflow = new GovernedServiceWorkflow(ctx, new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id));
            var doc = w.CtDocuments[0];
            Assert.True((await workflow.PostMobileAsync(new GovernedServicePostRequest(
                1, Guid.NewGuid(), CollectorOperationCodes.MarketFees, Today, 30m, null, null, null, doc.Id, doc.DocumentNumber,
                DateTime.UtcNow.AddMinutes(-1)))).IsSuccess);
        }
        await using var read = db.CreateContext(w.Tenant.Id);
        Assert.Equal(30m, Cell(Statement(w, read), "MARKET_FEES", c => c.Canonical));
        // Only office staff read it; a collector cannot.
        var forbidden = await new GetOfficialMonthlyIncomeQueryHandler(read, new LegacyMonthlyIncomeReader(read),
            new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id), new Clock())
            .Handle(new GetOfficialMonthlyIncomeQuery(Today.Year, null), CancellationToken.None);
        Assert.Equal(ResultStatus.Forbidden, forbidden.Status);
    }

    [SkippableFact]
    public async Task AShadowLineOnAStillLegacyRow_IsNotCountedBesideItsLegacyMoney_ButTwoIdenticalGenuineCollectionsBothAre()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var tccRecordId = await ReadTccRecordIdAsync(w);

        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            // Two genuine Market Fees collections: same day, same amount, no payor, different physical CTs.
            var workflow = new GovernedServiceWorkflow(ctx, new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id));
            foreach (var doc in w.CtDocuments.Take(2))
                Assert.True((await workflow.PostMobileAsync(new GovernedServicePostRequest(
                    1, Guid.NewGuid(), CollectorOperationCodes.MarketFees, Today, 30m, null, "Walk-up", null, doc.Id, doc.DocumentNumber,
                    DateTime.UtcNow.AddMinutes(-1)))).IsSuccess);

            // A hypothetical future writer posts a canonical line against the TCC rent row whose legacy money (900) is
            // still authoritative, plus a correction against it. Neither may appear beside the legacy figure.
            var rentClass = await ctx.RevenueClassifications.SingleAsync(x => x.SemanticCode == RevenueClassificationCodes.PermanentStallRent);
            var rentPolicy = await ctx.RevenueClassificationPolicies.SingleAsync(x => x.RevenueClassificationId == rentClass.Id);
            var shadow = Collection.Post(Today, DateTime.UtcNow.AddMinutes(-2), "head", "Head", "Admin",
                [new CollectionLineDraft(rentClass, rentPolicy, 900m, CollectionSourceKind.PaymentRecord, tccRecordId, null, null,
                    [new CollectionAllocationDraft(CollectionSourceKind.PaymentRecord, tccRecordId, 900m, null)])]);
            ctx.Add(shadow);
            var shadowLine = shadow.Lines.Single();
            ctx.Add(CollectionCorrection.Record(w.Tenant.Id, shadow.Id, null, null, null, CollectionCorrectionType.Reversal, Today,
                DateTime.UtcNow, -100m, "shadow partial correction", "head", "Head",
                [new CollectionCorrectionLineDraft(shadowLine.Id, -100m,
                    shadowLine.Allocations.Select(a => new CollectionCorrectionAllocationDraft(a.Id, -100m)).ToList())]));
            await ctx.SaveChangesAsync();
        }

        await using var read = db.CreateContext(w.Tenant.Id);
        var statement = Statement(w, read);
        Assert.Equal(900m, Cell(statement, "RENT_TCC", c => c.Legacy));
        Assert.Equal(0m, Cell(statement, "RENT_TCC", c => c.Canonical));
        Assert.Equal(60m, Cell(statement, "MARKET_FEES", c => c.Canonical));
        Assert.Equal(900m + 100m + 60m, statement.GrandTotal.Total);
    }

    private async Task<Guid> ReadTccRecordIdAsync(World w)
    {
        await using var ctx = db.CreateContext(w.Tenant.Id);
        return await ctx.PaymentRecords.Where(x => x.BaseRentalAmount == 900m).Select(x => x.Id).SingleAsync();
    }

    [SkippableFact]
    public async Task TheRegisterAndItsRcdSummaryAreDerivedFromPostedCollections_AndASerialTracesToItsRemittance()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var period = new DateOnly(Today.Year, Today.Month, 1);
        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            var composer = new CollectionComposerWorkflow(ctx, new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id));
            var draft = (await composer.AddRentAllocationAsync(new AddRentDraftAllocationRequest(w.IceStallId, period.Year, period.Month, 250m))).Value!;
            var selected = (await composer.SelectDocumentAsync(draft.DraftId, new SelectEcfDraftDocumentRequest(draft.Revision, w.OrIds[0]))).Value!;
            var reviewed = (await composer.ReviewAsync(draft.DraftId, new EcfDraftRevisionRequest(selected.Revision))).Value!;
            Assert.True((await composer.PostAsync(draft.DraftId, new PostEcfCollectionDraftRequest(reviewed.Revision, Guid.NewGuid()))).IsSuccess);
            var workflow = new GovernedServiceWorkflow(ctx, new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id));
            foreach (var index in new[] { 0, 1 })
            {
                var doc = w.CtDocuments[index];
                Assert.True((await workflow.PostMobileAsync(new GovernedServicePostRequest(
                    1, Guid.NewGuid(), CollectorOperationCodes.MarketFees, Today, 30m, null, null, null, doc.Id, doc.DocumentNumber,
                    DateTime.UtcNow.AddMinutes(-1)))).IsSuccess);
            }
        }

        await using var read = db.CreateContext(w.Tenant.Id);
        var reports = new CollectionsReportWorkflow(read, new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id));
        var register = (await reports.GetRegisterAsync(Today.AddDays(-1), Today, null, null, null)).Value!;

        // Three posted collections, three classified rows; the summary is derived from the same rows.
        Assert.Equal(3, register.Rows.Count);
        Assert.Equal(310m, register.Net);
        var market = Assert.Single(register.Summary, x => x.ClassificationName == "MARKET_FEES");
        Assert.Equal((2, 60m), (market.CollectionCount, market.Net));
        Assert.NotNull(market.FirstReferenceCode);
        Assert.NotEqual(market.FirstReferenceCode, market.LastReferenceCode);
        Assert.Contains(register.Rows, r => r.ClassificationName == "ICE_PLANT" && r.ReferenceCode.StartsWith("SRC-") && r.Instrument == RevenueInstrumentType.OfficialReceipt);
        // Filters are honoured on the server.
        Assert.Equal(2, (await reports.GetRegisterAsync(Today.AddDays(-1), Today, w.Collector.Id, RevenueInstrumentType.CashTicket, null)).Value!.Rows.Count);

        // A remittance covering the two CTs shows up when a serial is traced; an unused serial says where it is held.
        var remit = new RemittanceWorkflow(read, new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id));
        Assert.True((await remit.RecordAsync(new RecordRemittanceRequest(Guid.NewGuid(), w.Collector.Id, Today, Today.AddDays(-1), Today,
            RevenueInstrumentType.CashTicket, null, 60m, "ACK", null))).IsSuccess);
        // The remittance that covers a collection is read from the collection itself, by its SRC (IA-062).
        var ctRow = register.Rows.First(r => r.Instrument == RevenueInstrumentType.CashTicket);
        var detail = (await reports.GetCollectionAsync(ctRow.CollectionId)).Value!;
        Assert.Equal(ctRow.ReferenceCode, detail.ReferenceCode);
        Assert.Equal(30m, detail.Total);
        Assert.NotNull(detail.RemittanceId);
        Assert.Equal("Ana Reyes", detail.CollectorName);
        // A registered physical serial is no longer consumed by a collection: tracing it finds custody only.
        var unused = (await reports.TraceAsync("CT-0003")).Value!;
        Assert.Equal(("Assigned", "Ana Reyes"), (unused.State, unused.Custodian));
        Assert.Null(unused.Collection);
        Assert.Equal(ResultStatus.NotFound, (await reports.TraceAsync("CT-9999")).Status);
    }

    [SkippableFact]
    public async Task CollectionActivity_ListsEveryPostedCanonicalCollection_OncePerCollection_WithNoLegacyDuplicate()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync();
        var period = new DateOnly(Today.Year, Today.Month, 1);
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var composer = new CollectionComposerWorkflow(ctx, new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id));
        var draft = (await composer.AddRentAllocationAsync(new AddRentDraftAllocationRequest(w.IceStallId, period.Year, period.Month, 250m))).Value!;
        var selected = (await composer.SelectDocumentAsync(draft.DraftId, new SelectEcfDraftDocumentRequest(draft.Revision, w.OrIds[0]))).Value!;
        var reviewed = (await composer.ReviewAsync(draft.DraftId, new EcfDraftRevisionRequest(selected.Revision))).Value!;
        Assert.True((await composer.PostAsync(draft.DraftId, new PostEcfCollectionDraftRequest(reviewed.Revision, Guid.NewGuid()))).IsSuccess);
        var workflow = new GovernedServiceWorkflow(ctx, new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id));
        var doc = w.CtDocuments[0];
        Assert.True((await workflow.PostMobileAsync(new GovernedServicePostRequest(
            1, Guid.NewGuid(), CollectorOperationCodes.MarketFees, Today, 30m, null, null, null, doc.Id, doc.DocumentNumber,
            DateTime.UtcNow.AddMinutes(-1)))).IsSuccess);

        var activity = (await composer.GetActivityAsync(Today.AddDays(-1), Today)).Value!;

        // The web-posted OR and the Mobile-posted CT each appear once, under their own document; the converted ICE row's
        // legacy PaymentRecord projection and the still-legacy sources are not listed, so nothing is duplicated.
        Assert.Equal(2, activity.Count);
        Assert.Equal(2, activity.Select(x => x.ReferenceCode).Distinct().Count());
        Assert.All(activity, a => Assert.StartsWith("SRC-", a.ReferenceCode));
        Assert.Equal(280m, activity.Sum(x => x.TotalAmount));
        Assert.All(activity, a => Assert.Equal("Posted", a.CurrentDisposition));
    }
}
