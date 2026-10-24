using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace EEMOCantilanSDS.IntegrationTests;
public sealed partial class FineAssessmentTests
{
    private async Task<(Seed Seed, Guid Authority)> DirectSeed(bool payer=false, bool evidence=false, bool uncapped=false,
        decimal? ceiling=500m, GovernedServiceBasis basis=GovernedServiceBasis.DirectApprovedAmount)
    {
        Skip.IfNot(database.Available, database.UnavailableReason ?? "PostgreSQL unavailable");
        await database.ResetAsync();
        var municipality=Municipality.Create("DIRECT", "Isolated direct-fine fixture", "TEST", MunicipalityStatus.Active);
        var actor=Guid.NewGuid(); await using var db=database.CreateContext(municipality.Id);
        db.Municipalities.Add(municipality);
        var definition=PenaltyDefinition.Create(municipality.Id,"TEST_DIRECT_FINE",Today.AddYears(-1),"Test authorized fine",null,
            basis,basis==GovernedServiceBasis.FixedAmount ? 100m : null,basis==GovernedServiceBasis.DirectApprovedAmount ? ceiling : null,true,"fixture");
        var authority=DirectFineAuthority.Record(municipality.Id,definition.Id,"ISOLATED TEST governing authority",
            "ISOLATED TEST approval: no registration required",Today.AddYears(-1),null,uncapped,payer,evidence,evidence,actor,new Clock().UtcNow);
        var classification=RevenueClassification.Create(RevenueClassificationCodes.PenaltiesAndFines,municipality.Id);
        db.PenaltyDefinitions.Add(definition); db.DirectFineAuthorities.Add(authority); db.RevenueClassifications.Add(classification);
        db.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(classification.Id,Today.AddYears(-1),"Fines",RevenueInstrumentType.OfficialReceipt,municipality.Id));
        await db.SaveChangesAsync();
        return (new(municipality.Id,new(FineAccountKind.FacilityContract,Guid.Empty),Guid.Empty,actor),authority.Id);
    }
    private DirectFineCollectionWorkflow Direct(AppDbContext db, Seed seed, string role="SuperAdmin") =>
        new(db,new Actor(seed.TenantId,role,seed.ActorId),new Tenant(seed.TenantId),new Clock());
    private static DirectFineCollectionRequest DirectRequest(Guid authority) => new(Guid.NewGuid(),authority,Today,100m);

    private async Task<Seed> ManualDirectSeed(bool receiptPolicy = true)
    {
        Skip.IfNot(database.Available, database.UnavailableReason ?? "PostgreSQL unavailable");
        await database.ResetAsync();
        var municipality = Municipality.Create("MANUALFINE", "Isolated manual fine office", "TEST", MunicipalityStatus.Active);
        var actor = Guid.NewGuid();
        await using var db = database.CreateContext(municipality.Id);
        db.Municipalities.Add(municipality);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.PenaltiesAndFines, municipality.Id);
        db.RevenueClassifications.Add(classification);
        if (receiptPolicy)
            db.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(classification.Id,
                Today.AddDays(-1), "Fines", RevenueInstrumentType.OfficialReceipt, municipality.Id));
        await db.SaveChangesAsync();
        return new(municipality.Id, new(FineAccountKind.FacilityContract, Guid.Empty), Guid.Empty, actor);
    }

    [SkippableFact]
    public async Task Manual_direct_fine_posts_without_authority_setup_and_is_visible_in_fines_reports()
    {
        var s = await ManualDirectSeed();
        await using var db = database.CreateContext(s.TenantId);
        var workflow = Direct(db, s);
        Assert.Empty(await db.DirectFineAuthorities.ToListAsync());
        Assert.Empty(await db.PenaltyDefinitions.ToListAsync());
        var options = (await workflow.OptionsAsync()).Value!;
        var manual = Assert.Single(options.Categories);
        Assert.Equal(Guid.Empty, manual.AuthorityId);
        Assert.True(manual.RequiresReason);
        Assert.Equal(RevenueInstrumentType.OfficialReceipt,
            (await workflow.QuoteAsync(new(Guid.NewGuid(), Guid.Empty, Today, 65.25m, "Walkway obstruction"))).Value!.Instrument);

        var request = new DirectFineCollectionRequest(Guid.NewGuid(), Guid.Empty, Today, 65.25m,
            "Walkway obstruction", "Inspector entry 22", "Walk-in payor");
        var posted = await workflow.PostAsync(request);
        Assert.True(posted.IsSuccess, posted.Error);
        Assert.Equal(65.25m, posted.Value!.Amount);
        Assert.StartsWith("SRC-", posted.Value.ReferenceCode);
        var replay = await workflow.PostAsync(request);
        Assert.True(replay.IsSuccess, replay.Error);
        Assert.True(replay.Value!.ExistingOutcome);
        Assert.Equal(posted.Value.CollectionId, replay.Value.CollectionId);
        Assert.Equal(409, (await workflow.PostAsync(request with {Amount = 66m})).StatusCode);

        var line = Assert.Single(await db.CollectionLines.AsNoTracking().ToListAsync());
        Assert.Null(line.SourceKind);
        Assert.Null(line.SourceId);
        Assert.Contains("Walkway obstruction", line.CalculationSnapshot);
        Assert.Contains("Inspector entry 22", line.CalculationSnapshot);
        Assert.Contains("Manual", line.CalculationSnapshot);
        Assert.Single(await db.Collections.AsNoTracking().ToListAsync());
        Assert.Single(await db.PostingOperations.AsNoTracking().ToListAsync());
        Assert.Empty(await db.FineAssessments.ToListAsync());
        Assert.Empty(await db.CollectionAllocations.ToListAsync());
        Assert.Equal(65.25m, await OfficialFinesAsync(db, s));

        var penalties = new PenaltyDefinitionWorkflow(db, new Actor(s.TenantId, id:s.ActorId),
            new Tenant(s.TenantId), new Clock());
        var row = Assert.Single((await penalties.GetRegisterAsync(Today, Today)).Value!);
        Assert.Equal(PenaltyRegisterKinds.Direct, row.Kind);
        Assert.Equal("Walkway obstruction", row.Reason);
        Assert.Equal("Walk-in payor", row.PayerName);
        Assert.Equal(65.25m, row.EffectiveAmount);
        var summary = (await penalties.GetSummaryAsync(Today, Today)).Value!;
        Assert.Equal(65.25m, summary.DirectCollected);
        var activity = Assert.Single(await new EEMOCantilanSDS.Infrastructure.Repositories.CollectionActivityReader(db)
            .GetAsync(s.TenantId, Today, Today));
        Assert.Equal(posted.Value.ReferenceCode, activity.ReferenceCode);
        Assert.Equal(65.25m, activity.NetAmount);
    }

    [SkippableFact]
    public async Task Manual_direct_fine_rejects_invalid_facts_and_missing_receipt_policy_without_cash()
    {
        var s = await ManualDirectSeed(receiptPolicy:false);
        await using var db = database.CreateContext(s.TenantId);
        var workflow = Direct(db, s);
        var request = new DirectFineCollectionRequest(Guid.NewGuid(), Guid.Empty, Today, 100m, "Actual incident");
        Assert.False((await workflow.PostAsync(request)).IsSuccess);
        Assert.Empty(await db.Collections.ToListAsync());
        Assert.Empty(await db.PostingOperations.ToListAsync());

        db.RevenueClassificationPolicies.Add(RevenueClassificationPolicy.Create(
            (await db.RevenueClassifications.SingleAsync()).Id, Today.AddDays(-1),
            "Fines", RevenueInstrumentType.OfficialReceipt, s.TenantId));
        await db.SaveChangesAsync();
        foreach (var invalid in new[] { 0m, -5m, 1.001m })
            Assert.False((await workflow.PostAsync(request with { ClientOperationId=Guid.NewGuid(), Amount=invalid })).IsSuccess);
        Assert.False((await workflow.PostAsync(request with { ClientOperationId=Guid.NewGuid(), Reason=null })).IsSuccess);
        Assert.False((await workflow.PostAsync(request with { ClientOperationId=Guid.NewGuid(), AuthorityId=Guid.NewGuid() })).IsSuccess);
        Assert.False((await workflow.PostAsync(request with { ClientOperationId=Guid.NewGuid(), BusinessDate=Today.AddDays(-1) })).IsSuccess);
        Assert.Equal(403, (await Direct(db, s, "Collector").PostAsync(request)).StatusCode);
        Assert.Empty(await db.Collections.ToListAsync());
        Assert.Empty(await db.CollectionLines.ToListAsync());
        Assert.Empty(await db.PostingOperations.ToListAsync());
    }

    [SkippableFact] public async Task DirectFineCreatesOnlyCanonicalCashAndFinesRevenue()
    {
        var (s,a)=await DirectSeed(); await using var db=database.CreateContext(s.TenantId); var w=Direct(db,s);
        Assert.Equal(0m,await OfficialFinesAsync(db,s));
        var categories = (await w.OptionsAsync()).Value!.Categories;
        Assert.Equal(2, categories.Count);
        Assert.Equal(Guid.Empty, categories[0].AuthorityId);
        Assert.Equal(a, categories[1].AuthorityId);
        var r=await w.PostAsync(DirectRequest(a)); Assert.True(r.IsSuccess,r.Error);
        Assert.NotEmpty(r.Value!.ReferenceCode); Assert.Equal(100m,r.Value.Amount);
        var line=await db.CollectionLines.SingleAsync(); Assert.Equal(CollectionSourceKind.DirectFineCollection,line.SourceKind); Assert.Equal(a,line.SourceId);
        Assert.Empty(await db.CollectionAllocations.ToListAsync()); Assert.Empty(await db.FineAssessments.ToListAsync());
        Assert.Empty(await db.Contracts.ToListAsync()); Assert.Empty(await db.ObligationAccounts.ToListAsync());
        Assert.Empty(await db.PaymentRecords.ToListAsync()); Assert.Null((await db.Collections.SingleAsync()).PayerName);
        Assert.Equal(100m,await OfficialFinesAsync(db,s));
        var position=await new RemittanceWorkflow(db,new Actor(s.TenantId,id:s.ActorId),new Tenant(s.TenantId)).GetPositionAsync(Today,Today);
        // Existing remittance is collector-owned; Head cash must not impersonate a collector.
        Assert.True(position.IsSuccess,position.Error); Assert.Equal(0m,position.Value!.Collected); Assert.Equal(0m,position.Value.Unremitted);
        Assert.Null((await db.Collections.SingleAsync()).CollectorId); Assert.Equal("SuperAdmin",(await db.Collections.SingleAsync()).ActorRole);
        var row=Assert.Single((await new PenaltyDefinitionWorkflow(db,new Actor(s.TenantId,id:s.ActorId),new Tenant(s.TenantId),new Clock()).GetRegisterAsync(Today,Today)).Value!);
        Assert.Equal("Direct fine collection",row.Origin); Assert.Equal(100m,row.EffectiveAmount);
        var activity=Assert.Single(await new EEMOCantilanSDS.Infrastructure.Repositories.CollectionActivityReader(db).GetAsync(s.TenantId,Today,Today));
        Assert.Equal(r.Value.CollectionId,activity.CollectionId); Assert.Equal(r.Value.ReferenceCode,activity.ReferenceCode); Assert.Equal(100m,activity.NetAmount);
        var report=await new CollectionsReportWorkflow(db,new Actor(s.TenantId,id:s.ActorId),new Tenant(s.TenantId)).GetRegisterAsync(Today,Today,null,null,null);
        Assert.True(report.IsSuccess,report.Error); Assert.Equal(100m,report.Value!.Net);
    }
    [SkippableTheory] [InlineData("Admin")] [InlineData("Collector")] [InlineData("Payor")]
    public async Task DirectFineRejectsNonHeadAtServer(string role)
    {
        var (s,a)=await DirectSeed(); await using var db=database.CreateContext(s.TenantId);
        Assert.Equal(403,(await Direct(db,s,role).PostAsync(DirectRequest(a))).StatusCode);
        Assert.Equal(403,(await Direct(db,s,role).OptionsAsync()).StatusCode);
        Assert.Empty(await db.Collections.ToListAsync());
    }
    [SkippableTheory] [InlineData(0)] [InlineData(-1)] [InlineData(100.001)] [InlineData(501)]
    public async Task DirectFineAmountRejectedWithoutCash(decimal amount)
    {
        var (s,a)=await DirectSeed(); await using var db=database.CreateContext(s.TenantId);
        Assert.False((await Direct(db,s).PostAsync(DirectRequest(a) with {Amount=amount})).IsSuccess);
        Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.PostingOperations.ToListAsync());
    }
    [SkippableFact] public async Task DirectFineRequiresOwnAuthorityAndEvidenceNotAssessmentPolicy()
    {
        var (s,a)=await DirectSeed(payer:true,evidence:true); await using var db=database.CreateContext(s.TenantId); var w=Direct(db,s);
        var request=DirectRequest(a); Assert.False((await w.PostAsync(request)).IsSuccess);
        Assert.False((await w.PostAsync(request with {AuthorityId=Guid.NewGuid()})).IsSuccess);
        Assert.False((await w.PostAsync(request with {BusinessDate=Today.AddDays(-1)})).IsSuccess);
        Assert.True((await w.PostAsync(request with {Reason="Actual test incident",SupportingReference="TEST CASE",PayerParticulars="Actual test receipt name"})).IsSuccess);
    }
    [SkippableFact] public async Task DirectFineCrossTenantAndAuthorityForeignKeyFailClosed()
    {
        var (s,a)=await DirectSeed(); await using var db=database.CreateContext(s.TenantId);
        var wrong=new DirectFineCollectionWorkflow(db,new Actor(Guid.NewGuid()),new Tenant(s.TenantId),new Clock());
        Assert.Equal(403,(await wrong.PostAsync(DirectRequest(a))).StatusCode);
        await using var foreign=database.CreateContext(Guid.NewGuid()); Assert.Empty(await foreign.DirectFineAuthorities.ToListAsync());
        Assert.False((await Direct(foreign,s with {TenantId=Guid.NewGuid()}).PostAsync(DirectRequest(a))).IsSuccess);
        var bad=DirectFineAuthority.Record(s.TenantId,Guid.NewGuid(),"Test","Test",Today,null,false,false,false,false,s.ActorId,new Clock().UtcNow);
        db.DirectFineAuthorities.Add(bad); await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }
    [SkippableFact] public async Task DirectFineReplayAndChangedIntentCannotDoublePost()
    {
        var (s,a)=await DirectSeed(); await using var db=database.CreateContext(s.TenantId); var w=Direct(db,s); var request=DirectRequest(a);
        var first=await w.PostAsync(request); Assert.True(first.IsSuccess,first.Error);
        var replay=await w.PostAsync(request with {Amount=100.00m}); Assert.True(replay.IsSuccess,replay.Error);
        Assert.Equal(first.Value!.CollectionId,replay.Value!.CollectionId); Assert.True(replay.Value.ExistingOutcome);
        Assert.Equal(409,(await w.PostAsync(request with {Amount=101m})).StatusCode);
        Assert.Single(await db.Collections.ToListAsync()); Assert.Single(await db.PostingOperations.ToListAsync());
    }
    [SkippableFact] public async Task DirectFineConcurrentRetryHasOneCashRecord()
    {
        var (s,a)=await DirectSeed(); var request=DirectRequest(a);
        await using var db1=database.CreateContext(s.TenantId); await using var db2=database.CreateContext(s.TenantId);
        var results=await Task.WhenAll(Direct(db1,s).PostAsync(request),Direct(db2,s).PostAsync(request));
        Assert.Contains(results,r=>r.IsSuccess);
        await using var db=database.CreateContext(s.TenantId); Assert.Single(await db.Collections.ToListAsync()); Assert.Single(await db.PostingOperations.ToListAsync());
        var recovered=await Direct(db,s).PostAsync(request); Assert.True(recovered.IsSuccess,recovered.Error);
    }
    [SkippableFact] public async Task DirectFineUncappedAndFixedRulesRemainGoverned()
    {
        var (s,a)=await DirectSeed(ceiling:null); await using(var db=database.CreateContext(s.TenantId)) {
            var options = (await Direct(db,s).OptionsAsync()).Value!.Categories;
            Assert.Equal(Guid.Empty, Assert.Single(options).AuthorityId);
            Assert.False((await Direct(db,s).PostAsync(DirectRequest(a))).IsSuccess); }
        (s,a)=await DirectSeed(ceiling:null,uncapped:true); await using(var db=database.CreateContext(s.TenantId)) Assert.True((await Direct(db,s).PostAsync(DirectRequest(a))).IsSuccess);
        (s,a)=await DirectSeed(basis:GovernedServiceBasis.FixedAmount); await using(var db=database.CreateContext(s.TenantId)) {
            Assert.False((await Direct(db,s).PostAsync(DirectRequest(a) with {Amount=101m})).IsSuccess); Assert.True((await Direct(db,s).PostAsync(DirectRequest(a))).IsSuccess); }
    }
    [SkippableFact] public async Task DirectFineRetirementStopsNewCashButReplayRecoversExisting()
    {
        var (s,a)=await DirectSeed(); await using var db=database.CreateContext(s.TenantId); var request=DirectRequest(a); var w=Direct(db,s);
        var first=await w.PostAsync(request); Assert.True(first.IsSuccess,first.Error);
        db.PenaltyDefinitions.Add(PenaltyDefinition.Create(s.TenantId,"TEST_DIRECT_FINE",Today,"Retired",null,GovernedServiceBasis.FixedAmount,100m,null,false,"fixture")); await db.SaveChangesAsync();
        var options = (await w.OptionsAsync()).Value!.Categories;
        Assert.Equal(Guid.Empty, Assert.Single(options).AuthorityId);
        Assert.False((await w.PostAsync(request with {ClientOperationId=Guid.NewGuid()})).IsSuccess);
        Assert.Equal(first.Value!.CollectionId,(await w.PostAsync(request)).Value!.CollectionId);
    }
    [SkippableFact] public async Task DirectFineReversalRemovesEffectiveIncomeAndKeepsSrc()
    {
        var (s,a)=await DirectSeed(); await using var db=database.CreateContext(s.TenantId); var cash=await Direct(db,s).PostAsync(DirectRequest(a)); Assert.True(cash.IsSuccess,cash.Error);
        var collection=await db.Collections.SingleAsync(); var line=await db.CollectionLines.SingleAsync();
        db.CollectionCorrections.Add(CollectionCorrection.Record(s.TenantId,collection.Id,null,null,null,CollectionCorrectionType.Reversal,Today,DateTime.UtcNow,-100m,"TEST correction evidence","head","Head",
            [new CollectionCorrectionLineDraft(line.Id,-100m)])); await db.SaveChangesAsync();
        Assert.Equal(0m,await OfficialFinesAsync(db,s)); Assert.Equal(cash.Value!.ReferenceCode,collection.ReferenceCode);
        var register=await new PenaltyDefinitionWorkflow(db,new Actor(s.TenantId,id:s.ActorId),new Tenant(s.TenantId),new Clock()).GetRegisterAsync(Today,Today);
        Assert.Equal(0m,Assert.Single(register.Value!).EffectiveAmount); Assert.Empty(await db.FineAssessments.ToListAsync());
        var position=await new RemittanceWorkflow(db,new Actor(s.TenantId,id:s.ActorId),new Tenant(s.TenantId)).GetPositionAsync(Today,Today);
        Assert.True(position.IsSuccess,position.Error); Assert.Equal(0m,position.Value!.Collected);
    }
    [SkippableFact] public async Task WorkspaceSummaryKeepsDirectCashAssessmentsAndSettlementsDistinct()
    {
        var (s,fine)=await SettlementSeedAsync(); await using var db=database.CreateContext(s.TenantId);
        var definition=PenaltyDefinition.Create(s.TenantId,"TEST_DIRECT_FINE",Today.AddYears(-1),"Test authorized fine",null,GovernedServiceBasis.DirectApprovedAmount,null,500m,true,"fixture");
        var authority=DirectFineAuthority.Record(s.TenantId,definition.Id,"ISOLATED TEST governing authority","ISOLATED TEST approval: no registration required",Today.AddYears(-1),null,false,false,false,false,s.ActorId,new Clock().UtcNow);
        db.PenaltyDefinitions.Add(definition); db.DirectFineAuthorities.Add(authority); await db.SaveChangesAsync();
        var penalties=new PenaltyDefinitionWorkflow(db,new Actor(s.TenantId,id:s.ActorId),new Tenant(s.TenantId),new Clock());
        async Task<PenaltyWorkspaceSummaryDto> Summary() { var r=await penalties.GetSummaryAsync(Today,Today); Assert.True(r.IsSuccess,r.Error); return r.Value!; }

        // An unpaid assessment is a liability only: nothing collected.
        var start=await Summary(); Assert.Equal((0m,0m,100m,1),(start.Collected,start.DirectCollected,start.OutstandingAssessed,start.OutstandingAssessments));

        // Direct cash never pays the unrelated assessment and is the only thing counted as direct.
        var direct=await Direct(db,s).PostAsync(DirectRequest(authority.Id) with {Reason="Obstructing the walkway"}); Assert.True(direct.IsSuccess,direct.Error);
        var afterDirect=await Summary(); Assert.Equal((100m,100m,100m,1),(afterDirect.Collected,afterDirect.DirectCollected,afterDirect.OutstandingAssessed,afterDirect.OutstandingAssessments));

        // Settling the assessment is collected cash but not direct cash, and clears the liability.
        var composer=Composer(db,s); var draft=await FineDraftAsync(composer,fine);
        Assert.True((await composer.PostAsync(draft.DraftId,new(draft.Revision,Guid.NewGuid()))).IsSuccess);
        db.ChangeTracker.Clear();
        var afterSettlement=await Summary(); Assert.Equal((200m,100m,0m,0),(afterSettlement.Collected,afterSettlement.DirectCollected,afterSettlement.OutstandingAssessed,afterSettlement.OutstandingAssessments));

        var rows=(await penalties.GetRegisterAsync(Today,Today)).Value!;
        var directRow=Assert.Single(rows,r=>r.Kind==PenaltyRegisterKinds.Direct);
        Assert.Null(directRow.Account); Assert.Null(directRow.Facility); Assert.Equal("Obstructing the walkway",directRow.Reason); Assert.Equal("Direct fine collection",directRow.Origin);
        var assessedRow=Assert.Single(rows,r=>r.Kind==PenaltyRegisterKinds.Assessed);
        Assert.False(string.IsNullOrWhiteSpace(assessedRow.Account)); Assert.False(string.IsNullOrWhiteSpace(assessedRow.Facility)); Assert.False(string.IsNullOrWhiteSpace(assessedRow.Reason));

        // A reversal removes only that direct cash from the headline figures.
        var collection=await db.Collections.SingleAsync(c=>c.Id==direct.Value!.CollectionId); var line=await db.CollectionLines.SingleAsync(l=>l.CollectionId==collection.Id);
        db.CollectionCorrections.Add(CollectionCorrection.Record(s.TenantId,collection.Id,null,null,null,CollectionCorrectionType.Reversal,Today,DateTime.UtcNow,-100m,"TEST correction evidence","head","Head",
            [new CollectionCorrectionLineDraft(line.Id,-100m)])); await db.SaveChangesAsync();
        var reversed=await Summary(); Assert.Equal((100m,0m,0m),(reversed.Collected,reversed.DirectCollected,reversed.OutstandingAssessed));
    }
    [SkippableFact] public async Task DirectFineCashFailureRollsBackAndSameIntentCanRetry()
    {
        var (s,a)=await DirectSeed(); await using var db=database.CreateContext(s.TenantId); var w=Direct(db,s); var request=DirectRequest(a);
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION reject_direct_test_cash() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'forced cash failure' USING ERRCODE='23514'; END $$; CREATE TRIGGER reject_direct_test_cash BEFORE INSERT ON \"Collections\" FOR EACH ROW EXECUTE FUNCTION reject_direct_test_cash();");
        try {
            Assert.False((await w.PostAsync(request)).IsSuccess); db.ChangeTracker.Clear();
            Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.CollectionLines.ToListAsync()); Assert.Empty(await db.PostingOperations.ToListAsync());
        } finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_direct_test_cash ON \"Collections\"; DROP FUNCTION reject_direct_test_cash();"); }
        var retry=await w.PostAsync(request); Assert.True(retry.IsSuccess,retry.Error); Assert.Single(await db.Collections.ToListAsync());
    }
}
