using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Application.Queries.Revenue.GetCanonicalMonthlyIncome;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Queries.Revenue.GetOfficialMonthlyIncome;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.IntegrationTests;
public sealed partial class FineAssessmentTests
{
    private sealed class SettlementReportClock : IClock {
        public DateOnly PhilippineToday => Today;
        public DateTime PhilippineNow => new(2026,10,10,10,0,0);
        public DateTime UtcNow => DateTime.UtcNow.AddMinutes(1);
    }
    private CollectionComposerWorkflow Composer(AppDbContext db, Seed seed, Guid? actor = null, string role = "SuperAdmin") =>
        new(db, new Actor(seed.TenantId, role, actor ?? seed.ActorId), new Tenant(seed.TenantId), new Clock());
    private static async Task<decimal> OfficialFinesAsync(AppDbContext db,Seed seed) {
        var report=await new GetOfficialMonthlyIncomeQueryHandler(db,new LegacyMonthlyIncomeReader(db),new Actor(seed.TenantId,"SuperAdmin",seed.ActorId),new Tenant(seed.TenantId),new SettlementReportClock())
            .Handle(new(2026,10),default);
        Assert.True(report.IsSuccess,report.Error);
        return report.Value!.Groups.SelectMany(g=>g.Rows).Single(r=>r.ClassificationCode==RevenueClassificationCodes.PenaltiesAndFines).Total.SystemAmount;
    }
    private async Task<(Seed Seed, Guid Fine)> SettlementSeedAsync(bool closed = false)
    {
        var seed = await SeedAsync(closed: closed);
        await using var db = database.CreateContext(seed.TenantId);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.PenaltiesAndFines, seed.TenantId);
        db.RevenueClassifications.Add(classification);
        db.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(classification.Id, new(2023,1,1), "Fines", RevenueInstrumentType.OfficialReceipt, seed.TenantId));
        await db.SaveChangesAsync();
        var fine = await Workflow(db,seed).CreateAsync(Request(seed)); Assert.True(fine.IsSuccess, fine.Error);
        return (seed, fine.Value!.Id);
    }
    private static async Task<EcfCollectionDraftDto> FineDraftAsync(CollectionComposerWorkflow composer, Guid fine)
    {
        var quote = await composer.QuoteFineAsync(fine); Assert.True(quote.IsSuccess, quote.Error);
        var draft = await composer.AddFineAssessmentAsync(new(fine, quote.Value!.QuoteToken, quote.Value.Remaining));
        Assert.True(draft.IsSuccess,draft.Error);
        var reviewed = await composer.ReviewAsync(draft.Value!.DraftId, new(draft.Value.Revision));
        Assert.True(reviewed.IsSuccess,reviewed.Error); return reviewed.Value!;
    }
    [SkippableTheory] [InlineData(false)] [InlineData(true)]
    public async Task StandaloneFinePostsCanonicalCashAndReconcilesClosedLiability(bool closed)
    {
        var (seed, fine) = await SettlementSeedAsync(closed);
        await using var db = database.CreateContext(seed.TenantId); var composer = Composer(db,seed);
        var draft = await FineDraftAsync(composer, fine);
        var operation = Guid.NewGuid();
        var result = await composer.PostAsync(draft.DraftId,new(draft.Revision,operation));
        Assert.True(result.IsSuccess,result.Error);
        db.ChangeTracker.Clear();
        var balance = Assert.Single((await Workflow(db,seed).ReadAsync()).Value!);
        Assert.Equal((100m,0m,"Settled"),(balance.Settled,balance.Remaining,balance.Status));
        Assert.Single(await db.FineAssessments.ToListAsync()); Assert.Single(await db.FineAssessmentEvents.ToListAsync());
        var allocation = Assert.Single(await db.CollectionAllocations.ToListAsync());
        Assert.Equal((CollectionSourceKind.FineAssessment,fine,fine,100m),(allocation.SourceKind,allocation.SourceId,allocation.FineAssessmentId,allocation.Amount));
        Assert.Equal(100m,(await db.Collections.SingleAsync()).TotalAmount); Assert.NotEmpty(result.Value!.ReferenceCode);
        var replay = await composer.PostAsync(draft.DraftId,new(draft.Revision,operation)); Assert.True(replay.IsSuccess,replay.Error);
        Assert.Equal(result.Value.CollectionId,replay.Value!.CollectionId); Assert.Single(await db.Collections.ToListAsync());
        var otherFine=(await Workflow(db,seed).CreateAsync(Request(seed,"CASE-OTHER"))).Value!;
        var otherDraft=await FineDraftAsync(composer,otherFine.Id);
        Assert.Equal(409,(await composer.PostAsync(otherDraft.DraftId,new(otherDraft.Revision,operation))).StatusCode);
    }
    [SkippableTheory] [InlineData(40)] [InlineData(101)]
    public async Task FinePartialAndExcessAmountsAreRejected(decimal amount)
    {
        var (seed,fine)=await SettlementSeedAsync(); await using var db=database.CreateContext(seed.TenantId); var c=Composer(db,seed);
        var quote=(await c.QuoteFineAsync(fine)).Value!;
        Assert.False((await c.AddFineAssessmentAsync(new(fine,quote.QuoteToken,amount))).IsSuccess);
        Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.WebCollectionDrafts.ToListAsync());
    }
    [SkippableFact] public async Task ExistingAssessmentPaysAfterDefinitionRetirement()
    {
        var (seed,fine)=await SettlementSeedAsync(); await using var db=database.CreateContext(seed.TenantId);
        db.PenaltyDefinitions.Add(PenaltyDefinition.Create(seed.TenantId,"APPROVED_FINE",Today,"Retired",null,GovernedServiceBasis.FixedAmount,999m,null,false,"head"));
        await db.SaveChangesAsync(); var c=Composer(db,seed); var draft=await FineDraftAsync(c,fine);
        Assert.True((await c.PostAsync(draft.DraftId,new(draft.Revision,Guid.NewGuid()))).IsSuccess);
    }
    [SkippableFact] public async Task StaleFineQuoteAndWrongTenantCannotPost()
    {
        var (seed,fine)=await SettlementSeedAsync(); await using var db=database.CreateContext(seed.TenantId); var c=Composer(db,seed);
        var quote=(await c.QuoteFineAsync(fine)).Value!;
        Assert.False((await c.AddFineAssessmentAsync(new(fine,"stale",quote.Remaining))).IsSuccess);
        var foreign=Composer(db,seed with {TenantId=Guid.NewGuid()}); Assert.False((await foreign.QuoteFineAsync(fine)).IsSuccess);
        Assert.Empty(await db.Collections.ToListAsync());
    }
    [SkippableFact] public async Task ConcurrentFineDraftsHaveOneEffectiveAllocation()
    {
        var (seed,fine)=await SettlementSeedAsync(); var second=Guid.NewGuid();
        EcfCollectionDraftDto firstDraft,secondDraft;
        await using(var db=database.CreateContext(seed.TenantId)) firstDraft=await FineDraftAsync(Composer(db,seed),fine);
        await using(var db=database.CreateContext(seed.TenantId)) secondDraft=await FineDraftAsync(Composer(db,seed,second),fine);
        async Task<bool> Pay(EcfCollectionDraftDto draft,Guid actor) {await using var db=database.CreateContext(seed.TenantId); return (await Composer(db,seed,actor).PostAsync(draft.DraftId,new(draft.Revision,Guid.NewGuid()))).IsSuccess;}
        var winners=await Task.WhenAll(Pay(firstDraft,seed.ActorId),Pay(secondDraft,second)); Assert.Single(winners.Where(x=>x));
        await using var read=database.CreateContext(seed.TenantId); Assert.Single(await read.Collections.ToListAsync()); Assert.Single(await read.CollectionAllocations.ToListAsync());
    }
    [SkippableFact] public async Task SameAccountRentalAndFineShareAnOrButUnrelatedAccountsCannot()
    {
        var (seed,fine)=await SettlementSeedAsync(); await using var db=database.CreateContext(seed.TenantId);
        var rent=RevenueClassification.Create(RevenueClassificationCodes.KanmanggaySpaceRental,seed.TenantId);
        db.RevenueClassifications.Add(rent);
        db.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(rent.Id,new(2023,1,1),"Space rental",RevenueInstrumentType.OfficialReceipt,seed.TenantId));
        db.ObligationRates.Add(ObligationRate.Create(seed.TenantId,seed.Account.AccountId,new(2023,1,1),900m,"head")); await db.SaveChangesAsync();
        var c=Composer(db,seed); var rental=await c.AddObligationAllocationAsync(new(seed.Account.AccountId,2026,10,900m,null));
        Assert.True(rental.IsSuccess,rental.Error);
        var quote=(await c.QuoteFineAsync(fine)).Value!;
        var combined=await c.AddFineAssessmentAsync(new(fine,quote.QuoteToken,100m,rental.Value!.Revision)); Assert.True(combined.IsSuccess,combined.Error);
        Assert.Equal(1000m,combined.Value!.TotalAmount);
        var reviewed=await c.ReviewAsync(combined.Value.DraftId,new(combined.Value.Revision)); Assert.True(reviewed.IsSuccess,reviewed.Error);
        var posted=await c.PostAsync(reviewed.Value!.DraftId,new(reviewed.Value.Revision,Guid.NewGuid())); Assert.True(posted.IsSuccess,posted.Error);
        Assert.Equal(2,await db.CollectionLines.CountAsync()); Assert.Equal(100m,(await Workflow(db,seed).ReadAsync()).Value!.Single().Settled);
        Assert.Equal(100m,await OfficialFinesAsync(db,seed));
        // Unrelated source identity is rejected even with an identical occupant name.
        var other=ObligationAccount.Create(seed.TenantId,ObligationKind.KanmanggaySpaceRental,Guid.Empty,null,"Space 2",null,null,new(2023,1,1),"head",actualOccupant:"Liable holder");
        db.ObligationAccounts.Add(other); db.ObligationRates.Add(ObligationRate.Create(seed.TenantId,other.Id,new(2023,1,1),900m,"head")); await db.SaveChangesAsync();
        var newFine=(await Workflow(db,seed).CreateAsync(Request(seed,"CASE-SECOND"))).Value!;
        var otherRent=await c.AddObligationAllocationAsync(new(other.Id,2026,10,900m,null)); Assert.True(otherRent.IsSuccess,otherRent.Error);
        var newQuote=(await c.QuoteFineAsync(newFine.Id)).Value!;
        Assert.False((await c.AddFineAssessmentAsync(new(newFine.Id,newQuote.QuoteToken,100m,otherRent.Value!.Revision))).IsSuccess);
    }
    [SkippableFact] public async Task AssignedCollectorCanPayButUnassignedCollectorCannot()
    {
        var (seed,fine)=await SettlementSeedAsync(); await using var db=database.CreateContext(seed.TenantId);
        var collector=CollectorUser.Create("Collector","E1","fine-collector",null,null,new HashedPassword("test-hash"),seed.TenantId);
        db.CollectorUsers.Add(collector); await db.SaveChangesAsync();
        var c=Composer(db,seed,collector.Id,"Collector"); Assert.Equal(403,(await c.QuoteFineAsync(fine)).StatusCode);
        db.CollectorOperationAssignments.Add(CollectorOperationAssignment.Assign(seed.TenantId,collector.Id,CollectorOperationCodes.KanmanggaySpaceRental,"head"));
        await db.SaveChangesAsync(); var abandoned=await FineDraftAsync(c,fine);
        Assert.True((await c.DiscardAsync(abandoned.DraftId,new(abandoned.Revision))).IsSuccess);
        Assert.Empty(await db.Collections.ToListAsync()); var draft=await FineDraftAsync(c,fine);
        var posted=await c.PostAsync(draft.DraftId,new(draft.Revision,Guid.NewGuid())); Assert.True(posted.IsSuccess,posted.Error);
        Assert.Equal(collector.Id,(await db.Collections.SingleAsync()).CollectorId);
        var position=await new RemittanceWorkflow(db,new Actor(seed.TenantId,"Collector",collector.Id),new Tenant(seed.TenantId)).GetMyPositionAsync(Today,Today);
        Assert.True(position.IsSuccess,position.Error); Assert.Equal(100m,position.Value!.Collected);
        Assert.Equal(403,(await Workflow(db,seed,"Collector").CreateAsync(Request(seed,"CASE-UNAUTHORIZED"))).StatusCode);
    }
    [SkippableFact] public async Task ReversalRestoresFineOutstandingAndReportingWithoutRewritingSrc()
    {
        var (seed,fine)=await SettlementSeedAsync(); await using var db=database.CreateContext(seed.TenantId); var c=Composer(db,seed);
        var reportHandler=new GetCanonicalMonthlyIncomeQueryHandler(db,new Actor(seed.TenantId,"SuperAdmin",seed.ActorId),new Tenant(seed.TenantId),new SettlementReportClock());
        var unpaid=await reportHandler.Handle(new(2026,10,CanonicalReportingBasis.LatestCorrected,null),default); Assert.Equal(0m,unpaid.Value!.GrandTotal.NetCollected);
        Assert.Equal(0m,await OfficialFinesAsync(db,seed));
        var draft=await FineDraftAsync(c,fine); var post=await c.PostAsync(draft.DraftId,new(draft.Revision,Guid.NewGuid())); Assert.True(post.IsSuccess,post.Error);
        var paid=await reportHandler.Handle(new(2026,10,CanonicalReportingBasis.LatestCorrected,null),default); Assert.Equal(100m,paid.Value!.GrandTotal.NetCollected);
        Assert.Equal(100m,await OfficialFinesAsync(db,seed));
        var collection=await db.Collections.Include(x=>x.Lines).ThenInclude(l=>l.Allocations).SingleAsync(); var line=collection.Lines.Single(); var allocation=line.Allocations.Single(); var src=collection.ReferenceCode;
        db.CollectionCorrections.Add(CollectionCorrection.Record(seed.TenantId,collection.Id,null,null,null,CollectionCorrectionType.Reversal,Today,DateTime.UtcNow,-100m,"Approved correction evidence","head","Head",
            [new CollectionCorrectionLineDraft(line.Id,-100m,[new CollectionCorrectionAllocationDraft(allocation.Id,-100m)])]));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var balance=(await Workflow(db,seed).ReadAsync()).Value!.Single(); Assert.Equal((0m,100m,"Outstanding"),(balance.Settled,balance.Remaining,balance.Status));
        var history=await c.FineHistoryAsync(fine); var historicalPayment=Assert.Single(history.Value!);
        Assert.Equal((src,100m,0m),(historicalPayment.Src,historicalPayment.AllocatedAmount,historicalPayment.EffectiveAmount));
        var corrected=await reportHandler.Handle(new(2026,10,CanonicalReportingBasis.LatestCorrected,null),default); Assert.Equal(0m,corrected.Value!.GrandTotal.NetCollected);
        Assert.Equal(0m,await OfficialFinesAsync(db,seed));
        var register=await new PenaltyDefinitionWorkflow(db,new Actor(seed.TenantId,"SuperAdmin",seed.ActorId),new Tenant(seed.TenantId),new Clock()).GetRegisterAsync(Today,Today);
        Assert.Equal(0m,Assert.Single(register.Value!).EffectiveAmount); Assert.Equal("Reversed",register.Value!.Single().Status);
        Assert.Equal(src,(await db.Collections.SingleAsync()).ReferenceCode);
        var again=await FineDraftAsync(c,fine); Assert.True((await c.PostAsync(again.DraftId,new(again.Revision,Guid.NewGuid()))).IsSuccess);
        var replacement=await reportHandler.Handle(new(2026,10,CanonicalReportingBasis.LatestCorrected,null),default); Assert.Equal(100m,replacement.Value!.GrandTotal.NetCollected);
        Assert.Equal(100m,await OfficialFinesAsync(db,seed));
    }
    [SkippableFact] public async Task CashTicketPolicyCannotSettleFine()
    {
        var (seed,fine)=await SettlementSeedAsync(); await using var db=database.CreateContext(seed.TenantId);
        var policy=await db.RevenueClassificationPolicies.SingleAsync();
        db.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(policy.RevenueClassificationId,Today,"Fines CT",RevenueInstrumentType.CashTicket,seed.TenantId));
        await db.SaveChangesAsync(); var quote=await Composer(db,seed).QuoteFineAsync(fine);
        Assert.False(quote.IsSuccess); Assert.Contains("Official Receipt",quote.Error); Assert.Empty(await db.Collections.ToListAsync());
    }
    [SkippableFact] public async Task StaleReviewedDraftCannotPost()
    {
        var (seed,fine)=await SettlementSeedAsync(); await using var db=database.CreateContext(seed.TenantId); var c=Composer(db,seed);
        var draft=await FineDraftAsync(c,fine);
        Assert.Equal(409,(await c.PostAsync(draft.DraftId,new(draft.Revision-1,Guid.NewGuid()))).StatusCode);
        Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.CollectionAllocations.ToListAsync());
    }
    [SkippableFact] public async Task FailedCashInsertRollsBackClaimAllocationAndDurablePost()
    {
        var (seed,fine)=await SettlementSeedAsync(); await using var db=database.CreateContext(seed.TenantId); var c=Composer(db,seed);
        var draft=await FineDraftAsync(c,fine); var operation=Guid.NewGuid();
        // The fixture is an isolated disposable PostgreSQL database. Force a failure inside EF's cash transaction.
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_fine_test_cash() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'forced cash failure' USING ERRCODE='23514'; END $$; CREATE TRIGGER reject_fine_test_cash BEFORE INSERT ON \"Collections\" FOR EACH ROW EXECUTE FUNCTION reject_fine_test_cash();");
        try {
            Assert.False((await c.PostAsync(draft.DraftId,new(draft.Revision,operation))).IsSuccess);
            db.ChangeTracker.Clear(); Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.CollectionAllocations.ToListAsync());
            Assert.Empty(await db.PostingOperations.Where(p=>p.ClientOperationId==operation).ToListAsync());
            Assert.Equal(1,(await db.FineAssessments.SingleAsync()).Version);
        } finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_fine_test_cash ON \"Collections\"; DROP FUNCTION reject_fine_test_cash();"); }
        var retry=await c.PostAsync(draft.DraftId,new(draft.Revision,operation)); Assert.True(retry.IsSuccess,retry.Error);
        Assert.Single(await db.Collections.ToListAsync());
    }
}
