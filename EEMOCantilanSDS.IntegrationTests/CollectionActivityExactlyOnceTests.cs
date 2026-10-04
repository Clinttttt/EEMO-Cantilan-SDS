using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Collections;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Collections.GetCollectionActivity;
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
/// The unified office Collection Activity on PostgreSQL: authoritative legacy before a row's cutover plus posted canonical
/// Collections after it, every real collection exactly once. A converted row's legacy projection, a shadow canonical line on
/// a still-legacy row, a retried post and another tenant's rows are never listed; a correction stays under its original
/// event; an itemized OR is one event; and the period agrees with the official Monthly Income.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CollectionActivityExactlyOnceTests(PostgresFixture db)
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
        public string? MunicipalityCode => "pg-activity";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private static readonly DateOnly Today = PhilippineTime.Today;
    private static readonly DateOnly Period = new(Today.Year, Today.Month, 1);

    private sealed record World(Municipality Tenant, CollectorUser Collector, Guid HeadId, Guid IceStallId,
        Guid IceRecordId, Guid TccRecordId, Guid[] OrIds, AccountableDocument[] CtDocuments);

    private static CollectionSettlementCutover Activate(Guid tenantId, PaymentRecord record, Guid head)
    {
        record.MarkSettlementPendingCutover();
        var at = DateTime.UtcNow.AddMinutes(-3);
        var cutover = CollectionSettlementCutover.Freeze(tenantId, CollectionSourceKind.PaymentRecord, record.Id, null,
            record.SettlementVersion, at, record.BaseRentalAmount, 0m, record.BaseRentalAmount,
            "{\"pendingMobileOperations\":0,\"issuedDocuments\":0,\"legacyWritersQuiesced\":true}", head, at.AddMinutes(1));
        record.ActivateCanonicalSettlement(cutover);
        return cutover;
    }

    private async Task<World> SeedAsync(string label)
    {
        var tenant = Municipality.Create($"act-{Guid.NewGuid():N}"[..12], $"Activity {label}", "Province", MunicipalityStatus.Active,
            tenantCode: $"activity-{Guid.NewGuid():N}"[..28].ToLowerInvariant());
        var collector = CollectorUser.Create("Ana Reyes", "C-01", $"pg-{Guid.NewGuid():N}"[..14], null, null, new HashedPassword("h"), tenant.Id);
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, collector);
            await setup.SaveChangesAsync();
        }
        var head = Guid.NewGuid();
        var effective = new DateOnly(2000, 1, 1);
        await using var ctx = db.CreateContext(tenant.Id);
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
        // Converted ICE rent for this month and last month; a still-legacy TCC rent paid today; a legacy ECF partial.
        var iceFacility = Facility.Create(FacilityCode.ICE, "Ice Plant", "ICE", municipalityId: tenant.Id);
        var tccFacility = Facility.Create(FacilityCode.TCC, "Commercial Center", "TCC", municipalityId: tenant.Id);
        var payor = Payor.Create(tenant.Id, "Pedro Vendor", BusinessPayorKind.Person, "test");
        var iceStall = Stall.Create(iceFacility.Id, "ICE-01", 1000m, ApplicableFees.BaseRental, municipalityId: tenant.Id);
        var contract = Contract.Create(iceStall.Id, "Pedro Vendor", "Pedro Vendor", Period.AddMonths(-1), 5, 1000m, createdBy: "test");
        contract.AssociatePayor(payor.Id, "test");
        var tccStall = Stall.Create(tccFacility.Id, "TCC-01", 900m, ApplicableFees.BaseRental | ApplicableFees.Electricity, municipalityId: tenant.Id);
        var iceRecord = PaymentRecord.Create(iceStall.Id, Period.Year, Period.Month, 1000m, "test");
        var icePrevious = PaymentRecord.Create(iceStall.Id, Period.AddMonths(-1).Year, Period.AddMonths(-1).Month, 1000m, "test");
        var tccRecord = PaymentRecord.Create(tccStall.Id, Period.Year, Period.Month, 900m, "test");
        tccRecord.RecordPayment("LEGACY-1", collector.Id, PaymentStatus.Paid, updatedBy: "test");
        var bill = UtilityBill.Create(tccStall.Id, Period.Year, Period.Month, 0m, 80m, 10m, 0m, 0m, 0m, "test");
        bill.RecordPayment("LEGACY-E", null, null, PaymentStatus.Partial, 100m, PaymentStatus.Unpaid, null, updatedBy: "test");
        ctx.AddRange(iceFacility, tccFacility, payor, iceStall, contract, tccStall, iceRecord, icePrevious, tccRecord, bill);
        ctx.CollectionSettlementCutovers.Add(Activate(tenant.Id, iceRecord, head));
        ctx.CollectionSettlementCutovers.Add(Activate(tenant.Id, icePrevious, head));
        var service = GovernedService.Create(tenant.Id, CollectorOperationCodes.MarketFees, "head");
        ctx.Add(service);
        ctx.Add(GovernedServiceSetting.Create(tenant.Id, service.Id, effective, GovernedServiceBasis.FixedAmount, 30m, null, true, true, "head"));
        ctx.Add(CollectorOperationAssignment.Assign(tenant.Id, collector.Id, CollectorOperationCodes.MarketFees, "head"));
        await ctx.SaveChangesAsync();
        var custody = new AccountableFormCustodyWorkflow(ctx, new Caller(head, tenant.Id, "SuperAdmin"), new FixedTenant(tenant.Id));
        var orBook = (await custody.ReceiveAsync(new ReceiveAccountableFormBookRequest(RevenueInstrumentType.OfficialReceipt, "OR", "OR-", 1, 5, 4))).Value!;
        var ctBook = (await custody.ReceiveAsync(new ReceiveAccountableFormBookRequest(RevenueInstrumentType.CashTicket, "CT", "CT-", 1, 5, 4))).Value!;
        Assert.True((await custody.AssignRangeAsync(new AssignAccountableFormRangeRequest(ctBook.BookId, collector.Id, 1, 5))).IsSuccess);
        var orIds = (await ctx.AccountableDocuments.Where(x => x.FormBookId == orBook.BookId).OrderBy(x => x.SerialNumber).ToListAsync()).Select(x => x.Id).ToArray();
        var ctDocs = (await ctx.AccountableDocuments.Where(x => x.FormBookId == ctBook.BookId).OrderBy(x => x.SerialNumber).ToListAsync()).ToArray();
        return new World(tenant, collector, head, iceStall.Id, iceRecord.Id, tccRecord.Id, orIds, ctDocs);
    }

    private async Task PostRentAsync(World w, params (DateOnly Month, decimal Amount)[] allocations)
    {
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var composer = new CollectionComposerWorkflow(ctx, new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id));
        EEMOCantilanSDS.Application.Dtos.Revenue.EcfCollectionDraftDto? draft = null;
        foreach (var (month, amount) in allocations)
        {
            var added = await composer.AddRentAllocationAsync(
                new AddRentDraftAllocationRequest(w.IceStallId, month.Year, month.Month, amount, draft?.Revision));
            Assert.True(added.IsSuccess, added.Error);
            draft = added.Value!;
        }
        var selected = (await composer.SelectDocumentAsync(draft!.DraftId, new SelectEcfDraftDocumentRequest(draft.Revision, w.OrIds[0]))).Value!;
        var reviewed = (await composer.ReviewAsync(draft.DraftId, new EcfDraftRevisionRequest(selected.Revision))).Value!;
        var posted = await composer.PostAsync(draft.DraftId, new PostEcfCollectionDraftRequest(reviewed.Revision, Guid.NewGuid()));
        Assert.True(posted.IsSuccess, posted.Error);
    }

    private async Task PostMarketFeeAsync(World w, Guid operationId, DateTime? issuedAtUtc = null)
    {
        await using var ctx = db.CreateContext(w.Tenant.Id);
        var workflow = new GovernedServiceWorkflow(ctx, new Caller(w.Collector.Id, w.Tenant.Id, "Collector"), new FixedTenant(w.Tenant.Id));
        var doc = w.CtDocuments[0];
        var posted = await workflow.PostMobileAsync(new GovernedServicePostRequest(
            1, operationId, CollectorOperationCodes.MarketFees, Today, 30m, null, "Walk-up", null, doc.Id, doc.DocumentNumber,
            issuedAtUtc ?? DateTime.UtcNow.AddMinutes(-1)));
        Assert.True(posted.IsSuccess, posted.Error);
    }

    private static Result<CollectionActivityFeedDto> Run(AppDbContext ctx, Guid tenantId, Guid userId, string role = "Admin",
        Guid? claimedTenant = null, DateOnly? from = null, DateOnly? to = null) =>
        new GetCollectionActivityQueryHandler(new CollectionActivityReader(ctx), new Caller(userId, claimedTenant ?? tenantId, role),
                new FixedTenant(tenantId), new Clock())
            .Handle(new GetCollectionActivityQuery(from ?? Period, to ?? Today), CancellationToken.None).GetAwaiter().GetResult();

    private static CollectionActivityFeedDto Feed(AppDbContext ctx, World w)
    {
        var result = Run(ctx, w.Tenant.Id, w.HeadId);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [SkippableFact]
    public async Task AMixedPeriod_ListsEachRealCollectionOnce_AndAgreesWithTheOfficialMonthlyIncome()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("A");
        await PostRentAsync(w, (Period, 250m));
        var operation = Guid.NewGuid();
        var issuedAt = DateTime.UtcNow.AddMinutes(-1);
        await PostMarketFeeAsync(w, operation, issuedAt);
        // A retried Mobile post of the same intent with the same operation id is the same collection, not a second one.
        await PostMarketFeeAsync(w, operation, issuedAt);

        await using var read = db.CreateContext(w.Tenant.Id);
        var feed = Feed(read, w);

        Assert.Equal(4, feed.EventCount);
        var rent = Assert.Single(feed.Events, e => e.EventKey == $"PaymentRecord:{w.TccRecordId}");
        Assert.Equal(("Legacy", 900m, FacilityCode.TCC, "LEGACY-1"), (rent.Authority, rent.Amount, rent.Facility, rent.DocumentNumber));
        Assert.Equal(PermanentLine(rent).ClassificationCode, RevenueClassificationCodes.PermanentStallRent);
        var ecf = Assert.Single(feed.Events, e => e.Source == "UtilityBill");
        Assert.Equal(("Legacy", 100m, RevenueClassificationCodes.Ecf), (ecf.Authority, ecf.Amount, ecf.Lines.Single().ClassificationCode));
        // The converted ICE row's legacy projection is never listed beside its Collection.
        Assert.DoesNotContain(feed.Events, e => e.EventKey == $"PaymentRecord:{w.IceRecordId}");
        // Legacy rows keep the source's own document and are never given a fabricated SRC; canonical rows are identified by SRC.
        Assert.All(feed.Events.Where(e => e.Authority == "Legacy"), e => Assert.Null(e.ReferenceCode));
        Assert.All(feed.Events.Where(e => e.Authority == "Canonical"), e => Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", e.ReferenceCode!));
        var or = Assert.Single(feed.Events, e => e.Authority == "Canonical" && e.Facility == FacilityCode.ICE);
        Assert.Equal(("Canonical", 250m, RevenueInstrumentType.OfficialReceipt, FacilityCode.ICE, "Posted"),
            (or.Authority, or.Amount, or.InstrumentType, or.Facility, or.Disposition));
        Assert.Equal(RevenueClassificationCodes.IcePlant, or.Lines.Single().ClassificationCode);
        var ct = Assert.Single(feed.Events, e => e.Authority == "Canonical" && e.Amount == 30m);
        Assert.Equal(("Canonical", 30m, RevenueInstrumentType.CashTicket, w.Collector.Id, "Ana Reyes"),
            (ct.Authority, ct.Amount, ct.InstrumentType, ct.CollectorId, ct.CollectorName));
        Assert.Equal(Today, ct.BusinessDate);

        Assert.Equal((1000m, 280m, 0m, 1280m), (feed.LegacyAmount, feed.CanonicalAmount, feed.CorrectionEffect, feed.NetAmount));
        Assert.Equal(feed.EventCount, feed.Events.Select(e => e.EventKey).Distinct().Count());

        // The same money, read by the official Monthly Income for the month: one server truth.
        var statement = (await new GetOfficialMonthlyIncomeQueryHandler(read, new LegacyMonthlyIncomeReader(read),
                new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id), new Clock())
            .Handle(new GetOfficialMonthlyIncomeQuery(Today.Year, null), CancellationToken.None)).Value!;
        Assert.Equal(statement.MonthTotals[Today.Month - 1].Legacy, feed.LegacyAmount);
        Assert.Equal(statement.MonthTotals[Today.Month - 1].Canonical, feed.CanonicalAmount + feed.CorrectionEffect);

        // Filters narrow events and totals on the server.
        var canonicalOnly = new GetCollectionActivityQueryHandler(new CollectionActivityReader(read),
                new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id), new Clock())
            .Handle(new GetCollectionActivityQuery(Period, Today, Authority: "Canonical"), CancellationToken.None).Result.Value!;
        Assert.Equal((2, 280m, 0m), (canonicalOnly.EventCount, canonicalOnly.CanonicalAmount, canonicalOnly.LegacyAmount));
        var byCollector = new GetCollectionActivityQueryHandler(new CollectionActivityReader(read),
                new Caller(w.HeadId, w.Tenant.Id, "Admin"), new FixedTenant(w.Tenant.Id), new Clock())
            .Handle(new GetCollectionActivityQuery(Period, Today, CollectorId: w.Collector.Id), CancellationToken.None).Result.Value!;
        Assert.Equal(2, byCollector.Events.Count);
        Assert.Single(byCollector.Events, e => e.Authority == "Legacy" && e.DocumentNumber == "LEGACY-1" && e.ReferenceCode is null);
        Assert.Single(byCollector.Events, e => e.Authority == "Canonical" && e.ReferenceCode!.StartsWith("SRC-"));
    }

    private static CollectionActivityLineDto PermanentLine(CollectionActivityEventDto e) => e.Lines.First();

    [SkippableFact]
    public async Task AnItemizedOfficialReceipt_IsOneEvent_WithItsLineDetail()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("B");
        await PostRentAsync(w, (Period, 250m), (Period.AddMonths(-1), 300m));

        await using var read = db.CreateContext(w.Tenant.Id);
        var feed = Feed(read, w);

        var or = Assert.Single(feed.Events, e => e.Authority == "Canonical");
        Assert.Matches(@"^SRC-[0-9]{4}-[0-9]{6,}$", or.ReferenceCode!);
        Assert.Equal(550m, or.Amount);
        Assert.Equal(550m, or.Lines.Sum(l => l.Amount));
        Assert.All(or.Lines, l => Assert.Equal(nameof(CollectionSourceKind.PaymentRecord), l.SourceKind));
        Assert.Equal(550m, feed.CanonicalAmount);
    }

    [SkippableFact]
    public async Task AShadowLineOnAStillLegacyRow_IsNotListed_AndACorrectionStaysUnderItsOriginalEvent()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var w = await SeedAsync("C");
        await PostMarketFeeAsync(w, Guid.NewGuid());

        Guid shadowId;
        await using (var ctx = db.CreateContext(w.Tenant.Id))
        {
            // A canonical line against the TCC rent row whose legacy money is still authoritative: shadow evidence only.
            var rentClass = await ctx.RevenueClassifications.SingleAsync(x => x.SemanticCode == RevenueClassificationCodes.PermanentStallRent);
            var rentPolicy = await ctx.RevenueClassificationPolicies.SingleAsync(x => x.RevenueClassificationId == rentClass.Id);
            var shadow = Collection.Post(Today, DateTime.UtcNow.AddMinutes(-2), "head", "Head", "Admin",
                [new CollectionLineDraft(rentClass, rentPolicy, 900m, CollectionSourceKind.PaymentRecord, w.TccRecordId, null, null,
                    [new CollectionAllocationDraft(CollectionSourceKind.PaymentRecord, w.TccRecordId, 900m, null)])]);
            ctx.Add(shadow);
            shadowId = shadow.Id;

            // Reverse the Market Fees CT.
            var market = await ctx.Collections.Include(x => x.Lines).ThenInclude(x => x.Allocations)
                .SingleAsync(x => x.Id != shadow.Id && x.TotalAmount == 30m);
            var line = market.Lines.Single();
            ctx.Add(CollectionCorrection.Record(w.Tenant.Id, market.Id, null, null, null, CollectionCorrectionType.Reversal, Today,
                DateTime.UtcNow, -30m, "integration reversal", "head", "Head",
                [new CollectionCorrectionLineDraft(line.Id, -30m,
                    line.Allocations.Select(a => new CollectionCorrectionAllocationDraft(a.Id, -30m)).ToList())]));
            await ctx.SaveChangesAsync();
        }

        await using var read = db.CreateContext(w.Tenant.Id);
        var feed = Feed(read, w);

        Assert.DoesNotContain(feed.Events, e => e.CollectionId == shadowId);
        Assert.Single(feed.Events, e => e.EventKey == $"PaymentRecord:{w.TccRecordId}");
        Assert.Equal(1000m, feed.LegacyAmount);

        var ct = Assert.Single(feed.Events, e => e.Authority == "Canonical" && e.Amount == 30m);
        Assert.Equal(("Reversed", 30m, -30m, 0m), (ct.Disposition, ct.Amount, ct.CorrectionEffect, ct.NetAmount));
        Assert.Equal(nameof(CollectionCorrectionType.Reversal), ct.Corrections.Single().CorrectionType);
        Assert.Equal((30m, -30m, 1000m), (feed.CanonicalAmount, feed.CorrectionEffect, feed.NetAmount));
    }

    [SkippableFact]
    public async Task ATenantSeesOnlyItsOwnActivity_AndOnlyOfficeStaffReadIt()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var a = await SeedAsync("A");
        var b = await SeedAsync("B");
        await PostMarketFeeAsync(a, Guid.NewGuid());
        await PostRentAsync(a, (Period, 250m));

        await using (var readB = db.CreateContext(b.Tenant.Id))
        {
            var feedB = Feed(readB, b);
            Assert.Equal(2, feedB.EventCount);
            Assert.All(feedB.Events, e => Assert.Equal("Legacy", e.Authority));
            Assert.Contains(feedB.Events, e => e.EventKey == $"PaymentRecord:{b.TccRecordId}");
            Assert.DoesNotContain(feedB.Events, e => e.EventKey == $"PaymentRecord:{a.TccRecordId}");
            Assert.Equal(0m, feedB.CanonicalAmount);

            // Reading tenant B's request context while claiming tenant A is refused.
            Assert.Equal(ResultStatus.Forbidden, Run(readB, b.Tenant.Id, b.HeadId, claimedTenant: a.Tenant.Id).Status);
        }

        await using var readA = db.CreateContext(a.Tenant.Id);
        // The reader is explicitly tenant-scoped even on an unfiltered context.
        await using (var unfiltered = db.CreateContext(Guid.Empty))
        {
            var direct = await new CollectionActivityReader(unfiltered).GetAsync(b.Tenant.Id, Period, Today);
            Assert.DoesNotContain(direct, e => e.EventKey == $"PaymentRecord:{a.TccRecordId}" || e.Authority == "Canonical");
        }
        Assert.Equal(4, Feed(readA, a).EventCount);
        Assert.Equal(ResultStatus.Forbidden, Run(readA, a.Tenant.Id, a.Collector.Id, role: "Collector").Status);
        Assert.Equal(ResultStatus.Invalid, Run(readA, a.Tenant.Id, a.HeadId, from: Today.AddDays(-40), to: Today).Status);
    }
}
