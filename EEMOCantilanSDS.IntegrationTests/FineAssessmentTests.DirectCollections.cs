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

    [SkippableFact] public async Task DirectFineCreatesOnlyCanonicalCashAndFinesRevenue()
    {
        var (s,a)=await DirectSeed(); await using var db=database.CreateContext(s.TenantId); var w=Direct(db,s);
        Assert.Equal(0m,await OfficialFinesAsync(db,s));
        Assert.Single((await w.OptionsAsync()).Value!.Categories);
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
            Assert.Empty((await Direct(db,s).OptionsAsync()).Value!.Categories); Assert.False((await Direct(db,s).PostAsync(DirectRequest(a))).IsSuccess); }
        (s,a)=await DirectSeed(ceiling:null,uncapped:true); await using(var db=database.CreateContext(s.TenantId)) Assert.True((await Direct(db,s).PostAsync(DirectRequest(a))).IsSuccess);
        (s,a)=await DirectSeed(basis:GovernedServiceBasis.FixedAmount); await using(var db=database.CreateContext(s.TenantId)) {
            Assert.False((await Direct(db,s).PostAsync(DirectRequest(a) with {Amount=101m})).IsSuccess); Assert.True((await Direct(db,s).PostAsync(DirectRequest(a))).IsSuccess); }
    }
    [SkippableFact] public async Task DirectFineRetirementStopsNewCashButReplayRecoversExisting()
    {
        var (s,a)=await DirectSeed(); await using var db=database.CreateContext(s.TenantId); var request=DirectRequest(a); var w=Direct(db,s);
        var first=await w.PostAsync(request); Assert.True(first.IsSuccess,first.Error);
        db.PenaltyDefinitions.Add(PenaltyDefinition.Create(s.TenantId,"TEST_DIRECT_FINE",Today,"Retired",null,GovernedServiceBasis.FixedAmount,100m,null,false,"fixture")); await db.SaveChangesAsync();
        Assert.Empty((await w.OptionsAsync()).Value!.Categories); Assert.False((await w.PostAsync(request with {ClientOperationId=Guid.NewGuid()})).IsSuccess);
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
