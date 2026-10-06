using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
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
using EEMOCantilanSDS.Infrastructure.Repositories.Revenue;
using EEMOCantilanSDS.Infrastructure.Repositories.Payments;
using EEMOCantilanSDS.Infrastructure.Fees;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MediatR;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorMobileMenu;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class CollectionSessionTests(PostgresFixture database)
{
    [SkippableFact]
    public async Task Linked_meat_vendor_three_kilograms_quotes_records_and_replays_with_effective_rate()
    {
        var w = await SeedAsync(section: MarketSection.MeatSection);
        await using var db = database.CreateContext(w.TenantId);
        await EnableRentAndWeighing(db, w);
        const decimal approvedRate = 22m;
        db.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMeatPerKilo, approvedRate, Today, w.TenantId));
        await db.SaveChangesAsync();
        var source = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId),
            new Clock(), new DiscoverySender(db, w), new NoMarketDays());
        var flow = Workflow(db, w, source);
        var discovery = (await flow.DiscoverAsync(w.PayorId)).Value!;
        Assert.Contains(discovery.Operations.Single(x => x.Kind == CollectionSessionItemKind.Weighing).Choices!,
            x => x.Identity.WeighingType == WeighingType.Meat && x.Rate == approvedRate);
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId, [new(Guid.NewGuid(),
            CollectionSessionItemKind.Weighing, 0m, Weighing: new(w.StallId, WeighingType.Meat, 3m))]);
        var quote = (await flow.QuoteAsync(intent)).Value!;
        Assert.True(quote.CanRecord);
        Assert.Equal(3m * approvedRate, quote.GrandTotal);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, Assert.Single(quote.Items).Instrument);
        var result = (await flow.RecordAsync(new(intent, quote.QuoteFingerprint))).Value!;
        Assert.Equal(CollectionSessionStatus.Recorded, result.Status);
        Assert.Equal(result.Collections[0].ReferenceCode,
            (await flow.RecordAsync(new(intent, quote.QuoteFingerprint))).Value!.Collections[0].ReferenceCode);
        Assert.Equal(1, await db.Collections.CountAsync());
        Assert.Empty(await db.ObligationPeriods.ToListAsync());
    }

    [SkippableFact]
    public async Task SpaceHolder_created_midmonth_is_immediately_listed_and_collectible_only_by_assigned_operation()
    {
        var w = await SeedAsync();
        await using var db = database.CreateContext(w.TenantId);
        var tenant = new Tenant(w.TenantId);
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        var month = new DateOnly(Today.Year, Today.Month, 1);
        db.RemoveRange(await db.CollectorFacilityAssignments.Where(x => x.CollectorId == w.CollectorId).ToListAsync());
        var start = Today.Day > 1 ? Today : month.AddMonths(-1).AddDays(14);
        var setup = new ObligationWorkflow(db, head, tenant, new Clock());
        foreach (var kind in new[] { ObligationKind.KanmanggaySpaceRental, ObligationKind.FiestaArawLotRental })
        {
            var classification = RevenueClassification.Create(ObligationAccount.ClassificationCodeFor(kind), w.TenantId);
            db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, start.AddYears(-1),
                "Space rental", RevenueInstrumentType.OfficialReceipt, w.TenantId));
        }
        await db.SaveChangesAsync();
        var created = await setup.CreateAccountAsync(new(ObligationKind.KanmanggaySpaceRental, w.PayorId, null,
            "", null, null, start, 175m, OccupancyArrangement.SpaceOnly));
        Assert.True(created.IsSuccess, created.Error);
        var holder = created.Value!;
        var listed = (await setup.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).Value!;
        Assert.Equal(175m, Assert.Single(listed).CurrentAmount);
        Assert.True(Assert.Single(listed).OutstandingToDate >= 175m);
        Assert.Equal(holder.Id, Assert.Single((await setup.GetWorkspaceAsync(ObligationKind.KanmanggaySpaceRental)).Value!.Accounts).Id);
        Assert.Empty(await db.ObligationPeriods.ToListAsync()); // browsing never assesses
        var collector = new Caller(w.CollectorId, w.TenantId);
        var sources = new CollectionSessionSources(db, collector, tenant, new Clock(), new DiscoverySender(db, w), new NoMarketDays());
        var flow = Workflow(db, w, sources);
        var period = new DateOnly(start.Year, start.Month, 1);
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId, [
            new(Guid.NewGuid(), CollectionSessionItemKind.Obligation, 75m,
                Obligation: new(holder.Id, period.Year, period.Month))]);
        Assert.False((await flow.QuoteAsync(intent)).Value!.CanRecord);
        db.Add(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.KanmanggaySpaceRental, "head"));
        await db.SaveChangesAsync();
        var choices = (await flow.DiscoverAsync(w.PayorId)).Value!.Operations
            .Single(x => x.OperationCode == CollectorOperationCodes.KanmanggaySpaceRental);
        Assert.Contains(choices.Choices!, x => x.Identity.AccountId == holder.Id);
        var otherPayor = Payor.Create(w.TenantId, "Different payer", BusinessPayorKind.Person, "test");
        db.Add(otherPayor);
        await db.SaveChangesAsync();
        Assert.DoesNotContain((await flow.DiscoverAsync(otherPayor.Id)).Value!.Operations.SelectMany(x => x.Choices ?? []),
            x => x.Identity.AccountId == holder.Id);
        var posted = await Record(flow, intent);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt, Assert.Single(posted.Collections).Instrument);
        Assert.StartsWith("SRC-", posted.Collections[0].ReferenceCode);
        var replay = (await flow.RecordAsync(new(intent, "unknown-response-retry"))).Value!;
        Assert.Equal(posted.Collections[0].CollectionId, replay.Collections[0].CollectionId);
        Assert.Equal(1, await db.ObligationPeriods.CountAsync());
        Assert.Equal(100m, (await new ObligationCollectionSource(db).GetQuotesAsync(w.TenantId,
            [await db.ObligationAccounts.SingleAsync(x => x.Id == holder.Id)], Today, default))
            .Single(x => x.PeriodStart == period).OutstandingAmount);
        var standalone = new CollectionComposerWorkflow(db, collector, tenant, new Clock());
        var final = await standalone.PostMobileObligationAsync(new(Guid.NewGuid(), holder.Id, period.Year, period.Month, 100m, Today));
        Assert.True(final.IsSuccess, final.Error);
        Assert.Equal(2, await db.Collections.CountAsync());
        Assert.Equal(1, await db.ObligationPeriods.CountAsync());
        var future = await setup.CreateAccountAsync(new(ObligationKind.KanmanggaySpaceRental, w.PayorId, null,
            "", null, null, Today.AddDays(1), 250m, OccupancyArrangement.SpaceOnly));
        Assert.True(future.IsSuccess, future.Error);
        Assert.Equal(0m, (await setup.GetAccountsAsync(ObligationKind.KanmanggaySpaceRental)).Value!.Single(x => x.Id == future.Value!.Id).OutstandingToDate);
    }

    [SkippableFact]
    public async Task Fiesta_event_collection_and_space_followup_keep_event_and_monthly_semantics()
    {
        var w = await SeedAsync();
        await using var db = database.CreateContext(w.TenantId);
        var tenant = new Tenant(w.TenantId);
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        var setup = new ObligationWorkflow(db, head, tenant, new Clock());
        foreach (var kind in new[] { ObligationKind.KanmanggaySpaceRental, ObligationKind.FiestaArawLotRental })
        {
            var classification = RevenueClassification.Create(ObligationAccount.ClassificationCodeFor(kind), w.TenantId);
            db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, Today.AddYears(-1), "Space", RevenueInstrumentType.OfficialReceipt, w.TenantId));
        }
        db.AddRange(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.FiestaArawLotRental, "head"),
            CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.KanmanggaySpaceRental, "head"));
        await db.SaveChangesAsync();
        var eventDate = Today.AddDays(-1);
        var lot = (await setup.CreateAccountAsync(new(ObligationKind.FiestaArawLotRental, w.PayorId, null,
            "", LotRentalEvent.Fiesta, eventDate, eventDate, 400m, OccupancyArrangement.SpaceOnly))).Value!;
        var monthly = (await setup.CreateAccountAsync(new(ObligationKind.KanmanggaySpaceRental, w.PayorId, null,
            "", null, null, new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1), 200m, OccupancyArrangement.SpaceOnly))).Value!;
        var reader = new SpaceFollowUpWorkflow(db, head, tenant);
        var unpaid = await reader.GetAsync(Today, null, default);
        Assert.Equal(2, unpaid.Count);
        Assert.Equal(eventDate, unpaid.Single(x => x.AccountId == lot.Id).PeriodStart);
        var collector = new Caller(w.CollectorId, w.TenantId);
        var flow = Workflow(db, w, new CollectionSessionSources(db, collector, tenant, new Clock(), new DiscoverySender(db, w), new NoMarketDays()));
        var eventChoice = Assert.Single((await flow.DiscoverAsync(w.PayorId)).Value!.Operations
            .Single(x => x.OperationCode == CollectorOperationCodes.FiestaArawLotRental).Choices!);
        Assert.Equal((lot.Id, eventDate, LotRentalEvent.Fiesta),
            (eventChoice.Identity.AccountId, eventChoice.Identity.PeriodStart, eventChoice.Identity.Event));
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId, [
            new(Guid.NewGuid(), CollectionSessionItemKind.Obligation, 400m, Obligation: new(lot.Id, eventDate.Year, eventDate.Month)),
            new(Guid.NewGuid(), CollectionSessionItemKind.Obligation, 50m, Obligation: new(monthly.Id, Today.AddMonths(-1).Year, Today.AddMonths(-1).Month))]);
        Assert.Equal(450m, (await Record(flow, intent)).GrandTotal);
        var partial = Assert.Single(await reader.GetAsync(Today, null, default));
        Assert.Equal((monthly.Id, 150m), (partial.AccountId, partial.Outstanding));
        Assert.Empty(await reader.GetAsync(Today, CollectorOperationCodes.FiestaArawLotRental, default));
        Assert.Empty(await new SpaceFollowUpWorkflow(db, new Caller(Guid.NewGuid(), Guid.NewGuid(), "Admin"), tenant).GetAsync(Today, null, default));
        Assert.Equal(2, await db.ObligationPeriods.CountAsync());
        Assert.Equal(2, await db.Collections.CountAsync());
        var remaining = await new CollectionComposerWorkflow(db, collector, tenant, new Clock()).PostMobileObligationAsync(
            new(Guid.NewGuid(), monthly.Id, Today.AddMonths(-1).Year, Today.AddMonths(-1).Month, 150m, Today));
        Assert.True(remaining.IsSuccess, remaining.Error);
        Assert.Empty(await reader.GetAsync(Today, null, default));
        Assert.Equal(2, await db.ObligationPeriods.CountAsync());
        var income = (await new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), head, tenant, new Clock())
            .Handle(new(Today.Year, Today.Month), default)).Value!;
        var reportRows = income.Groups.SelectMany(x => x.Rows).ToArray();
        Assert.Equal(200m, reportRows.Single(x => x.ClassificationCode == RevenueClassificationCodes.KanmanggaySpaceRental).Total.Canonical);
        Assert.Equal(400m, reportRows.Single(x => x.ClassificationCode == RevenueClassificationCodes.FiestaArawLotRental).Total.Canonical);
        var remit = (await new RemittanceWorkflow(db, head, tenant).GetReviewAsync(new(Today, Today))).Value!;
        Assert.Equal((600m, 3), (remit.Total, remit.CollectionCount));
    }

    [SkippableFact]
    public async Task Transportation_quick_amount_has_no_vehicle_class_and_uses_same_canonical_writer_in_session()
    {
        var w = await SeedAsync();
        await using var db = database.CreateContext(w.TenantId);
        var tenant = new Tenant(w.TenantId);
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        var classification = RevenueClassification.Create(RevenueClassificationCodes.TransportationParking, w.TenantId);
        db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, Today, "Transport", RevenueInstrumentType.CashTicket, w.TenantId),
            CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Transportation, "head"));
        await db.SaveChangesAsync();
        var setup = new GovernedServiceWorkflow(db, head, tenant, new Clock());
        Assert.True((await setup.ConfigureAsync(CollectorOperationCodes.Transportation,
            new(Today, GovernedServiceBasis.VehicleClassRate, null, null, true, true, QuickAmountEnabled: true))).IsSuccess);
        var collector = new Caller(w.CollectorId, w.TenantId);
        var service = new GovernedServiceWorkflow(db, collector, tenant, new Clock());
        var request = new GovernedServicePostRequest(1, Guid.NewGuid(), CollectorOperationCodes.Transportation,
            Today, 123m, GovernedServiceMode.QuickAmount, null, "Bulk received");
        var first = await service.PostMobileAsync(request);
        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(first.Value!.ReferenceCode, (await service.PostMobileAsync(request)).Value!.ReferenceCode);
        var flow = Workflow(db, w, new CollectionSessionSources(db, collector, tenant, new Clock(), new DiscoverySender(db, w), new NoMarketDays()));
        var choice = Assert.Single((await flow.DiscoverAsync(null)).Value!.Operations.Single(x => x.OperationCode == CollectorOperationCodes.Transportation).Choices!);
        Assert.Equal(GovernedServiceMode.QuickAmount, choice.Identity.Mode);
        Assert.Null(choice.Identity.VehicleClassCode);
        await Record(flow, new(Guid.NewGuid(), Today, null, [new(Guid.NewGuid(), CollectionSessionItemKind.GovernedService,
            45m, Service: new(CollectorOperationCodes.Transportation, GovernedServiceMode.QuickAmount))]));
        var current = (await setup.GetTransportationCurrentAsync(Today, Today)).Value!;
        Assert.Equal(168m, current.CollectedToday);
        Assert.All(current.Collections, x => { Assert.Null(x.VehicleClassCode); Assert.Equal(GovernedServiceMode.QuickAmount, x.Mode); });
        Assert.Empty(await db.TrmTrips.ToListAsync());
        Assert.Empty(await db.VehicleClasses.ToListAsync());
        Assert.Equal(2, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Transportation_DisablingCanonicalService_DoesNotReopenLegacyTripAuthority()
    {
        var w = await SeedAsync();
        await using var db = database.CreateContext(w.TenantId);
        var tenant = new Tenant(w.TenantId);
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        var classification = RevenueClassification.Create(RevenueClassificationCodes.TransportationParking, w.TenantId);
        db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, Today.AddDays(-1),
            "Transportation", RevenueInstrumentType.CashTicket, w.TenantId));
        await db.SaveChangesAsync();
        var setup = new GovernedServiceWorkflow(db, head, tenant, new Clock());
        Assert.True((await setup.ConfigureAsync(CollectorOperationCodes.Transportation,
            new(Today, GovernedServiceBasis.VehicleClassRate, null, null, true, true))).IsSuccess);
        Assert.True((await setup.ConfigureAsync(CollectorOperationCodes.Transportation,
            new(Today.AddDays(1), GovernedServiceBasis.VehicleClassRate, null, null, false, false))).IsSuccess);
        var authority = new TransportationCollectionAuthority(db, tenant);
        Assert.False(await authority.IsCanonicalAsync(Today.AddDays(-1)));
        Assert.True(await authority.IsCanonicalAsync(Today));
        Assert.True(await authority.IsCanonicalAsync(Today.AddDays(1)));
        Assert.True(await authority.IsCanonicalAsync(Today.AddDays(2)));
    }

    [SkippableFact]
    public async Task Transportation_CurrentTerminalRead_UsesStandaloneAndItemizedCollections_NotLegacyTrips()
    {
        var w = await SeedAsync();
        await using var db = database.CreateContext(w.TenantId);
        var tenant = new Tenant(w.TenantId);
        var collector = new Caller(w.CollectorId, w.TenantId);
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        var setup = new GovernedServiceWorkflow(db, head, tenant, new Clock());
        var classification = RevenueClassification.Create(RevenueClassificationCodes.TransportationParking, w.TenantId);
        db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, Today.AddDays(-1),
            "Transportation", RevenueInstrumentType.CashTicket, w.TenantId),
            CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Transportation, "test"));
        await db.SaveChangesAsync();
        Assert.True((await setup.ConfigureAsync(CollectorOperationCodes.Transportation,
            new(Today, GovernedServiceBasis.VehicleClassRate, null, null, true, true))).IsSuccess);
        var classes = new VehicleClassWorkflow(db, head, tenant, new Clock());
        var vehicle = (await classes.SaveAsync(new("JEEP", "Jeepney", Today, 20m))).Value!;
        var standalone = new GovernedServiceWorkflow(db, collector, tenant, new Clock());
        var request = new GovernedServicePostRequest(1, Guid.NewGuid(), CollectorOperationCodes.Transportation,
            Today, 20m, null, "Walk-in payer", "Dispatch A", VehicleClassCode: vehicle.Code);
        var first = await standalone.PostMobileAsync(request);
        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal(first.Value!.ReferenceCode, (await standalone.PostMobileAsync(request)).Value!.ReferenceCode);
        var sources = new CollectionSessionSources(db, collector, tenant, new Clock(), new DiscoverySender(db, w), new NoMarketDays());
        var flow = Workflow(db, w, sources);
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId, [
            new(Guid.NewGuid(), CollectionSessionItemKind.GovernedService, 20m,
                Service: new(CollectorOperationCodes.Transportation, Reference: "Dispatch B", VehicleClassCode: vehicle.Code))]);
        var posted = await Record(flow, intent);
        Assert.Equal(posted.Collections[0].ReferenceCode, (await Record(flow, intent)).Collections[0].ReferenceCode);
        Assert.Equal(2, await db.Collections.CountAsync());
        Assert.Empty(await db.TrmTrips.ToListAsync());
        Assert.Empty(await db.TrmTransporters.ToListAsync());

        var current = (await setup.GetTransportationCurrentAsync(Today, Today)).Value!;
        Assert.Equal((40m, 2, 1, 40m, 2), (current.CollectedToday, current.TransactionsToday,
            current.ActiveVehicleClassCount, current.CollectedInPeriod, current.TransactionsInPeriod));
        Assert.All(current.Collections, c =>
        {
            Assert.StartsWith("SRC-", c.ReferenceCode);
            Assert.Equal("JEEP", c.VehicleClassCode);
            Assert.Equal("Jeepney", c.VehicleClassName);
            Assert.Equal(20m, c.FrozenVehicleRate);
            Assert.NotNull(c.VehicleClassRateId);
            Assert.Equal(w.CollectorId, c.CollectorId);
            Assert.Equal("Session Collector", c.CollectorName);
            Assert.Equal(RevenueInstrumentType.CashTicket, c.Instrument);
            Assert.Equal(GovernedCollectionState.Posted, c.State);
        });
        Assert.Equal(new[] { "Jeepney · Dispatch A", "Jeepney · Dispatch B" }, current.Collections.Select(c => c.Reference).Order());
        Assert.Equal(JsonSerializer.Serialize(current.Collections), JsonSerializer.Serialize(
            (await setup.GetActivityAsync(CollectorOperationCodes.Transportation, Today, Today)).Value));
        Assert.Equal(2, (await new CollectionActivityReader(db).GetAsync(w.TenantId, Today, Today)).Count);
        Assert.Equal(2, (await new CollectionsReportWorkflow(db, collector, tenant).GetMyRegisterAsync(Today, Today)).Value!.Rows.Count);
        var collectorReport = await new CollectorReportQueries(db).GetCollectionsAsync(w.CollectorId, Today, Today);
        Assert.Equal(2, collectorReport.OperationCollections!.Count);
        Assert.Equal(40m, collectorReport.OperationCollections.Sum(c => c.Amount));
        Assert.All(collectorReport.OperationCollections, c => Assert.StartsWith("SRC-", c.DocumentNumber));
        var income = new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), head, tenant, new Clock());
        var before = (await income.Handle(new(Today.Year, Today.Month), default)).Value!;
        Assert.Equal(40m, before.Groups.SelectMany(g => g.Rows).Single(r => r.ClassificationCode == RevenueClassificationCodes.TransportationParking).Total.Total);
        var remittance = new RemittanceWorkflow(db, head, tenant);
        Assert.Equal(2, (await remittance.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!.Collections.Count);
        var remit = await remittance.RecordAsync(new(Guid.NewGuid(), w.CollectorId, Today, Today, Today, null,
            current.Collections.Select(c => c.CollectionId).ToArray(), 40m, null, null));
        Assert.True(remit.IsSuccess, remit.Error);
        Assert.Empty((await remittance.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!.Collections);
        Assert.True((await remittance.VoidAsync(remit.Value!.Row.Id, new("Test reversal of remittance"))).IsSuccess);
        Assert.Equal(2, (await remittance.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!.Collections.Count);
        Assert.Equal(40m, (await income.Handle(new(Today.Year, Today.Month), default)).Value!.GrandTotal.Total);
        Assert.Equal(2, await db.Collections.CountAsync());

        var legacyDate = new DateOnly(Today.Year, Today.Month, 1).AddDays(-1);
        var legacy = EEMOCantilanSDS.Domain.Entities.TransportTerminal.TrmTrip.Create(null, 7, "Historical driver", "OLD-PLATE",
            "Historical route", "OLD-OR", recordedAt: PhilippineTime.DayUtcRange(legacyDate).StartUtc, fee: 30m);
        db.TrmTrips.Add(legacy);
        await db.SaveChangesAsync();
        Assert.Equal(40m, (await setup.GetTransportationCurrentAsync(Today, Today)).Value!.CollectedToday);
        var history = await new TrmRepository(db, new Clock()).GetTripsByMonthAsync(legacyDate.Year, legacyDate.Month);
        Assert.Equal(legacy.Id, Assert.Single(history).Id);
        Assert.Equal("OLD-OR", history[0].ORNumber);
        Assert.Equal(0m, (await setup.GetTransportationCurrentAsync(legacyDate, legacyDate)).Value!.CollectedInPeriod);

        Assert.True((await classes.SaveAsync(new("JEEP", "Renamed Jeepney", Today.AddDays(1), 25m))).IsSuccess);
        var next = new GovernedServiceWorkflow(db, collector, tenant, new Clock(Today.AddDays(1)));
        Assert.True((await next.PostMobileAsync(request with { ClientOperationId = Guid.NewGuid(), BusinessDate = Today.AddDays(1), ReceivedAmount = 25m })).IsSuccess);
        var after = (await setup.GetTransportationCurrentAsync(Today, Today)).Value!;
        Assert.All(after.Collections, c => { Assert.Equal(20m, c.Amount); Assert.Equal("Jeepney", c.VehicleClassName); });
        var future = new GovernedServiceWorkflow(db, head, tenant, new Clock(Today.AddDays(1)));
        Assert.Equal(25m, Assert.Single((await future.GetTransportationCurrentAsync(Today.AddDays(1), Today.AddDays(1))).Value!.Collections).FrozenVehicleRate);
        Assert.Equal(2, Assert.Single((await classes.GetAsync()).Value!).History!.Count);
        Assert.Equal(ResultStatus.Conflict, (await standalone.PostMobileAsync(request with { ReceivedAmount = 25m })).Status);
        Assert.Equal(3, await db.Collections.CountAsync());
        Assert.Single(await db.TrmTrips.ToListAsync());
        var originalLine = await db.CollectionLines.SingleAsync(l => l.CollectionId == first.Value.CollectionId);
        db.CollectionCorrections.Add(CollectionCorrection.Record(w.TenantId, first.Value.CollectionId, null, null, null,
            CollectionCorrectionType.Reversal, Today, DateTime.UtcNow, -20m, "Test corrected collection",
            head.Id.ToString("N"), "Head", [new CollectionCorrectionLineDraft(originalLine.Id, -20m)]));
        await db.SaveChangesAsync();
        var corrected = (await setup.GetTransportationCurrentAsync(Today, Today)).Value!;
        Assert.Equal((20m, 1), (corrected.CollectedToday, corrected.TransactionsToday));
        var reversed = corrected.Collections.Single(c => c.CollectionId == first.Value.CollectionId);
        Assert.Equal((20m, 0m, GovernedCollectionState.Reversed), (reversed.Amount, reversed.NetAmount, reversed.State));
        Assert.Equal(3, await db.Collections.CountAsync());
        Assert.Equal(ResultStatus.Forbidden, (await standalone.GetTransportationCurrentAsync(Today, Today)).Status);
        var foreign = new GovernedServiceWorkflow(db, head with { TenantId = Guid.NewGuid() }, tenant, new Clock());
        Assert.Equal(ResultStatus.Forbidden, (await foreign.GetTransportationCurrentAsync(Today, Today)).Status);
    }

    [SkippableFact]
    public async Task LandingAndBerthing_AreStableFeeChoices_SharedByStandaloneAndSession_AndCountOnce()
    {
        var w = await SeedAsync();
        await using var db = database.CreateContext(w.TenantId);
        var tenant = new Tenant(w.TenantId);
        var collector = new Caller(w.CollectorId, w.TenantId);
        var head = new Caller(Guid.NewGuid(), w.TenantId, "SuperAdmin");
        var setup = new GovernedServiceWorkflow(db, head, tenant, new Clock());
        var configured = await setup.ConfigureAsync(CollectorOperationCodes.LandingBerthing,
            new(Today, GovernedServiceBasis.ApprovedFeeOption, null, null, true, true));
        Assert.True(configured.IsSuccess, configured.Error);
        async Task<Guid> Add(string name, string code, GovernedServiceBasis basis, decimal? amount)
        {
            var result = await setup.AddFeeOptionAsync(CollectorOperationCodes.LandingBerthing,
                new(name, code, null, null, Today, basis, amount, null));
            Assert.True(result.IsSuccess, result.Error);
            return result.Value!.Single(x => x.Code == code).Id;
        }
        var landing = await Add("Landing", "LANDING", GovernedServiceBasis.DirectApprovedAmount, null);
        var berthing = await Add("Berthing", "BERTHING", GovernedServiceBasis.FixedAmount, 80m); // approved test fixture only
        Assert.NotEqual(landing, berthing);
        Assert.False((await setup.AddFeeOptionAsync(CollectorOperationCodes.LandingBerthing,
            new("Unapproved", "UNAPPROVED", null, null, Today, GovernedServiceBasis.FixedAmount, null, null))).IsSuccess);
        var standalone = new GovernedServiceWorkflow(db, collector, tenant, new Clock());
        var terms = (await standalone.GetTermsAsync(CollectorOperationCodes.LandingBerthing, null)).Value!;
        Assert.Equal(2, terms.FeeOptions!.Count);
        Assert.Equal(RevenueInstrumentType.CashTicket, terms.Instrument);
        Assert.All(terms.FeeOptions, x => { Assert.NotNull(x.RateId); Assert.Equal(Today, x.EffectiveDate); });
        Assert.Equal("LANDING", terms.FeeOptions.Single(x => x.Id == landing).Code);
        var request = new GovernedServicePostRequest(1, Guid.NewGuid(), CollectorOperationCodes.LandingBerthing,
            Today, 50m, null, "Walk-in", null, FeeOptionId: landing);
        var first = await standalone.PostMobileAsync(request);
        Assert.True(first.IsSuccess, first.Error);
        Assert.StartsWith("SRC-", first.Value!.ReferenceCode);
        Assert.Equal(landing, first.Value.FeeOptionId);
        Assert.Equal(terms.FeeOptions.Single(x => x.Id == landing).RateId, first.Value.FeeOptionRateId);
        Assert.Equal(first.Value.CollectionId, (await standalone.PostMobileAsync(request)).Value!.CollectionId);
        Assert.Equal(first.Value.ReferenceCode, (await standalone.PostMobileAsync(request with
            { AccountableDocumentId = Guid.NewGuid(), DocumentNumber = "OBSOLETE-SERIAL" })).Value!.ReferenceCode);
        Assert.Equal(ResultStatus.Conflict, (await standalone.PostMobileAsync(request with { FeeOptionId = berthing })).Status);
        Assert.False((await standalone.PostMobileAsync(request with
            { ClientOperationId = Guid.NewGuid(), FeeOptionId = Guid.NewGuid() })).IsSuccess);
        var unassigned = new GovernedServiceWorkflow(db, new Caller(Guid.NewGuid(), w.TenantId), tenant, new Clock());
        Assert.False((await unassigned.PostMobileAsync(request with { ClientOperationId = Guid.NewGuid() })).IsSuccess);
        var sources = new CollectionSessionSources(db, collector, tenant, new Clock(), new DiscoverySender(db, w), new NoMarketDays());
        var discovery = (await Workflow(db, w, sources).DiscoverAsync(w.PayorId)).Value!;
        var operation = discovery.Operations.Single(o => o.OperationCode == CollectorOperationCodes.LandingBerthing);
        Assert.Equal(2, operation.EligibleChoiceCount);
        Assert.False(operation.CanAutoSelect);
        Assert.Equal(new[] { landing, berthing }.Order(), operation.Choices!.Select(c => c.Identity.FeeOptionId!.Value).Order());
        Assert.All(operation.Choices!, c => { Assert.NotNull(c.RateId); Assert.Equal(Today, c.RateEffectiveDate); });
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId, [
            new(Guid.NewGuid(), CollectionSessionItemKind.GovernedService, 80m,
                Service: new(CollectorOperationCodes.LandingBerthing, FeeOptionId: berthing)),
            new(Guid.NewGuid(), CollectionSessionItemKind.VendorFee, 100m, VendorFee: new(w.StallId))]);
        var flow = Workflow(db, w, sources);
        var posted = await Record(flow, intent);
        Assert.Equal(2, posted.Collections.Count);
        Assert.Equal(2, posted.Collections.Select(c => c.ReferenceCode).Distinct().Count());
        Assert.Equal(posted.Collections.Select(c => c.CollectionId), (await Record(flow, intent)).Collections.Select(c => c.CollectionId));
        Assert.Equal(3, await db.Collections.CountAsync());
        Assert.True((await setup.ScheduleFeeOptionRateAsync(CollectorOperationCodes.LandingBerthing, berthing,
            new(Today.AddDays(1), GovernedServiceBasis.FixedAmount, 120m, null))).IsSuccess);
        Assert.True((await setup.RetireFeeOptionAsync(CollectorOperationCodes.LandingBerthing, landing, new(Today))).IsSuccess);
        Assert.False((await standalone.PostMobileAsync(request with { ClientOperationId = Guid.NewGuid() })).IsSuccess);
        Assert.Equal(first.Value.ReferenceCode, (await standalone.PostMobileAsync(request)).Value!.ReferenceCode);
        Assert.Equal(2, (await setup.GetFeeOptionsAsync(CollectorOperationCodes.LandingBerthing)).Value!.Count);
        var active = (await setup.GetFeeOptionsAsync(CollectorOperationCodes.LandingBerthing, activeOnly: true)).Value!;
        Assert.Equal(berthing, Assert.Single(active).Id);
        Assert.True(active[0].CanCollect);
        Assert.Equal(FeeOptionAvailability.Active, active[0].Availability);
        var retired = (await setup.GetFeeOptionsAsync(CollectorOperationCodes.LandingBerthing)).Value!.Single(x => x.Id == landing);
        Assert.False(retired.CanCollect);
        Assert.Equal("FeeTypeRetired", retired.ReasonCode);
        Assert.Equal(first.Value.FeeOptionRateId, Assert.Single(retired.History).RateId);
        Assert.Single((await standalone.GetTermsAsync(CollectorOperationCodes.LandingBerthing, null)).Value!.FeeOptions!);
        var tomorrow = new GovernedServiceWorkflow(db, collector, tenant, new Clock(Today.AddDays(1)));
        Assert.Equal(120m, Assert.Single((await tomorrow.GetTermsAsync(CollectorOperationCodes.LandingBerthing, null)).Value!.FeeOptions!).Amount);
        var activity = (await setup.GetActivityAsync(CollectorOperationCodes.LandingBerthing, Today, Today)).Value!;
        Assert.Equal(new[] { "Berthing", "Landing" }, activity.Select(a => a.FeeOptionName).Order());
        Assert.Equal(130m, activity.Sum(a => a.Amount));
        Assert.Equal(130m, (await setup.GetFeeOptionTotalsAsync(CollectorOperationCodes.LandingBerthing, Today, Today)).Value!.Sum(x => x.Amount));
        Assert.Equal(3, (await new CollectionActivityReader(db).GetAsync(w.TenantId, Today, Today)).Count);
        Assert.Equal(3, (await new CollectionsReportWorkflow(db, collector, tenant).GetMyRegisterAsync(Today, Today)).Value!.Rows.Count);
        var income = new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), head, tenant, new Clock());
        var before = (await income.Handle(new(Today.Year, Today.Month), default)).Value!;
        Assert.Equal(130m, before.Groups.SelectMany(g => g.Rows).Single(r => r.ClassificationCode == RevenueClassificationCodes.LandingBerthing).Total.Total);
        Assert.Equal(230m, before.GrandTotal.Total);
        var remittance = new RemittanceWorkflow(db, head, tenant);
        var remit = await remittance.RecordAsync(new(Guid.NewGuid(), w.CollectorId, Today, Today, Today, null,
            (await db.Collections.Select(c => c.Id).ToListAsync()), 230m, null, null));
        Assert.True(remit.IsSuccess, remit.Error);
        Assert.True((await remittance.VoidAsync(remit.Value!.Row.Id, new("Test void"))).IsSuccess);
        Assert.Equal(3, (await remittance.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!.Collections.Count);
        Assert.Equal(JsonSerializer.Serialize(before.Groups), JsonSerializer.Serialize((await income.Handle(new(Today.Year, Today.Month), default)).Value!.Groups));
        Assert.Equal(3, await db.Collections.CountAsync());
        var changedRate = await tomorrow.PostMobileAsync(request with
            { ClientOperationId = Guid.NewGuid(), BusinessDate = Today.AddDays(1), FeeOptionId = berthing, ReceivedAmount = 120m });
        Assert.True(changedRate.IsSuccess, changedRate.Error);
        Assert.Equal(120m, changedRate.Value!.Amount);
        Assert.NotEqual(first.Value.FeeOptionRateId, changedRate.Value.FeeOptionRateId);
        var originalBerthingId = posted.Collections.Single(c => c.Amount == 80m).CollectionId;
        Assert.Equal(80m, (await db.Collections.SingleAsync(c => c.Id == originalBerthingId)).TotalAmount);
        Assert.True((await setup.ConfigureAsync(CollectorOperationCodes.LandingBerthing,
            new(Today.AddDays(2), GovernedServiceBasis.ApprovedFeeOption, null, null, false, false))).IsSuccess);
        var disabled = new GovernedServiceWorkflow(db, collector, tenant, new Clock(Today.AddDays(2)));
        Assert.False((await disabled.PostMobileAsync(request with
            { ClientOperationId = Guid.NewGuid(), BusinessDate = Today.AddDays(2), FeeOptionId = berthing, ReceivedAmount = 120m })).IsSuccess);
        var foreign = new GovernedServiceWorkflow(db, new Caller(w.CollectorId, Guid.NewGuid()), tenant, new Clock());
        Assert.Equal(ResultStatus.Forbidden, (await foreign.PostMobileAsync(request with { ClientOperationId = Guid.NewGuid() })).Status);
        Assert.Equal(4, await db.Collections.CountAsync());
    }

    private static readonly DateOnly Today = PhilippineTime.Today;
    private sealed record Tenant(Guid MunicipalityId) : ICurrentMunicipalityAccessor { public void Set(Guid id) { } }
    private sealed class NoMarketDays : ITpmMarketDayProvider
    {
        public Task<DayOfWeek> GetMarketDayAsync(DateOnly asOf, CancellationToken ct = default) => throw new InvalidOperationException("Tabo is not an itemized capability.");
        public Task<IReadOnlyList<DateOnly>> GetMarketDatesAsync(int year, int month, CancellationToken ct = default) => throw new InvalidOperationException("Tabo is not an itemized capability.");
    }
    private sealed class Clock(DateOnly? businessDate = null) : IClock
    {
        public DateTime UtcNow => businessDate?.ToDateTime(new TimeOnly(4, 0), DateTimeKind.Utc) ?? DateTime.UtcNow;
        public DateTime PhilippineNow => businessDate?.ToDateTime(new TimeOnly(12, 0)) ?? PhilippineTime.Now;
        public DateOnly PhilippineToday => businessDate ?? Today;
    }
    private sealed record Caller(Guid Id, Guid TenantId, string Role = "Collector") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Id;
        public Guid? CollectorId => Role == "Collector" ? Id : null;
        public Guid? MunicipalityId => TenantId;
        public string? Username => "session-test";
        public string? MunicipalityCode => "session-test";
        public AdminUserDto? GetCurrentUser() => null;
    }
    private sealed record World(Guid TenantId, Guid CollectorId, Guid PayorId, Guid AccountId, Guid StallId);
    private async Task<World> SeedAsync(DateOnly? accountStart = null, bool linked = true, MarketSection section = MarketSection.FishSection)
    {
        Skip.IfNot(database.Available, database.UnavailableReason ?? "");
        await database.ResetAsync();
        var tenant = Municipality.Create("SESSION-" + Guid.NewGuid().ToString("N")[..8], "Session test", "Province", MunicipalityStatus.Active,
            tenantCode: "session-" + Guid.NewGuid().ToString("N")[..8]);
        await using (var setup = database.CreateContext(Guid.Empty)) { setup.Add(tenant); await setup.SaveChangesAsync(); }
        await using var db = database.CreateContext(tenant.Id);
        var collector = CollectorUser.Create("Session Collector", "C-1", "session-" + Guid.NewGuid().ToString("N")[..8],
            null, null, new HashedPassword("test"), tenant.Id);
        var facility = Facility.Create(FacilityCode.NPM, "Market", "NPM", archetype: BillingArchetype.DailyStall, municipalityId: tenant.Id);
        var stall = Stall.Create(facility.Id, "FISH-1", 900m, ApplicableFees.BaseRental | ApplicableFees.Water | ApplicableFees.Electricity,
            section, createdBy: "test", municipalityId: tenant.Id);
        var payor = Payor.Create(tenant.Id, "One Payer", BusinessPayorKind.Person, "test");
        var contract = Contract.Create(stall.Id, "One Payer", "One Payer", (accountStart ?? Today).AddYears(-1), 20, 900m, createdBy: "test");
        if (linked) contract.AssociatePayor(payor.Id, "test");
        collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, facility.Id, FacilityCode.NPM));
        db.AddRange(collector, facility, stall, payor, contract);
        foreach (var (code, classificationCode, fixedAmount) in new[]
        {
            (CollectorOperationCodes.MarketFees, RevenueClassificationCodes.MarketFees, 30m),
            (CollectorOperationCodes.LandingBerthing, RevenueClassificationCodes.LandingBerthing, 200m)
        })
        {
            var classification = RevenueClassification.Create(classificationCode, tenant.Id);
            var service = GovernedService.Create(tenant.Id, code, "head");
            db.AddRange(classification, RevenueClassificationPolicy.Create(classification.Id, new(2000, 1, 1), code, RevenueInstrumentType.CashTicket, tenant.Id),
                service, GovernedServiceSetting.Create(tenant.Id, service.Id, Today.AddDays(-1), GovernedServiceBasis.FixedAmount, fixedAmount, null, true, true, "head"),
                CollectorOperationAssignment.Assign(tenant.Id, collector.Id, code, "head"));
        }
        var fish = RevenueClassification.Create(RevenueClassificationCodes.FishMeatVendorFee, tenant.Id);
        var water = RevenueClassification.Create(RevenueClassificationCodes.Wcf, tenant.Id);
        var electricity = RevenueClassification.Create(RevenueClassificationCodes.Ecf, tenant.Id);
        var startDate = accountStart ?? Today;
        var start = new DateOnly(startDate.Year, startDate.Month, 1);
        var account = ObligationAccount.Create(tenant.Id, ObligationKind.FishMeatVendorFee, payor.Id, stall.Id, "Fish vendor", null, null, start, "head");
        db.AddRange(fish, water, electricity,
            RevenueClassificationPolicy.Create(fish.Id, new(2000, 1, 1), "Vendor Fee", RevenueInstrumentType.OfficialReceipt, tenant.Id),
            RevenueClassificationPolicy.Create(water.Id, new(2000, 1, 1), "Water", RevenueInstrumentType.CashTicket, tenant.Id),
            RevenueClassificationPolicy.Create(electricity.Id, new(2000, 1, 1), "Electricity", RevenueInstrumentType.OfficialReceipt, tenant.Id),
            account, ObligationRate.Create(tenant.Id, account.Id, start, 900m, "head"));
        await db.SaveChangesAsync();
        return new(tenant.Id, collector.Id, payor.Id, account.Id, stall.Id);
    }
    private static CollectionSessionIntent Basket(World w, Guid? payor = null) => new(Guid.NewGuid(), Today, payor ?? w.PayorId,
    [
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"), CollectionSessionItemKind.GovernedService, 30m, Service: new(CollectorOperationCodes.MarketFees)),
        new(Guid.Parse("00000000-0000-0000-0000-000000000002"), CollectionSessionItemKind.GovernedService, 200m, Service: new(CollectorOperationCodes.LandingBerthing)),
        new(Guid.Parse("00000000-0000-0000-0000-000000000003"), CollectionSessionItemKind.VendorFee, 900m,
            VendorFee: new(w.StallId))
    ]);

    [SkippableFact]
    public async Task Explicit_occupancy_link_enables_vendor_fee_and_itemized_discovery_without_name_matching()
    {
        var w = await SeedAsync(linked: false);
        await using var db = database.CreateContext(w.TenantId);
        var collector = new Caller(w.CollectorId, w.TenantId);
        var vendor = new FishMeatVendorFeeCollectionWorkflow(db, collector, new Tenant(w.TenantId), new Clock());
        var unlinked = Assert.Single((await vendor.DiscoverAsync()).Value!);
        Assert.False(unlinked.CanCollect);
        Assert.Equal(VendorFeeSourceStatus.NeedsPayor, unlinked.Status);
        Assert.Equal(VendorFeeSourceAction.LinkBusinessPayor, unlinked.RequiredAction);
        Assert.Equal("RequiresPayorLink", unlinked.ReasonCode);
        Assert.Null((await db.Contracts.AsNoTracking().SingleAsync()).PayorId);
        var sources = new CollectionSessionSources(db, collector, new Tenant(w.TenantId), new Clock(), new DiscoverySender(db, w), new NoMarketDays());
        var before = await sources.DiscoverAsync(w.PayorId, Today, default);
        Assert.Empty(before.VendorFeeSources!);
        Assert.Equal("NoEligibleSource", before.Operations.Single(x => x.Kind == CollectionSessionItemKind.VendorFee).ReasonCode);
        var office = new BusinessPayorWorkflow(db, new Caller(Guid.NewGuid(), w.TenantId, "Admin"), new Tenant(w.TenantId));
        Assert.True((await office.LinkAsync(new(unlinked.OccupancyId!.Value, w.PayorId))).IsSuccess);
        var payor = Assert.Single(await new CollectionSessionStore(db).SearchPayorsAsync(w.TenantId, "One Payer", default));
        Assert.Contains("NPM · FISH-1", payor.Contexts!);
        var after = await sources.DiscoverAsync(w.PayorId, Today, default);
        Assert.True(Assert.Single(after.VendorFeeSources!).CanCollect);
        Assert.True(after.Operations.Single(x => x.Kind == CollectionSessionItemKind.VendorFee).CanAdd);
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId,
            [new(Guid.NewGuid(), CollectionSessionItemKind.VendorFee, 100m, VendorFee: new(w.StallId))]);
        var outcome = await Record(Workflow(db, w, sources), intent);
        Assert.Equal(100m, Assert.Single(outcome.Collections).Amount);
        Assert.Empty(await db.DailyCollections.ToListAsync());
        Assert.Empty(await db.ObligationPeriods.ToListAsync());
    }
    private static CollectionSessionWorkflow Workflow(AppDbContext db, World w, ICollectionSessionSources? sources = null, Caller? actor = null) =>
        new(new CollectionSessionStore(db), sources ?? new CollectionSessionSources(db, actor ?? new(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), null!, new NoMarketDays()),
            actor ?? new(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), NullLogger<CollectionSessionWorkflow>.Instance);
    private static async Task<CollectionSessionResult> Record(CollectionSessionWorkflow flow, CollectionSessionIntent intent)
    {
        var quote = (await flow.QuoteAsync(intent)).Value!;
        Assert.True(quote.CanRecord, string.Join(";", quote.Problems.Select(p => p.Message)));
        var result = await flow.RecordAsync(new(intent, quote.QuoteFingerprint));
        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.Status == CollectionSessionStatus.Recorded, string.Join(";", result.Value.Problems.Select(x => x.Code + ": " + x.Message)));
        Assert.Empty(result.Value.Problems);
        return result.Value!;
    }

    private sealed class Code : ITenantContext { public string TenantCode => "session-test"; }
    private sealed class NoCache : IEemoCacheInvalidator
    {
        public Task InvalidateRegionAsync(string region, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidatePeriodAsync(string tenantCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateFacilityPeriodAsync(string tenantCode, FacilityCode facilityCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidatePaymentAffectedViewsAsync(string tenantCode, FacilityCode? facilityCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateReferenceDataAsync(string tenantCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateTenantAsync(string tenantCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private static NpmWholePaymentWorkflow Whole(AppDbContext db, World w)
    {
        var caller = new Caller(w.CollectorId, w.TenantId); var tenant = new Tenant(w.TenantId);
        return new(new StallRepository(db), new CollectorRepository(db), new DailyCollectionRepository(db),
            new NpmMonthSettlementService(new DailyCollectionRepository(db), new NpmMarketClosureRepository(db), new FeeRateResolver(db), new Clock()),
            new NpmDailyCanonicalPoster(db, caller, tenant, new GovernedCanonicalAuthority(db, tenant)), caller, new Clock(), new NoCache(), new Code());
    }
    private static async Task EnableRentAndWeighing(AppDbContext db, World w)
    {
        var rent = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, w.TenantId);
        var weighing = RevenueClassification.Create(RevenueClassificationCodes.WeightAndMeasure, w.TenantId);
        var service = GovernedService.Create(w.TenantId, CollectorOperationCodes.NpmDaily, "head");
        db.AddRange(rent, weighing, service,
            RevenueClassificationPolicy.Create(rent.Id, Today, "Rent", RevenueInstrumentType.OfficialReceipt, w.TenantId),
            RevenueClassificationPolicy.Create(weighing.Id, Today, "Weight & Measure", RevenueInstrumentType.OfficialReceipt, w.TenantId),
            GovernedServiceSetting.Create(w.TenantId, service.Id, Today, GovernedServiceBasis.DirectApprovedAmount, null, null, true, true, "head"),
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, 30m, Today.AddYears(-2), w.TenantId),
            FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, 3m, Today, w.TenantId));
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task Direct_vendor_fee_is_append_only_replays_and_never_creates_an_assessment_or_changes_rent()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        await EnableRentAndWeighing(db, w);
        var whole = Whole(db, w);
        Assert.Equal(900m, (await whole.QuoteAsync(w.StallId, Today.Year, Today.Month)).Value!.Amount);
        var vendor = new FishMeatVendorFeeCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock());
        var request = new DirectVendorFeeRequest(Guid.NewGuid(), Today, w.StallId, w.PayorId, 100m);
        var first = (await vendor.PostAsync(request)).Value!;
        Assert.StartsWith("SRC-", first.ReferenceCode);
        Assert.Equal(first.CollectionId, (await vendor.PostAsync(request)).Value!.CollectionId);
        Assert.Equal(ResultStatus.Conflict, (await vendor.PostAsync(request with { AmountReceived = 101m })).Status);
        Assert.True((await vendor.PostAsync(request with { ClientOperationId = Guid.NewGuid(), AmountReceived = 50m })).IsSuccess);
        Assert.Equal(150m, await db.Collections.SumAsync(x => x.TotalAmount));
        Assert.Equal(2, await db.Collections.CountAsync()); Assert.Empty(await db.ObligationPeriods.ToListAsync());
        Assert.Empty(await db.DailyCollections.ToListAsync());
        Assert.Equal(900m, (await whole.QuoteAsync(w.StallId, Today.Year, Today.Month)).Value!.Amount);
        Assert.All(await db.CollectionLines.ToListAsync(), x => Assert.Equal(CollectionSourceKind.FishMeatVendorFee, x.SourceKind));
    }

    [SkippableFact]
    public async Task Direct_vendor_fee_preserves_pre_cutover_obligation_money_without_allocating_to_it()
    {
        var historicalDate = FishMeatVendorFeeRules.DirectEffectiveDate.AddDays(-1);
        var w = await SeedAsync(historicalDate); await using var db = database.CreateContext(w.TenantId);
        var historical = new CollectionComposerWorkflow(db, new Caller(w.CollectorId, w.TenantId),
            new Tenant(w.TenantId), new Clock(historicalDate));
        var oldRequest = new MobileObligationPostRequest(Guid.NewGuid(), w.AccountId,
            historicalDate.Year, historicalDate.Month, 50m, historicalDate);
        var posted = await historical.PostMobileObligationAsync(oldRequest);
        Assert.True(posted.IsSuccess, posted.Error);
        var oldCollection = Assert.Single(await db.Collections.AsNoTracking().ToListAsync());
        var period = Assert.Single(await db.ObligationPeriods.AsNoTracking().ToListAsync());
        var allocations = await db.CollectionAllocations.CountAsync();
        var direct = new FishMeatVendorFeeCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId),
            new Tenant(w.TenantId), new Clock());
        Assert.True((await direct.PostAsync(new(Guid.NewGuid(), Today, w.StallId, w.PayorId, 100m))).IsSuccess);
        Assert.Equal(2, await db.Collections.CountAsync());
        Assert.Equal(150m, await db.Collections.SumAsync(x => x.TotalAmount));
        Assert.Equal(allocations, await db.CollectionAllocations.CountAsync());
        Assert.Equal(period.SettlementVersion, (await db.ObligationPeriods.AsNoTracking().SingleAsync()).SettlementVersion);
        Assert.Equal(oldCollection.ReferenceCode, (await db.Collections.AsNoTracking().SingleAsync(x => x.Id == oldCollection.Id)).ReferenceCode);
        var accounts = await db.ObligationAccounts.AsNoTracking().ToListAsync();
        var quotes = await new ObligationCollectionSource(db).GetQuotesAsync(w.TenantId, accounts, Today, default);
        Assert.All(quotes, x => Assert.False(x.CanAddToDraft));
        Assert.Single(await db.ObligationPeriods.ToListAsync());
        Assert.Empty(await db.DailyCollections.ToListAsync());
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Itemized_six_sources_keep_independent_classifications_and_atomic_retries(bool injectFailure)
    {
        var w = await SeedAsync();
        var intent = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId,
            [new(Guid.Parse("00000000-0000-0000-0000-000000000001"), CollectionSessionItemKind.NpmWholePayment, 0m, NpmWhole: new(w.StallId, Today.Year, Today.Month)),
             new(Guid.Parse("00000000-0000-0000-0000-000000000002"), CollectionSessionItemKind.VendorFee, 100m, VendorFee: new(w.StallId)),
             new(Guid.Parse("00000000-0000-0000-0000-000000000003"), CollectionSessionItemKind.Weighing, 0m, Weighing: new(w.StallId, WeighingType.Fish, 22m)),
             new(Guid.Parse("00000000-0000-0000-0000-000000000004"), CollectionSessionItemKind.Water, 20m, Water: new(w.StallId, Today.Year, Today.Month)),
             new(Guid.Parse("00000000-0000-0000-0000-000000000005"), CollectionSessionItemKind.Electricity, 44m, Electricity: new(Guid.Empty, 0, w.StallId, Today.Year, Today.Month)),
             new(Guid.Parse("00000000-0000-0000-0000-000000000006"), CollectionSessionItemKind.GovernedService, 200m, Service: new(CollectorOperationCodes.LandingBerthing))]);
        await using (var setup = database.CreateContext(w.TenantId))
        {
            await EnableRentAndWeighing(setup, w);
            setup.AddRange(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "head"),
                CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, Today, Guid.NewGuid(), "head", DateTime.UtcNow));
            await setup.SaveChangesAsync();
        }
        if (injectFailure)
            intent = intent with { Items = intent.Items.Select(x => x.Kind switch
            {
                CollectionSessionItemKind.Water => x with { ClientItemId = Guid.Parse("00000000-0000-0000-0000-000000000005") },
                CollectionSessionItemKind.Electricity => x with { ClientItemId = Guid.Parse("00000000-0000-0000-0000-000000000004") },
                _ => x
            }).ToArray() };
        if (injectFailure)
        {
            await using var failing = database.CreateContext(w.TenantId);
            var sources = new CollectionSessionSources(failing, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), null!, new NoMarketDays(), Whole(failing, w));
            var flow = Workflow(failing, w, new FailAfterFirst(sources));
            var quote = (await flow.QuoteAsync(intent)).Value!;
            Assert.True(quote.CanRecord, string.Join(";", quote.Problems.Select(x => x.Message)));
            await Assert.ThrowsAsync<IOException>(() => flow.RecordAsync(new(intent, quote.QuoteFingerprint)));
        }
        await using var db = database.CreateContext(w.TenantId);
        Assert.Equal(0, await db.Collections.CountAsync()); Assert.Equal(0, await db.DailyCollections.CountAsync());
        var whole = Whole(db, w);
        var source = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), null!, new NoMarketDays(), whole);
        var workflow = Workflow(db, w, source);
        var result = await Record(workflow, intent);
        Assert.Equal(1330m, result.GrandTotal); Assert.Equal(6, result.Collections.Count);
        Assert.Equal(6, result.Collections.Select(x => x.ReferenceCode).Distinct().Count());
        Assert.Equal(220m, result.Collections.Where(x => x.Instrument == RevenueInstrumentType.CashTicket).Sum(x => x.Amount));
        Assert.Equal(0m, (await whole.QuoteAsync(w.StallId, Today.Year, Today.Month)).Value!.Amount);
        Assert.Empty(await db.ObligationPeriods.ToListAsync());
        var replay = (await workflow.RecordAsync(new(intent, "already recorded"))).Value!;
        Assert.Equal(result.Collections.Select(x => x.CollectionId), replay.Collections.Select(x => x.CollectionId));
        Assert.Equal(6, await db.Collections.CountAsync());
        Assert.Contains((await workflow.RecordAsync(new(intent with { Items = intent.Items.Skip(1).ToArray() }, "old"))).Value!.Problems,
            x => x.Code == "SessionIntentConflict");
        var income = new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), new Caller(Guid.NewGuid(), w.TenantId, "Admin"), new Tenant(w.TenantId), new Clock());
        var before = (await income.Handle(new(Today.Year, Today.Month), default)).Value!;
        var rows = before.Groups.SelectMany(x => x.Rows).ToArray();
        Assert.Equal(900m, rows.Single(x => x.Key == "RENT_NPM").Total.Total);
        Assert.Equal(100m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.FishMeatVendorFee).Total.Total);
        Assert.Equal(66m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.WeightAndMeasure).Total.Total);
        Assert.Equal(20m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.Wcf).Total.Total);
        Assert.Equal(44m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.Ecf).Total.Total);
        Assert.Equal(200m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.LandingBerthing).Total.Total);
        var register = (await new CollectionsReportWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId)).GetMyRegisterAsync(Today, Today)).Value!;
        Assert.Equal(1330m, register.Net); Assert.Equal(6, register.Rows.Count);
        Assert.Equal(6, (await new CollectionActivityReader(db).GetAsync(w.TenantId, Today, Today)).Count);
        var report = await new CollectorReportQueries(db).GetCollectionsAsync(w.CollectorId, Today, Today);
        Assert.Equal(1330m, report.Lines.Sum(x => x.Amount) + (report.OperationCollections?.Sum(x => x.Amount) ?? 0));
        var remittance = new RemittanceWorkflow(db, new Caller(Guid.NewGuid(), w.TenantId, "Admin"), new Tenant(w.TenantId));
        Assert.Equal(6, (await remittance.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!.Collections.Count);
        var remitted = await remittance.RecordAsync(new(Guid.NewGuid(), w.CollectorId, Today, Today, Today, null,
            result.Collections.Select(x => x.CollectionId).ToArray(), 1330m, null, null));
        Assert.True(remitted.IsSuccess);
        var countBeforeVoid = await db.Collections.CountAsync();
        Assert.True((await remittance.VoidAsync(remitted.Value!.Row.Id, new("Test correction"))).IsSuccess);
        Assert.Equal(6, (await remittance.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!.Collections.Count);
        Assert.Equal(countBeforeVoid, await db.Collections.CountAsync());
        Assert.Equal(result.Collections.Select(x => x.ReferenceCode).OrderBy(x => x), (await db.Collections.Select(x => x.ReferenceCode).ToListAsync()).OrderBy(x => x));
        Assert.Equal(JsonSerializer.Serialize(before.Groups), JsonSerializer.Serialize((await income.Handle(new(Today.Year, Today.Month), default)).Value!.Groups));
    }

    [SkippableFact]
    public async Task Mixed_instruments_keep_three_source_boundaries_and_reporting_and_remittance_count_each_once()
    {
        var w = await SeedAsync();
        await using var db = database.CreateContext(w.TenantId);
        var flow = Workflow(db, w); var intent = Basket(w);
        var quote = (await flow.QuoteAsync(intent)).Value!;
        Assert.Equal(1130m, quote.GrandTotal);
        Assert.Equal(3, quote.Items.Select(x => x.GroupId).Distinct().Count());
        Assert.Equal(230m, quote.InstrumentTotals.Single(x => x.Instrument == RevenueInstrumentType.CashTicket).Amount);
        Assert.Equal(900m, quote.InstrumentTotals.Single(x => x.Instrument == RevenueInstrumentType.OfficialReceipt).Amount);
        Assert.Equal(0, await db.Collections.CountAsync()); Assert.Equal(0, await db.ObligationPeriods.CountAsync());
        var posted = await Record(flow, intent);
        Assert.Equal(CollectionSessionStatus.Recorded, posted.Status);
        Assert.Equal(3, posted.Collections.Count); Assert.Equal(3, posted.Collections.Select(x => x.ReferenceCode).Distinct().Count());
        Assert.All(posted.Collections, c => Assert.Matches("^SRC-", c.ReferenceCode));
        Assert.Equal(3, await db.CollectionLines.CountAsync()); Assert.Equal(3, await db.PostingOperations.CountAsync());
        Assert.Single(await db.MobileCollectionSessions.ToListAsync());
        var replay = (await flow.RecordAsync(new(intent with { Items = intent.Items.Reverse().ToArray() }, quote.QuoteFingerprint))).Value!;
        Assert.True(replay.ExistingOutcome); Assert.Equal(posted.Collections.Select(c => c.CollectionId), replay.Collections.Select(c => c.CollectionId));
        Assert.Equal(posted.Collections.Select(c => c.ReferenceCode), replay.Collections.Select(c => c.ReferenceCode));
        Assert.Equal(3, await db.Collections.CountAsync()); Assert.Equal(3, await db.CollectionLines.CountAsync());

        var collector = new Caller(w.CollectorId, w.TenantId); var head = new Caller(Guid.NewGuid(), w.TenantId, "Admin");
        var tenant = new Tenant(w.TenantId);
        var activity = await new CollectionActivityReader(db).GetAsync(w.TenantId, Today, Today);
        Assert.Equal(3, activity.Count); Assert.Equal(1130m, activity.Sum(x => x.Amount));
        var register = (await new CollectionsReportWorkflow(db, collector, tenant).GetMyRegisterAsync(Today, Today)).Value!;
        Assert.Equal(3, register.Rows.Count); Assert.Equal(1130m, register.Rows.Sum(x => x.Amount));
        var report = await new CollectorReportQueries(db).GetCollectionsAsync(w.CollectorId, Today, Today);
        Assert.Equal(1130m, report.Lines.Sum(x => x.Amount) + (report.OperationCollections?.Sum(x => x.Amount) ?? 0));
        var records = (await new GovernedServiceWorkflow(db, collector, tenant, new Clock()).GetCollectorRecordsAsync(Today, Today)).Value!;
        Assert.Equal(2, records.Count); Assert.Equal(230m, records.Sum(x => x.Amount));
        var income = new GetOfficialMonthlyIncomeQueryHandler(db, new LegacyMonthlyIncomeReader(db), head, tenant, new Clock());
        var before = (await income.Handle(new(Today.Year, Today.Month), default)).Value!;
        Assert.Equal(1130m, before.GrandTotal.Total);
        var rows = before.Groups.SelectMany(x => x.Rows).ToList();
        Assert.Equal(30m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.MarketFees).Total.Total);
        Assert.Equal(200m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.LandingBerthing).Total.Total);
        Assert.Equal(900m, rows.Single(x => x.ClassificationCode == RevenueClassificationCodes.FishMeatVendorFee).Total.Total);
        var remit = new RemittanceWorkflow(db, head, tenant);
        var scope = (await remit.GetScopeAsync(w.CollectorId, Today, Today, null)).Value!;
        Assert.Equal(3, scope.Collections.Count);
        var recorded = await remit.RecordAsync(new(Guid.NewGuid(), w.CollectorId, Today, Today, Today, null,
            posted.Collections.Select(x => x.CollectionId).ToArray(), 1130m, null, null));
        Assert.True(recorded.IsSuccess, recorded.Error);
        var position = Assert.Single((await remit.GetPositionAsync(Today, Today)).Value!.Collectors);
        Assert.Equal((1130m, 1130m, 0m), (position.Collected, position.Remitted, position.Unremitted));
        var after = (await income.Handle(new(Today.Year, Today.Month), default)).Value!;
        Assert.Equal(JsonSerializer.Serialize(before.Groups), JsonSerializer.Serialize(after.Groups));
        Assert.Equal(3, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Payer_mismatch_and_optional_payer_and_changed_intent_are_enforced_before_money()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var flow = Workflow(db, w);
        var other = Payor.Create(w.TenantId, "One Payer", BusinessPayorKind.Person, "test"); db.Add(other); await db.SaveChangesAsync();
        var invalid = (await flow.QuoteAsync(Basket(w, other.Id))).Value!;
        Assert.Contains(invalid.Problems, p => p.Code == "PayerMismatch"); Assert.Equal(0, await db.Collections.CountAsync());
        var basket = Basket(w); var anonymous = basket with { PayorId = null, Items = basket.Items.Take(2).ToArray() };
        var posted = await Record(flow, anonymous); Assert.Equal(230m, posted.GrandTotal);
        var missing = (await flow.QuoteAsync(basket with { PayorId = null })).Value!;
        Assert.False(missing.CanRecord);
        var conflict = (await flow.RecordAsync(new(anonymous with { PayorId = w.PayorId }, "ignored"))).Value!;
        Assert.Contains(conflict.Problems, p => p.Code == "SessionIntentConflict"); Assert.Equal(2, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Stale_policy_and_disabled_policy_preflight_leave_no_collection_or_assessment()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var flow = Workflow(db, w); var basket = Basket(w);
        var quote = (await flow.QuoteAsync(basket)).Value!;
        var fish = await db.RevenueClassifications.SingleAsync(x => x.SemanticCode == RevenueClassificationCodes.FishMeatVendorFee);
        db.Add(RevenueClassificationPolicy.Create(fish.Id, Today, "Vendor Fee", RevenueInstrumentType.OfficialReceipt, w.TenantId));
        await db.SaveChangesAsync();
        var stale = (await flow.RecordAsync(new(basket, quote.QuoteFingerprint))).Value!;
        Assert.Equal(CollectionSessionStatus.NeedsReview, stale.Status); Assert.Empty(await db.Collections.ToListAsync());
        var landing = await db.GovernedServices.SingleAsync(x => x.OperationCode == CollectorOperationCodes.LandingBerthing);
        db.Add(GovernedServiceSetting.Create(w.TenantId, landing.Id, Today, GovernedServiceBasis.FixedAmount, 200m, null, false, true, "head"));
        await db.SaveChangesAsync();
        var disabled = (await flow.QuoteAsync(basket)).Value!;
        Assert.Contains(disabled.Problems, p => p.ClientItemId == basket.Items[1].ClientItemId && p.Code == "SourceNotAvailable");
        Assert.Empty(await db.Collections.ToListAsync());
    }

    private sealed class FailAfterFirst(ICollectionSessionSources inner) : ICollectionSessionSources
    {
        private int _count;
        public Task<CollectionSessionDiscovery> DiscoverAsync(Guid? id, DateOnly date, CancellationToken ct) => inner.DiscoverAsync(id, date, ct);
        public Task<(CollectionSessionItemQuote? Quote, CollectionSessionProblem? Problem)> QuoteAsync(CollectionSessionIntent s, CollectionSessionItemIntent i, CancellationToken ct) => inner.QuoteAsync(s, i, ct);
        public async Task<CollectionSessionCollection> PostAsync(CollectionSessionIntent s, CollectionSessionItemIntent i, Guid id, CancellationToken ct)
        {
            if (++_count == 2) throw new IOException("Injected outage after first child save");
            return await inner.PostAsync(s, i, id, ct);
        }
    }
    [SkippableFact]
    public async Task A_failure_after_a_child_save_rolls_back_everything_and_same_session_retries_safely()
    {
        var w = await SeedAsync(); var basket = Basket(w);
        await using (var db = database.CreateContext(w.TenantId))
        {
            var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), null!, new NoMarketDays());
            var flow = Workflow(db, w, new FailAfterFirst(sources)); var quote = (await flow.QuoteAsync(basket)).Value!;
            await Assert.ThrowsAsync<IOException>(() => flow.RecordAsync(new(basket, quote.QuoteFingerprint)));
        }
        await using var verify = database.CreateContext(w.TenantId);
        Assert.Equal(0, await verify.Collections.CountAsync()); Assert.Equal(0, await verify.PostingOperations.CountAsync());
        Assert.Equal(0, await verify.MobileCollectionSessions.CountAsync()); Assert.Equal(0, await verify.ObligationPeriods.CountAsync());
        Assert.Equal(3, (await Record(Workflow(verify, w), basket)).Collections.Count);
    }

    [SkippableFact]
    public async Task Tenant_role_assignment_and_result_ownership_fail_closed()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var basket = Basket(w);
        Assert.Equal(ResultStatus.Forbidden, (await Workflow(db, w, actor: new(w.CollectorId, w.TenantId, "Admin")).QuoteAsync(basket)).Status);
        Assert.Equal(ResultStatus.Forbidden, (await Workflow(db, w, actor: new(w.CollectorId, Guid.NewGuid())).QuoteAsync(basket)).Status);
        var invalidPayor = (await Workflow(db, w).QuoteAsync(basket with { PayorId = Guid.NewGuid() })).Value!;
        Assert.Contains(invalidPayor.Problems, p => p.Code == "InvalidPayor");
        var assignments = await db.CollectorOperationAssignments.ToListAsync(); db.RemoveRange(assignments); await db.SaveChangesAsync();
        Assert.False((await Workflow(db, w).QuoteAsync(basket)).Value!.CanRecord);
        Assert.Equal(0, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Prepared_water_wins_and_canonical_electricity_uses_existing_bill_writer()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        db.AddRange(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "head"),
            CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, Today, Guid.NewGuid(), "head", DateTime.UtcNow));
        var bill = UtilityBill.Create(w.StallId, Today.Year, Today.Month, 0, 8, 10, 0, 1, 83, "head");
        db.Add(bill); await db.SaveChangesAsync();
        var waterSource = Assert.Single((await new WcfCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock())
            .GetMobileSourcesAsync(Today.Year, Today.Month)).Value!);
        var item = new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.Water, 70m,
            Water: new(w.StallId, Today.Year, Today.Month, bill.Id, waterSource.WaterSourceVersion));
        var basket = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId, [item]);
        var flow = Workflow(db, w);
        var changed = (await flow.QuoteAsync(basket)).Value!;
        Assert.Equal(70m, Assert.Single(changed.Items).Amount);
        Assert.True(changed.CanRecord);
        Assert.Equal(0, await db.Collections.CountAsync());
        Assert.Equal(83m, (await Record(flow, basket with { Items = [item with { ConfirmedAmount = 83m }] })).GrandTotal);

        var electricity = new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.Electricity, 80m,
            Electricity: new(bill.Id, bill.ElectricitySourceVersion));
        var ecf = basket with { ClientCollectionSessionId = Guid.NewGuid(), Items = [electricity] };
        Assert.True((await flow.QuoteAsync(ecf)).Value!.CanRecord);
        bill.MarkElectricityPendingCutover();
        var at = DateTime.UtcNow.AddMinutes(-2);
        var cutover = CollectionSettlementCutover.Freeze(w.TenantId, CollectionSourceKind.UtilityBill, bill.Id,
            CollectionSourcePart.Electricity, bill.ElectricitySourceVersion, at, 80m, 0m, 80m,
            "{\"pendingMobileOperations\":0,\"issuedDocuments\":0,\"legacyWritersQuiesced\":true}", Guid.NewGuid(), at.AddMinutes(1));
        bill.ActivateCanonicalElectricitySettlement(cutover); db.Add(cutover); await db.SaveChangesAsync();
        var recorded = await Record(flow, ecf with { Items = [electricity with { Electricity = new(bill.Id, bill.ElectricitySourceVersion) }] });
        Assert.Equal(80m, recorded.GrandTotal); Assert.Equal(RevenueInstrumentType.OfficialReceipt, Assert.Single(recorded.Collections).Instrument);
        Assert.Equal(2, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Dynamic_fee_identity_and_effective_rate_version_are_revalidated()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var service = await db.GovernedServices.SingleAsync(x => x.OperationCode == CollectorOperationCodes.MarketFees);
        db.Add(GovernedServiceSetting.Create(w.TenantId, service.Id, Today, GovernedServiceBasis.ApprovedFeeOption, null, null, true, true, "head"));
        var option = GovernedServiceFeeOption.Create(w.TenantId, service.Id, "Configured facility charge", null, "Terminal", null, "head");
        db.AddRange(option, GovernedServiceFeeOptionRate.Create(w.TenantId, option.Id, Today.AddDays(-1), GovernedServiceBasis.FixedAmount, 30m, null, "head"));
        await db.SaveChangesAsync();
        var basket = Basket(w); basket = basket with { Items = [basket.Items[0] with { Service = new(CollectorOperationCodes.MarketFees, FeeOptionId: option.Id) }] };
        var flow = Workflow(db, w); var quote = (await flow.QuoteAsync(basket)).Value!; Assert.True(quote.CanRecord);
        var missing = (await flow.QuoteAsync(basket with { Items = [basket.Items[0] with { Service = new(CollectorOperationCodes.MarketFees) }] })).Value!;
        Assert.Contains(missing.Problems, p => p.Code == "InvalidSource");
        // Same amount, different approved effective version: the old quote must still be stale.
        db.Add(GovernedServiceFeeOptionRate.Create(w.TenantId, option.Id, Today, GovernedServiceBasis.FixedAmount, 30m, null, "head"));
        await db.SaveChangesAsync();
        var stale = (await flow.RecordAsync(new(basket, quote.QuoteFingerprint))).Value!;
        Assert.Contains(stale.Problems, p => p.Code == "QuoteStale"); Assert.Equal(0, await db.Collections.CountAsync());
        Assert.Equal(30m, (await Record(flow, basket)).GrandTotal);
    }

    [SkippableFact]
    public async Task Foreign_payer_account_and_source_are_unavailable_without_cross_tenant_details()
    {
        var w = await SeedAsync();
        var foreign = Municipality.Create("FOREIGN-" + Guid.NewGuid().ToString("N")[..6], "Foreign", "Province", MunicipalityStatus.Active,
            tenantCode: "foreign-" + Guid.NewGuid().ToString("N")[..8]);
        await using (var setup = database.CreateContext(Guid.Empty)) { setup.Add(foreign); await setup.SaveChangesAsync(); }
        var payer = Payor.Create(foreign.Id, "Secret payer", BusinessPayorKind.Person, "test");
        var facility = Facility.Create(FacilityCode.NPM, "Foreign market", "NPM", municipalityId: foreign.Id);
        var stall = Stall.Create(facility.Id, "SECRET-1", 0m, ApplicableFees.Water, MarketSection.FishSection, createdBy: "test", municipalityId: foreign.Id);
        var account = ObligationAccount.Create(foreign.Id, ObligationKind.FishMeatVendorFee, payer.Id, stall.Id, "Secret account", null, null, Today, "test");
        var bill = UtilityBill.Create(stall.Id, Today.Year, Today.Month, 0, 8, 10, 0, 1, 83, "head");
        await using (var other = database.CreateContext(foreign.Id)) { other.AddRange(payer, facility, stall, account, bill); await other.SaveChangesAsync(); }
        await using var db = database.CreateContext(w.TenantId); var flow = Workflow(db, w); var basket = Basket(w);
        Assert.Contains((await flow.QuoteAsync(basket with { PayorId = payer.Id })).Value!.Problems, p => p.Code == "InvalidPayor");
        var invalid = (await flow.QuoteAsync(basket with { Items = [basket.Items[2] with { VendorFee = new(stall.Id) }] })).Value!;
        Assert.Contains(invalid.Problems, p => p.Code == "InvalidSource");
        Assert.DoesNotContain(invalid.Problems, p => p.Message.Contains("Secret"));
        var foreignBill = new CollectionSessionItemIntent(Guid.NewGuid(), CollectionSessionItemKind.Electricity, 80m, Electricity: new(bill.Id, bill.ElectricitySourceVersion));
        Assert.Contains((await flow.QuoteAsync(basket with { Items = [foreignBill] })).Value!.Problems, p => p.Code == "InvalidSource");
        Assert.Equal(0, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Concurrent_unknown_response_replays_reconcile_to_one_atomic_outcome()
    {
        var w = await SeedAsync(); var basket = Basket(w);
        string? fingerprint;
        await using (var quoteDb = database.CreateContext(w.TenantId)) fingerprint = (await Workflow(quoteDb, w).QuoteAsync(basket)).Value!.QuoteFingerprint;
        async Task<Result<CollectionSessionResult>> Attempt()
        {
            await using var db = database.CreateContext(w.TenantId);
            return await Workflow(db, w).RecordAsync(new(basket, fingerprint));
        }
        var attempts = await Task.WhenAll(Attempt(), Attempt());
        Assert.Contains(attempts, x => x.IsSuccess && x.Value!.Status == CollectionSessionStatus.Recorded);
        await using var verify = database.CreateContext(w.TenantId);
        var replay = (await Workflow(verify, w).RecordAsync(new(basket, fingerprint))).Value!;
        Assert.True(replay.ExistingOutcome); Assert.Equal(3, replay.Collections.Count);
        Assert.Equal(3, await verify.Collections.CountAsync()); Assert.Single(await verify.MobileCollectionSessions.ToListAsync());
        foreach (var mutate in new[]
        {
            basket with { Items = [basket.Items[0] with { ConfirmedAmount = 31m }] },
            basket with { Items = [basket.Items[0] with { Service = new(CollectorOperationCodes.LandingBerthing) }] },
            basket with { Items = [basket.Items[0] with { ClientItemId = Guid.NewGuid() }] }
        }) Assert.Contains((await Workflow(verify, w).RecordAsync(new(mutate, fingerprint))).Value!.Problems, p => p.Code == "SessionIntentConflict");
        Assert.Equal(3, await verify.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Discovery_reuses_assigned_capabilities_and_returns_payer_scoped_sources_without_assessment()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var caller = new Caller(w.CollectorId, w.TenantId); var tenant = new Tenant(w.TenantId);
        var flow = Workflow(db, w, new CollectionSessionSources(db, caller, tenant, new Clock(), new DiscoverySender(db, w), new NoMarketDays()));
        var discovery = (await flow.DiscoverAsync(w.PayorId)).Value!;
        Assert.Contains(discovery.Operations, x => x.OperationCode == CollectorOperationCodes.MarketFees && x.CanAdd);
        Assert.Equal(2, discovery.ServiceTerms!.Count);
        Assert.Empty(discovery.ObligationSources!);
        Assert.Equal(w.PayorId, Assert.Single(discovery.VendorFeeSources!).PayorId);
        Assert.Contains(discovery.Operations, x => x.OperationCode == "NPM_WHOLE_PAYMENT" && !x.Supported);
        Assert.Contains(discovery.Operations, x => x.Kind == CollectionSessionItemKind.Weighing && !x.CanAdd && x.ReasonCode == "SourceNotAvailable");
        Assert.Equal(0, await db.ObligationPeriods.CountAsync()); Assert.Equal(0, await db.Collections.CountAsync());
    }
    [SkippableFact]
    public async Task Typed_choices_cover_supported_sources_and_settled_Npm_is_no_longer_selectable()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        await EnableRentAndWeighing(db, w);
        db.AddRange(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "head"),
            CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, Today, Guid.NewGuid(), "head", DateTime.UtcNow));
        foreach (var (code, classificationCode, basis, instrument) in new[]
        {
            (CollectorOperationCodes.Transportation, RevenueClassificationCodes.TransportationParking, GovernedServiceBasis.VehicleClassRate, RevenueInstrumentType.CashTicket),
            (CollectorOperationCodes.TransferLargeCattle, RevenueClassificationCodes.TransferLargeCattle, GovernedServiceBasis.ApprovedFeeOption, RevenueInstrumentType.OfficialReceipt),
            (CollectorOperationCodes.VegetableFruitSpaceRental, RevenueClassificationCodes.VegetableFruitSpaceRental, GovernedServiceBasis.DirectApprovedAmount, RevenueInstrumentType.OfficialReceipt)
        })
        {
            var classification = RevenueClassification.Create(classificationCode, w.TenantId);
            var service = GovernedService.Create(w.TenantId, code, "head");
            db.AddRange(classification, service, CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, code, "head"),
                GovernedServiceSetting.Create(w.TenantId, service.Id, Today, basis, null, null, true, true, "head"));
            if (code == CollectorOperationCodes.VegetableFruitSpaceRental)
                db.AddRange(RevenueClassificationPolicy.Create(classification.Id, Today, "Whole", instrument, w.TenantId, businessContext: RevenuePolicyContext.VegetableWholePayment),
                    RevenueClassificationPolicy.Create(classification.Id, Today, "Daily", RevenueInstrumentType.CashTicket, w.TenantId, businessContext: RevenuePolicyContext.VegetableDailyTransaction));
            else db.Add(RevenueClassificationPolicy.Create(classification.Id, Today, code, instrument, w.TenantId));
            if (code == CollectorOperationCodes.TransferLargeCattle)
                foreach (var amount in new[] { 100m, 200m })
                {
                    // Deliberately same labels: identity must come from the option ID, never text.
                    var option = GovernedServiceFeeOption.Create(w.TenantId, service.Id, "Approved transfer", null, null, null, "head");
                    db.AddRange(option, GovernedServiceFeeOptionRate.Create(w.TenantId, option.Id, Today, GovernedServiceBasis.FixedAmount, amount, null, "head"));
                }
        }
        var jeepney = VehicleClass.Create(w.TenantId, "JEEPNEY", "Jeepney", "head");
        var bus = VehicleClass.Create(w.TenantId, "BUS", "Bus", "head");
        db.AddRange(jeepney, bus, VehicleClassRate.Create(w.TenantId, jeepney.Id, Today, 20m, "head"),
            VehicleClassRate.Create(w.TenantId, bus.Id, Today, 40m, "head"));
        var market = await db.GovernedServices.SingleAsync(x => x.OperationCode == CollectorOperationCodes.MarketFees);
        var fee = GovernedServiceFeeOption.Create(w.TenantId, market.Id, "Configured fee", null, "Terminal", null, "head");
        db.AddRange(GovernedServiceSetting.Create(w.TenantId, market.Id, Today, GovernedServiceBasis.ApprovedFeeOption, null, null, true, true, "head"),
            fee, GovernedServiceFeeOptionRate.Create(w.TenantId, fee.Id, Today, GovernedServiceBasis.DirectApprovedAmount, null, 50m, "head"));
        var slh = Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH", municipalityId: w.TenantId);
        var slaughterClass = RevenueClassification.Create(RevenueClassificationCodes.Slaughterhouse, w.TenantId);
        var slaughterService = GovernedService.Create(w.TenantId, CollectorOperationCodes.Slaughterhouse, "head");
        db.AddRange(slh, slaughterClass, slaughterService, CollectorFacilityAssignment.Create(w.CollectorId, slh.Id, FacilityCode.SLH),
            GovernedServiceSetting.Create(w.TenantId, slaughterService.Id, Today, GovernedServiceBasis.DirectApprovedAmount, null, null, true, true, "head"),
            RevenueClassificationPolicy.Create(slaughterClass.Id, Today, "Slaughter", RevenueInstrumentType.OfficialReceipt, w.TenantId),
            FacilityRate.Create(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 120m, Today, w.TenantId));
        await db.SaveChangesAsync();
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(),
            new DiscoverySender(db, w), new NoMarketDays(), Whole(db, w));
        var flow = Workflow(db, w, sources);
        var discovery = (await flow.DiscoverAsync(w.PayorId)).Value!;
        var available = discovery.Operations.Where(x => x.CanAdd).ToArray();
        Assert.Equal(11, available.Length);
        Assert.All(available, x => Assert.NotEmpty(x.Choices!));
        var water = Assert.Single(available, x => x.Kind == CollectionSessionItemKind.Water);
        Assert.True(water.CanAutoSelect); Assert.Equal(CollectionSessionAmountRule.DirectAmount, Assert.Single(water.Choices!).AmountRule);
        Assert.Null(water.Choices![0].ServerAmount);
        var electricity = Assert.Single(available, x => x.Kind == CollectionSessionItemKind.Electricity);
        Assert.True(electricity.CanAutoSelect); Assert.Equal(w.StallId, Assert.Single(electricity.Choices!).Identity.StallId);
        var weighing = Assert.Single(available, x => x.Kind == CollectionSessionItemKind.Weighing);
        Assert.NotNull(Assert.Single(weighing.Choices!).RateId);
        var transfer = Assert.Single(available, x => x.OperationCode == CollectorOperationCodes.TransferLargeCattle);
        Assert.Equal(2, transfer.EligibleChoiceCount); Assert.False(transfer.CanAutoSelect);
        Assert.Equal(2, transfer.Choices!.Select(x => x.Identity.FeeOptionId).Distinct().Count());
        var transportation = Assert.Single(available, x => x.OperationCode == CollectorOperationCodes.Transportation);
        Assert.Equal(new[] { 20m, 40m }, transportation.Choices!.Select(x => x.ServerAmount!.Value).OrderBy(x => x));
        var marketChoice = Assert.Single(available.Single(x => x.OperationCode == CollectorOperationCodes.MarketFees).Choices!);
        Assert.Equal(fee.Id, marketChoice.Identity.FeeOptionId); Assert.True(marketChoice.CanEnterAmount); Assert.Equal(50m, marketChoice.MaximumAmount);
        Assert.Equal(2, available.Single(x => x.OperationCode == CollectorOperationCodes.VegetableFruitSpaceRental).EligibleChoiceCount);
        var rent = available.Single(x => x.Kind == CollectionSessionItemKind.NpmWholePayment);
        Assert.True(rent.CanAutoSelect); Assert.NotNull(Assert.Single(rent.Choices!).Identity.OccupancyId);
        Assert.Equal(Today.Month, rent.Choices![0].Identity.Month); Assert.Equal(900m, rent.Choices[0].ServerAmount);
        Assert.All(discovery.Operations.Where(x => !x.Supported), x => Assert.Empty(x.Choices!));
        Assert.Equal(0, await db.Collections.CountAsync()); Assert.Equal(0, await db.UtilityBills.CountAsync());
        var anonymous = (await flow.DiscoverAsync(null)).Value!;
        Assert.True(anonymous.Operations.Single(x => x.OperationCode == CollectorOperationCodes.LandingBerthing).CanAutoSelect);
        Assert.Empty(anonymous.Operations.Single(x => x.Kind == CollectionSessionItemKind.Water).Choices!);
        var selectedItems = available.Select(operation =>
        {
            var choice = operation.Choices![0]; var identity = choice.Identity;
            return new CollectionSessionItemIntent(Guid.NewGuid(), choice.Kind,
                choice.AmountRule is CollectionSessionAmountRule.MonthlyRemaining or CollectionSessionAmountRule.QuantityRate ? 0m : choice.ServerAmount ?? 20m,
                Water: choice.Kind == CollectionSessionItemKind.Water ? new(identity.StallId!.Value, identity.Year!.Value, identity.Month!.Value, identity.UtilityBillId, identity.SourceVersion) : null,
                Electricity: choice.Kind == CollectionSessionItemKind.Electricity ? new(identity.UtilityBillId ?? Guid.Empty, identity.SourceVersion, identity.StallId, identity.Year, identity.Month) : null,
                VendorFee: choice.Kind == CollectionSessionItemKind.VendorFee ? new(identity.StallId!.Value) : null,
                NpmWhole: choice.Kind == CollectionSessionItemKind.NpmWholePayment ? new(identity.StallId!.Value, identity.Year!.Value, identity.Month!.Value) : null,
                Weighing: choice.Kind == CollectionSessionItemKind.Weighing ? new(identity.StallId!.Value, identity.WeighingType!.Value, 1m) : null,
                Slaughter: choice.Kind == CollectionSessionItemKind.Slaughter ? new(identity.Animal!.Value, 1, identity.CustomAnimalName) : null,
                Service: choice.Kind == CollectionSessionItemKind.GovernedService ? new(choice.OperationCode, identity.Mode, identity.FeeOptionId, identity.VehicleClassCode, "Reviewed reference") : null);
        }).ToArray();
        var checkout = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId, selectedItems);
        var approved = (await flow.QuoteAsync(checkout)).Value!;
        Assert.True(approved.CanRecord, string.Join(";", approved.Problems.Select(x => x.Message)));
        var posted = await Record(flow, checkout);
        Assert.Equal(11, posted.Collections.Count); Assert.Equal(11, posted.Collections.Select(x => x.ReferenceCode).Distinct().Count());
        Assert.Equal(approved.GrandTotal, posted.GrandTotal);
        Assert.True((await flow.RecordAsync(new(checkout, "unknown response retry"))).Value!.ExistingOutcome);
        Assert.Equal(11, await db.Collections.CountAsync());
        var settled = (await flow.DiscoverAsync(w.PayorId)).Value!.Operations.Single(x => x.Kind == CollectionSessionItemKind.NpmWholePayment);
        Assert.False(settled.CanAdd); Assert.Equal("NoEligibleSource", settled.ReasonCode); Assert.Empty(settled.Choices!);
    }

    private sealed class DiscoverySender(AppDbContext db, World w) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            object response = request switch
            {
                GetCollectorMobileMenuQuery => Result<MobileMenuDto>.Success(new(w.CollectorId, "Collector", "C-1", Today,
                    [new(FacilityCode.NPM, "Market", "Market", true, true),
                     new(FacilityCode.SLH, "Slaughterhouse", "Slaughterhouse", true,
                         await db.CollectorFacilityAssignments.AnyAsync(x => x.CollectorId == w.CollectorId && x.FacilityCode == FacilityCode.SLH, ct),
                         CanonicalCollection: await new GovernedCanonicalAuthority(db, new Tenant(w.TenantId))
                             .IsCanonicalForCollectorAsync(CollectorOperationCodes.Slaughterhouse, Today, ct))])),
                GetCollectorOperationCapabilitiesQuery => await new GetCollectorOperationCapabilitiesQueryHandler(db,
                    new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock()).Handle(new(), ct),
                _ => throw new NotSupportedException()
            };
            return (TResponse)response;
        }
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [SkippableFact]
    public async Task Electricity_direct_receipts_repeat_and_never_become_a_monthly_assessment()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var flow = Workflow(db, w);
        foreach (var amount in new[] { 44m, 20m })
        {
            var source = Assert.Single((await new CollectionComposerWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock())
                .GetMobileEcfSourcesAsync(w.PayorId)).Value!);
            Assert.Equal("DirectCollection", source.ChargeBasis); Assert.True(source.CanPostCanonical);
            await Record(flow, new(Guid.NewGuid(), Today, w.PayorId,
                [new(Guid.NewGuid(), CollectionSessionItemKind.Electricity, amount,
                    Electricity: new(source.UtilityBillId, source.ElectricitySourceVersion, source.StallId, source.BillingYear, source.BillingMonth))]));
        }
        Assert.Equal(2, await db.Collections.CountAsync()); Assert.Equal(64m, await db.Collections.SumAsync(x => x.TotalAmount));
        var bill = await db.UtilityBills.SingleAsync(); Assert.Equal(0m, bill.ElecCharge); Assert.True(bill.ElectricityDirectCollection);
        Assert.Throws<InvalidOperationException>(() => bill.UpdateReadings(0, 1, 44, 0, 0, 0, null, "head"));
    }

    [SkippableFact]
    public async Task Prepared_utilities_accept_partial_collections_and_refuse_overpayment()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var bill = UtilityBill.Create(w.StallId, Today.Year, Today.Month, 0, 1, 500m, 0, 1, 100m, "head");
        db.AddRange(bill, CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "head"),
            CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, Today, Guid.NewGuid(), "head", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var flow = Workflow(db, w);
        foreach (var amount in new[] { 200m, 100m })
        {
            var source = (await new CollectionComposerWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock()).GetMobileEcfSourcesAsync(w.PayorId)).Value!.Single();
            await Record(flow, new(Guid.NewGuid(), Today, w.PayorId, [new(Guid.NewGuid(), CollectionSessionItemKind.Electricity, amount, Electricity: new(bill.Id, source.ElectricitySourceVersion))]));
        }
        foreach (var amount in new[] { 20m, 30m })
        {
            var source = (await new WcfCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock()).GetMobileSourcesAsync(Today.Year, Today.Month)).Value!.Single();
            await Record(flow, new(Guid.NewGuid(), Today, w.PayorId, [new(Guid.NewGuid(), CollectionSessionItemKind.Water, amount, Water: new(w.StallId, Today.Year, Today.Month, bill.Id, source.WaterSourceVersion))]));
        }
        var ecf = (await new CollectionComposerWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock()).GetMobileEcfSourcesAsync(w.PayorId)).Value!.Single();
        var water = (await new WcfCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock()).GetMobileSourcesAsync(Today.Year, Today.Month)).Value!.Single();
        Assert.Equal(200m, ecf.OutstandingAmount); Assert.Equal(50m, water.OutstandingAmount);
        var bad = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId,
            [new(Guid.NewGuid(), CollectionSessionItemKind.Electricity, 201m, Electricity: new(bill.Id, ecf.ElectricitySourceVersion)),
             new(Guid.NewGuid(), CollectionSessionItemKind.Water, 51m, Water: new(w.StallId, Today.Year, Today.Month, bill.Id, water.WaterSourceVersion))]);
        Assert.False((await flow.QuoteAsync(bad)).Value!.CanRecord);
        Assert.Equal(4, await db.Collections.CountAsync());
        Assert.False(bill.ElectricityDirectCollection); Assert.False(bill.WaterDirectCollection);
    }

    [SkippableFact]
    public async Task Slaughter_uses_existing_approved_calculation_and_keeps_each_transaction_boundary()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var facility = Facility.Create(FacilityCode.SLH, "Slaughterhouse", "SLH", municipalityId: w.TenantId);
        var service = GovernedService.Create(w.TenantId, CollectorOperationCodes.Slaughterhouse, "head");
        var classification = RevenueClassification.Create(RevenueClassificationCodes.Slaughterhouse, w.TenantId);
        db.AddRange(facility, service, classification, CollectorFacilityAssignment.Create(w.CollectorId, facility.Id, FacilityCode.SLH),
            GovernedServiceSetting.Create(w.TenantId, service.Id, Today, GovernedServiceBasis.FixedAmount, 1m, null, true, true, "head"),
            RevenueClassificationPolicy.Create(classification.Id, Today, "Slaughterhouse", RevenueInstrumentType.OfficialReceipt, w.TenantId),
            FacilityRate.Create(FacilityCode.SLH, FeeRateKey.SlhHogPerHead, 120m, Today, w.TenantId));
        await db.SaveChangesAsync();
        var flow = Workflow(db, w);
        var basket = new CollectionSessionIntent(Guid.NewGuid(), Today, null,
            [new(Guid.NewGuid(), CollectionSessionItemKind.Slaughter, 0m, Slaughter: new(AnimalType.Hog, 2, OwnerName: "Walk-in owner")),
             new(Guid.NewGuid(), CollectionSessionItemKind.Slaughter, 0m, Slaughter: new(AnimalType.Hog, 1, OwnerName: "Walk-in owner"))]);
        var quote = (await flow.QuoteAsync(basket)).Value!;
        Assert.True(quote.CanRecord, string.Join(";", quote.Problems.Select(x => x.Message)));
        Assert.Equal(360m, quote.GrandTotal); Assert.Equal(0, await db.Collections.CountAsync());
        var recorded = await Record(flow, basket);
        Assert.Equal(2, recorded.Collections.Count); Assert.Equal(2, recorded.Collections.Select(x => x.ReferenceCode).Distinct().Count());
        Assert.All(await db.CollectionLines.ToListAsync(), x => Assert.Equal(classification.Id, x.RevenueClassificationId));
        Assert.Equal(0, await db.SlaughterTransactions.CountAsync());
        var invalid = basket with { ClientCollectionSessionId = Guid.NewGuid(), Items = [basket.Items[0] with { Slaughter = new(AnimalType.Hog, 0, OwnerName: "Walk-in owner") }] };
        Assert.False((await flow.QuoteAsync(invalid)).Value!.CanRecord);
        Assert.True((await flow.RecordAsync(new(basket, quote.QuoteFingerprint))).Value!.ExistingOutcome);
        Assert.Equal(2, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task Weighing_freezes_server_rates_and_stale_quotes_require_review()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.WeightAndMeasure, w.TenantId);
        var fish = FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmFishPerKilo, 2m, Today, w.TenantId);
        db.AddRange(classification, fish, FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmMeatPerKilo, 3m, Today, w.TenantId),
            RevenueClassificationPolicy.Create(classification.Id, Today, "Weight & Measure", RevenueInstrumentType.OfficialReceipt, w.TenantId));
        await db.SaveChangesAsync();
        var flow = Workflow(db, w);
        var basket = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId,
            [new(Guid.NewGuid(), CollectionSessionItemKind.Weighing, 0m, Weighing: new(w.StallId, WeighingType.Fish, 18.5m))]);
        var quote = (await flow.QuoteAsync(basket)).Value!; Assert.Equal(37m, quote.GrandTotal);
        fish.UpdateAmount(4m, "head"); await db.SaveChangesAsync();
        Assert.Contains((await flow.RecordAsync(new(basket, quote.QuoteFingerprint))).Value!.Problems, p => p.Code == "QuoteStale");
        Assert.Equal(0, await db.Collections.CountAsync());
        var posted = await Record(flow, basket); Assert.Equal(74m, posted.GrandTotal);
        var snapshot = (await db.CollectionLines.SingleAsync()).CalculationSnapshot!;
        Assert.Contains("18.5", snapshot); Assert.Contains(fish.Id.ToString(), snapshot);
        fish.UpdateAmount(6m, "head"); await db.SaveChangesAsync();
        Assert.Equal(snapshot, (await db.CollectionLines.SingleAsync()).CalculationSnapshot);
        var meat = basket with { ClientCollectionSessionId = Guid.NewGuid(), Items = [basket.Items[0] with { Weighing = new(w.StallId, WeighingType.Meat, 10m) }] };
        Assert.Equal(30m, (await Record(flow, meat)).GrandTotal);
    }

    [SkippableFact]
    public async Task Direct_water_receipts_repeat_without_creating_an_assessment()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        db.AddRange(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "head"),
            CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, Today, Guid.NewGuid(), "head", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var flow = Workflow(db, w);
        foreach (var amount in new[] { 20m, 30m, 10m })
        {
            var source = Assert.Single((await new WcfCollectionWorkflow(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock())
                .GetMobileSourcesAsync(Today.Year, Today.Month)).Value!);
            Assert.Null(source.PreparedAmount); Assert.True(source.CanEnterDirect);
            await Record(flow, new(Guid.NewGuid(), Today, w.PayorId,
                [new(Guid.NewGuid(), CollectionSessionItemKind.Water, amount,
                    Water: new(w.StallId, Today.Year, Today.Month, source.UtilityBillId, source.WaterSourceVersion))]));
        }
        Assert.Equal(3, await db.Collections.CountAsync());
        Assert.Equal(60m, await db.Collections.SumAsync(x => x.TotalAmount));
        Assert.Equal(0m, (await db.UtilityBills.SingleAsync()).WaterCharge);
    }

    [SkippableFact]
    public async Task Direct_water_and_voided_replay_do_not_create_replacement_money()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        db.AddRange(CollectorOperationAssignment.Assign(w.TenantId, w.CollectorId, CollectorOperationCodes.Wcf, "head"),
            CollectorOperationActivation.Activate(w.TenantId, CollectorOperationCodes.Wcf, Today, Guid.NewGuid(), "head", DateTime.UtcNow));
        await db.SaveChangesAsync();
        var basket = new CollectionSessionIntent(Guid.NewGuid(), Today, w.PayorId,
            [new(Guid.NewGuid(), CollectionSessionItemKind.Water, 83m, Water: new(w.StallId, Today.Year, Today.Month))]);
        var flow = Workflow(db, w); var recorded = await Record(flow, basket);
        var child = Assert.Single(recorded.Collections); Assert.Equal(83m, child.Amount);
        var line = await db.CollectionLines.SingleAsync(x => x.CollectionId == child.CollectionId);
        var allocations = await db.CollectionAllocations.Where(x => x.CollectionLineId == line.Id).ToListAsync();
        db.Add(CollectionCorrection.Record(w.TenantId, child.CollectionId, null, null, null, CollectionCorrectionType.Void,
            Today, DateTime.UtcNow, -83m, "Test correction", Guid.NewGuid().ToString(), "Head",
            [new(line.Id, -83m, allocations.Select(x => new CollectionCorrectionAllocationDraft(x.Id, -x.Amount)).ToArray())]));
        await db.SaveChangesAsync();
        var replay = (await flow.RecordAsync(new(basket, "original response was lost"))).Value!;
        Assert.True(replay.ExistingOutcome); Assert.Equal("Voided", Assert.Single(replay.Collections).Disposition);
        Assert.Equal(child.ReferenceCode, replay.Collections[0].ReferenceCode);
        Assert.Equal(1, await db.Collections.CountAsync());
    }

    [SkippableFact]
    public async Task One_malformed_electricity_bill_does_not_hide_an_unrelated_valid_bill()
    {
        var w = await SeedAsync(); await using var db = database.CreateContext(w.TenantId);
        var valid = UtilityBill.Create(w.StallId, Today.Year, Today.Month, 0, 8, 10, 0, 0, 0, "test");
        var period = Today.AddMonths(-1);
        var broken = UtilityBill.Create(w.StallId, period.Year, period.Month, 0, 8, 10, 0, 0, 0, "test");
        db.AddRange(valid, broken);
        // Historical malformed canonical evidence, deliberately without a cutover reference.
        db.Entry(broken).Property(x => x.ElectricitySettlementAuthorityState).CurrentValue = SettlementAuthority.Canonical;
        await db.SaveChangesAsync();
        var sources = new CollectionSessionSources(db, new Caller(w.CollectorId, w.TenantId), new Tenant(w.TenantId), new Clock(), new DiscoverySender(db, w), new NoMarketDays());
        var discovery = await sources.DiscoverAsync(w.PayorId, Today, default);
        Assert.Equal(valid.Id, Assert.Single(discovery.ElectricitySources!).UtilityBillId);
        Assert.Equal(0, await db.Collections.CountAsync());
    }
}
