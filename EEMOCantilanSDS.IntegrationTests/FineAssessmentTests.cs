using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class FineAssessmentTests(PostgresFixture database)
{
    private static readonly DateOnly Today = new(2026, 10, 10);
    private sealed class Actor(Guid tenant, string role = "SuperAdmin", Guid? id = null) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId { get; } = id ?? Guid.NewGuid(); public Guid? MunicipalityId => tenant;
        public string? Username => "fine-test-head"; public string? Role => role; public Guid? CollectorId => null;
        public string? MunicipalityCode => "TEST"; public AdminUserDto? GetCurrentUser() => null;
    }
    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor { public Guid MunicipalityId => id; public void Set(Guid value) { } }
    private sealed class Clock : IClock { public DateOnly PhilippineToday => Today; public DateTime UtcNow => new(2026,10,10,2,0,0,DateTimeKind.Utc); public DateTime PhilippineNow => new(2026,10,10,10,0,0); }
    private sealed record Seed(Guid TenantId, FineAccountRef Account, Guid Policy, Guid ActorId);
    private FineAssessmentWorkflow Workflow(AppDbContext context, Seed seed, string role = "SuperAdmin") =>
        new(context,new Actor(seed.TenantId,role,seed.ActorId),new Tenant(seed.TenantId),new Clock());
    private async Task<Seed> SeedAsync(FineAccountKind kind = FineAccountKind.Kanmanggay, bool closed = false,
        decimal? ceiling = 500m, bool uncapped = false, GovernedServiceBasis basis = GovernedServiceBasis.DirectApprovedAmount,
        FacilityCode facilityCode = FacilityCode.ICE)
    {
        Skip.IfNot(database.Available,database.UnavailableReason ?? "PostgreSQL unavailable");
        await database.ResetAsync();
        var municipality=Municipality.Create("FINE", "Fine test", "Test",MunicipalityStatus.Active);
        var actor=Guid.NewGuid();
        await using var context=database.CreateContext(municipality.Id);
        context.Municipalities.Add(municipality);
        Guid account;
        if(kind == FineAccountKind.FacilityContract) {
            var facility=Facility.Create(facilityCode,"Test facility","TEST",municipalityId:municipality.Id);
            var stall=Stall.Create(facility.Id,"1",900m,ApplicableFees.BaseRental,municipalityId:municipality.Id);
            var contract=Contract.Create(stall.Id,"Historical holder","Signed holder",new(2023,1,1),3,900m,createdBy:"test");
            context.Facilities.Add(facility); context.Stalls.Add(stall); context.Contracts.Add(contract); account=contract.Id;
        } else {
            var obligationKind=kind switch {FineAccountKind.Kanmanggay=>ObligationKind.KanmanggaySpaceRental,
                FineAccountKind.VegetableFruitMonthly=>ObligationKind.VegetableFruitSpaceRental,_=>ObligationKind.FiestaArawLotRental};
            var eventDate=kind==FineAccountKind.FiestaAraw ? Today.AddDays(-10) : (DateOnly?)null;
            var a=ObligationAccount.Create(municipality.Id,obligationKind,Guid.Empty,null,"Space 1",
                kind==FineAccountKind.FiestaAraw ? LotRentalEvent.Fiesta : null,eventDate,new(2023,1,1),"head",actualOccupant:"Liable holder");
            if(closed) a.Close(new(2026,9,30)); context.ObligationAccounts.Add(a); account=a.Id;
        }
        var definition=PenaltyDefinition.Create(municipality.Id,"APPROVED_FINE",new(2023,1,1),"Approved incident",null,basis,
            basis==GovernedServiceBasis.FixedAmount ? 100m : null,basis==GovernedServiceBasis.DirectApprovedAmount ? ceiling : null,true,"head");
        var policy=FineAssessmentPolicy.Create(municipality.Id,definition.Id,kind,kind==FineAccountKind.FacilityContract ? facilityCode : null,
            "TEST governing authority",FinePenaltyDateBasis.AssessmentDate,uncapped,true,actor,new Clock().UtcNow);
        context.PenaltyDefinitions.Add(definition); context.FineAssessmentPolicies.Add(policy); await context.SaveChangesAsync();
        return new(municipality.Id,new(kind,account),policy.Id,actor);
    }
    private static CreateFineAssessmentRequest Request(Seed seed,string? incident="CASE-001") =>
        new(Guid.NewGuid(),seed.Account,seed.Policy,new(2026,9,15),incident,"Verified violation",100m);

    [SkippableTheory]
    [InlineData(FineAccountKind.Kanmanggay)] [InlineData(FineAccountKind.VegetableFruitMonthly)] [InlineData(FineAccountKind.FiestaAraw)]
    public async Task AssessmentPersistsAuditAndReadsWithoutCash(FineAccountKind kind)
    {
        var seed=await SeedAsync(kind); await using var context=database.CreateContext(seed.TenantId);
        var workflow=Workflow(context,seed); var request=Request(seed) with {ViolationDate=Today.AddDays(-10)};
        var result=await workflow.CreateAsync(request); Assert.True(result.IsSuccess,result.Error);
        context.ChangeTracker.Clear();
        var row=Assert.Single((await workflow.ReadAsync(kind,seed.Account.AccountId)).Value!);
        Assert.Equal(result.Value!.Id,row.Id); Assert.Equal(100m,row.Remaining); Assert.Equal(0m,row.Settled);
        Assert.Equal(Today,row.AssessmentDate); Assert.Equal(request.ViolationDate,row.ViolationDate);
        var audit=Assert.Single((await workflow.HistoryAsync(row.Id)).Value!); Assert.Equal("Created",audit.Kind);
        Assert.Equal(seed.ActorId,audit.ActorId); Assert.Equal(100m,audit.Resulting);
        Assert.Empty(await context.Collections.ToListAsync()); Assert.Empty(await context.CollectionLines.ToListAsync());
        Assert.Empty(await context.PostingOperations.ToListAsync()); Assert.Empty(await context.ObligationPeriods.ToListAsync());
        Assert.Empty(await context.CollectionRemittances.ToListAsync());
    }
    [SkippableTheory] [InlineData("Admin")] [InlineData("Collector")] [InlineData("Payor")]
    public async Task NonHeadCannotCreate(string role) {
        var seed=await SeedAsync(); await using var context=database.CreateContext(seed.TenantId);
        Assert.Equal(403,(await Workflow(context,seed,role).CreateAsync(Request(seed))).StatusCode);
        Assert.Empty(await context.FineAssessments.ToListAsync()); Assert.Empty(await context.FineAssessmentEvents.ToListAsync());
    }
    [SkippableFact] public async Task WrongTenantAndAccountKindFailClosed() {
        var seed=await SeedAsync(); await using var context=database.CreateContext(seed.TenantId);
        var wrong=new FineAssessmentWorkflow(context,new Actor(Guid.NewGuid()),new Tenant(seed.TenantId),new Clock());
        Assert.Equal(403,(await wrong.CreateAsync(Request(seed))).StatusCode);
        Assert.False((await Workflow(context,seed).CreateAsync(Request(seed) with {Account=new(FineAccountKind.VegetableFruitMonthly,seed.Account.AccountId)})).IsSuccess);
        await using var other=database.CreateContext(Guid.NewGuid()); Assert.Empty(await other.FineAssessments.ToListAsync());
    }
    [SkippableTheory] [InlineData(0)] [InlineData(-1)] [InlineData(100.001)] [InlineData(500.01)]
    public async Task InvalidMoneyOrCeilingCreatesNoAudit(decimal amount) {
        var seed=await SeedAsync(); await using var context=database.CreateContext(seed.TenantId);
        Assert.False((await Workflow(context,seed).CreateAsync(Request(seed) with {Amount=amount})).IsSuccess);
        Assert.Empty(await context.FineAssessmentEvents.ToListAsync());
    }
    [SkippableFact] public async Task UncappedRequiresExplicitPolicyAndFixedIsPreserved() {
        var seed=await SeedAsync(ceiling:null); await using(var context=database.CreateContext(seed.TenantId))
            Assert.False((await Workflow(context,seed).CreateAsync(Request(seed))).IsSuccess);
        seed=await SeedAsync(ceiling:null,uncapped:true); await using(var context=database.CreateContext(seed.TenantId))
            Assert.True((await Workflow(context,seed).CreateAsync(Request(seed))).IsSuccess);
        seed=await SeedAsync(basis:GovernedServiceBasis.FixedAmount); await using(var context=database.CreateContext(seed.TenantId)) {
            Assert.False((await Workflow(context,seed).CreateAsync(Request(seed) with {Amount=99m})).IsSuccess);
            Assert.True((await Workflow(context,seed).CreateAsync(Request(seed))).IsSuccess); }
    }
    [SkippableFact] public async Task ReplayConflictAndDistinctIncidentsAreNotMonthlyUniqueness() {
        var seed=await SeedAsync(); await using var context=database.CreateContext(seed.TenantId); var w=Workflow(context,seed); var request=Request(seed);
        var first=await w.CreateAsync(request); Assert.True(first.IsSuccess,first.Error);
        Assert.Equal(first.Value!.Id,(await w.CreateAsync(request)).Value!.Id);
        Assert.Equal(first.Value.Id,(await w.CreateAsync(request with {Amount=100.00m})).Value!.Id);
        Assert.False((await w.CreateAsync(request with {Amount=100.001m})).IsSuccess);
        Assert.Equal(409,(await w.CreateAsync(request with {Amount=101m})).StatusCode);
        Assert.Equal(409,(await w.CreateAsync(request with {ClientOperationId=Guid.NewGuid()})).StatusCode);
        Assert.True((await w.CreateAsync(Request(seed,"CASE-002"))).IsSuccess);
        Assert.Equal(2,await context.FineAssessments.CountAsync()); Assert.Equal(2,await context.FineAssessmentEvents.CountAsync());
    }
    [SkippableFact] public async Task UnreferencedIncidentsWarnAndRequireExplicitDistinctConfirmation() {
        var seed=await SeedAsync(); await using var context=database.CreateContext(seed.TenantId); var w=Workflow(context,seed);
        Assert.True((await w.CreateAsync(Request(seed,null))).IsSuccess);
        var request=Request(seed,null); Assert.Contains("POSSIBLE_DUPLICATE",(await w.CreateAsync(request)).Error);
        Assert.True((await w.CreateAsync(request with {ConfirmPossibleDuplicate=true})).IsSuccess);
    }
    [SkippableFact] public async Task ClosedAccountKeepsOriginalLiabilityAndDates() {
        var seed=await SeedAsync(closed:true); await using var context=database.CreateContext(seed.TenantId);
        Assert.True((await Workflow(context,seed).CreateAsync(Request(seed))).IsSuccess);
        var account=await context.ObligationAccounts.SingleAsync(); Assert.Equal(new DateOnly(2026,9,30),account.ActiveTo);
        Assert.False((await Workflow(context,seed).CreateAsync(Request(seed,"CASE-003") with {ViolationDate=Today})).IsSuccess);
        Assert.False((await Workflow(context,seed).CreateAsync(Request(seed,"CASE-004") with {ViolationDate=Today.AddDays(1)})).IsSuccess);
    }
    [SkippableTheory] [InlineData(FacilityCode.NPM)] [InlineData(FacilityCode.NCC)] [InlineData(FacilityCode.TCC)] [InlineData(FacilityCode.BBQ)] [InlineData(FacilityCode.ICE)]
    public async Task FacilityLiabilityUsesContractNotCurrentName(FacilityCode code) {
        var seed=await SeedAsync(FineAccountKind.FacilityContract,facilityCode:code); await using var context=database.CreateContext(seed.TenantId);
        var result=await Workflow(context,seed).CreateAsync(Request(seed) with {ViolationDate=new(2024,1,1)});
        Assert.True(result.IsSuccess,result.Error); Assert.Equal("Historical holder",result.Value!.Occupant);
        Assert.Equal(seed.Account,result.Value.Account); Assert.Empty(await context.Collections.ToListAsync());
    }
    [SkippableFact] public async Task UnsupportedPolicyAndStaleDefinitionRejected() {
        var seed=await SeedAsync(); await using var context=database.CreateContext(seed.TenantId); var w=Workflow(context,seed);
        Assert.False((await w.CreateAsync(Request(seed) with {PolicyId=Guid.NewGuid()})).IsSuccess);
        context.PenaltyDefinitions.Add(PenaltyDefinition.Create(seed.TenantId,"APPROVED_FINE",Today,"Updated approved incident",null,
            GovernedServiceBasis.FixedAmount,200m,null,true,"head")); await context.SaveChangesAsync();
        Assert.False((await w.CreateAsync(Request(seed))).IsSuccess);
    }
    [SkippableFact] public async Task GoverningPolicyMustBeHeadRecordedAndHistoricallyExplicit() {
        var seed=await SeedAsync(); await using var context=database.CreateContext(seed.TenantId);
        var version=await context.PenaltyDefinitions.SingleAsync();
        var request=new FinePolicyRequest(version.Id,FineAccountKind.Kanmanggay,null,"Actual approved reference",FinePenaltyDateBasis.ViolationDate,false,true);
        Assert.Equal(403,(await Workflow(context,seed,"Admin").RecordPolicyAsync(request)).StatusCode);
        Assert.Equal(403,(await Workflow(context,seed,"Collector").RecordPolicyAsync(request)).StatusCode);
        Assert.False((await Workflow(context,seed).RecordPolicyAsync(request with {GoverningReference=""})).IsSuccess);
        var policy=await Workflow(context,seed).RecordPolicyAsync(request); Assert.True(policy.IsSuccess,policy.Error);
        var result=await Workflow(context,seed).CreateAsync(Request(seed) with {PolicyId=policy.Value!.Id}); Assert.True(result.IsSuccess,result.Error);
        using var evidence=System.Text.Json.JsonDocument.Parse(Assert.Single((await Workflow(context,seed).HistoryAsync(result.Value!.Id)).Value!).Evidence);
        Assert.Equal((int)FinePenaltyDateBasis.ViolationDate,evidence.RootElement.GetProperty("DateBasis").GetInt32());
    }
    [SkippableFact] public async Task LegacyFineCollectionAndSrcRemainUntouched() {
        var seed=await SeedAsync(); await using var context=database.CreateContext(seed.TenantId);
        var version=await context.PenaltyDefinitions.SingleAsync();
        var classification=RevenueClassification.Create("PENALTIES_FINES",seed.TenantId);
        var policy=RevenueClassificationPolicy.Create(classification.Id,new(2023,1,1),"Fines",RevenueInstrumentType.OfficialReceipt,seed.TenantId);
        context.RevenueClassifications.Add(classification); context.RevenueClassificationPolicies.Add(policy);
        var collection=Collection.Post(new(2026,9,1),new Clock().UtcNow,"legacy-head","Head","SuperAdmin",
            [new CollectionLineDraft(classification,policy,100m,CollectionSourceKind.PenaltyDefinition,version.Id)],payerName:"Legacy holder",clientOperationId:Guid.NewGuid());
        context.Collections.Add(collection); await context.SaveChangesAsync();
        var originalSrc=collection.ReferenceCode; var line=Assert.Single(collection.Lines); var id=line.Id;
        var before=(await new PenaltyDefinitionWorkflow(context,new Actor(seed.TenantId),new Tenant(seed.TenantId),new Clock()).GetRegisterAsync(new(2026,9,1),Today)).Value!;
        Assert.True((await Workflow(context,seed).CreateAsync(Request(seed))).IsSuccess);
        context.ChangeTracker.Clear(); var after=await context.Collections.SingleAsync();
        Assert.Equal(originalSrc,after.ReferenceCode); Assert.Equal(100m,after.TotalAmount);
        Assert.Equal(id,(await context.CollectionLines.SingleAsync()).Id); Assert.Empty(await context.CollectionAllocations.ToListAsync());
        var register=(await new PenaltyDefinitionWorkflow(context,new Actor(seed.TenantId),new Tenant(seed.TenantId),new Clock()).GetRegisterAsync(new(2026,9,1),Today)).Value!;
        Assert.Equal(before,register); Assert.Empty(await context.CollectionRemittances.ToListAsync());
    }
    [SkippableFact] public async Task ConcurrentReplayAndIncidentUniquenessAreDatabaseEnforced() {
        var seed=await SeedAsync(); var request=Request(seed);
        async Task<Guid?> Create(CreateFineAssessmentRequest r) { await using var c=database.CreateContext(seed.TenantId); return (await Workflow(c,seed).CreateAsync(r)).Value?.Id; }
        var replay=await Task.WhenAll(Create(request),Create(request)); Assert.NotNull(replay[0]); Assert.Equal(replay[0],replay[1]);
        var incident=await Task.WhenAll(Create(Request(seed,"CASE-NEW")),Create(Request(seed,"CASE-NEW")));
        Assert.Single(incident.Where(x=>x.HasValue));
        await using var context=database.CreateContext(seed.TenantId); Assert.Equal(2,await context.FineAssessments.CountAsync()); Assert.Equal(2,await context.FineAssessmentEvents.CountAsync());
    }
}
