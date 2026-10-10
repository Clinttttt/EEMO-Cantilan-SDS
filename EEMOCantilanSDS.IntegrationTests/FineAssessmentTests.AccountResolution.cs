using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class FineAssessmentTests
{
    [SkippableTheory]
    [InlineData(FacilityCode.NPM)] [InlineData(FacilityCode.NCC)] [InlineData(FacilityCode.TCC)]
    [InlineData(FacilityCode.BBQ)] [InlineData(FacilityCode.ICE)]
    public async Task FacilityReviewResolvesExactCurrentAndHistoricalLiabilityWithoutCash(FacilityCode code)
    {
        var seed=await SeedAsync(FineAccountKind.FacilityContract,facilityCode:code);
        await using var db=database.CreateContext(seed.TenantId);
        var old=await db.Contracts.SingleAsync(x=>x.Id==seed.Account.AccountId);
        var current=Contract.Create(old.StallId,"Current holder","Current signed name",new(2026,1,1),3,950m,createdBy:"test");
        db.Contracts.Add(current); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var workflow=Workflow(db,seed);
        var resolved=await workflow.ResolveFacilityAccountAsync(old.StallId,code,Today);
        Assert.True(resolved.IsSuccess,resolved.Error); Assert.Equal(current.Id,resolved.Value!.Account.AccountId);
        var historical=await workflow.ResolveFacilityAccountAsync(old.StallId,code,Today,old.Id);
        Assert.True(historical.IsSuccess,historical.Error); Assert.Equal(old.Id,historical.Value!.Account.AccountId);
        Assert.Equal("Historical holder",historical.Value.Occupant); Assert.True(historical.Value.Closed);
        Assert.False((await workflow.ResolveFacilityAccountAsync(old.StallId,code,new(2025,12,1))).IsSuccess); // Stale queue must not attach the replacement.
        Assert.False((await workflow.ResolveFacilityAccountAsync(old.StallId,code==FacilityCode.NPM ? FacilityCode.ICE : FacilityCode.NPM,Today)).IsSuccess);
        Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.FineAssessments.ToListAsync()); Assert.Empty(await db.PaymentRecords.ToListAsync());
        var request=Request(seed) with {Account=resolved.Value.Account,ViolationDate=Today,IncidentReference="CURRENT-REVIEW"};
        Assert.True((await workflow.CreateAsync(request)).IsSuccess);
        Assert.Single((await workflow.ReadAsync(FineAccountKind.FacilityContract,current.Id)).Value!);
        Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.CollectionAllocations.ToListAsync());
    }

    [SkippableFact]
    public async Task FacilityReviewRejectsAmbiguityWrongTenantAndOtherStallContract()
    {
        var seed=await SeedAsync(FineAccountKind.FacilityContract,facilityCode:FacilityCode.NPM);
        await using var db=database.CreateContext(seed.TenantId);
        var old=await db.Contracts.SingleAsync(x=>x.Id==seed.Account.AccountId);
        var current=Contract.Create(old.StallId,"Holder A","Holder A",new(2026,1,1),3,900m,createdBy:"test");
        var competing=Contract.Create(old.StallId,"Holder B","Holder B",new(2026,2,1),3,900m,createdBy:"test");
        db.Contracts.AddRange(current,competing); await db.SaveChangesAsync();
        Assert.Equal(409,(await Workflow(db,seed).ResolveFacilityAccountAsync(old.StallId,FacilityCode.NPM,Today)).StatusCode);
        Assert.False((await Workflow(db,seed).ResolveFacilityAccountAsync(Guid.NewGuid(),FacilityCode.NPM,Today,current.Id)).IsSuccess);
        await using var foreign=database.CreateContext(Guid.NewGuid());
        Assert.False((await Workflow(foreign,seed).ResolveFacilityAccountAsync(old.StallId,FacilityCode.NPM,Today,current.Id)).IsSuccess);
        Assert.Equal(403,(await Workflow(db,seed,"Collector").ResolveFacilityAccountAsync(old.StallId,FacilityCode.NPM,Today,current.Id)).StatusCode);
        Assert.True((await Workflow(db,seed,"Admin").ResolveFacilityAccountAsync(old.StallId,FacilityCode.NPM,Today,old.Id)).IsSuccess);
    }

    [SkippableFact]
    public async Task SameStallNumberAcrossMarketAreasCannotInterchangeFineAccounts()
    {
        var seed=await SeedAsync(FineAccountKind.FacilityContract,facilityCode:FacilityCode.NPM);
        await using var db=database.CreateContext(seed.TenantId);
        var old=await db.Contracts.SingleAsync(x=>x.Id==seed.Account.AccountId);
        var facilityId=(await db.Stalls.SingleAsync(x=>x.Id==old.StallId)).FacilityId;
        var ids=new List<Guid>();
        foreach(var area in new[]{MarketSection.FishSection,MarketSection.MeatSection,MarketSection.VegetableArea})
        {
            var stall=Stall.Create(facilityId,"7",900m,ApplicableFees.BaseRental,section:area,municipalityId:seed.TenantId);
            var contract=Contract.Create(stall.Id,"Same name","Same name",new(2026,1,1),3,900m,createdBy:"test");
            db.Stalls.Add(stall); db.Contracts.Add(contract); await db.SaveChangesAsync();
            var result=await Workflow(db,seed).ResolveFacilityAccountAsync(stall.Id,FacilityCode.NPM,Today);
            Assert.Equal(contract.Id,result.Value!.Account.AccountId); ids.Add(result.Value.Account.AccountId);
            Assert.False((await Workflow(db,seed).ResolveFacilityAccountAsync(old.StallId,FacilityCode.NPM,Today,contract.Id)).IsSuccess);
        }
        Assert.Equal(3,ids.Distinct().Count()); Assert.Empty(await db.Collections.ToListAsync());
    }
    [SkippableFact]
    public async Task MissingCurrentAndInvalidQueueDatesFailClosedWhileExplicitHistoryIsReadable()
    {
        var seed=await SeedAsync(FineAccountKind.FacilityContract,facilityCode:FacilityCode.NPM);
        await using var db=database.CreateContext(seed.TenantId);
        var old=await db.Contracts.SingleAsync(x=>x.Id==seed.Account.AccountId);
        var workflow=Workflow(db,seed);
        Assert.Equal(409,(await workflow.ResolveFacilityAccountAsync(old.StallId,FacilityCode.NPM,Today)).StatusCode);
        Assert.False((await workflow.ResolveFacilityAccountAsync(old.StallId,FacilityCode.NPM,Today.AddDays(1),old.Id)).IsSuccess);
        Assert.False((await workflow.ResolveFacilityAccountAsync(old.StallId,FacilityCode.NPM,default,old.Id)).IsSuccess);
        Assert.False((await workflow.ResolveFacilityAccountAsync(old.StallId,FacilityCode.NPM,Today,Guid.NewGuid())).IsSuccess);
        Assert.True((await workflow.ResolveFacilityAccountAsync(old.StallId,FacilityCode.NPM,Today,old.Id)).IsSuccess);
        Assert.Empty(await db.Collections.ToListAsync()); Assert.Empty(await db.FineAssessments.ToListAsync());
    }
}
