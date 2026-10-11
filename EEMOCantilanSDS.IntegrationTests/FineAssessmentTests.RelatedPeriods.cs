using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MediatR;
using EEMOCantilanSDS.Application.Queries.DailyCollections.GetSettleableNpmDays;
using EEMOCantilanSDS.Application.Dtos.DailyCollections;

namespace EEMOCantilanSDS.IntegrationTests;

public sealed partial class FineAssessmentTests
{
    [SkippableTheory]
    [InlineData(FineAccountKind.Kanmanggay)] [InlineData(FineAccountKind.VegetableFruitMonthly)]
    public async Task RelatedAssessedSpacePeriodIsFrozenAndNeverAddsRentOrCash(FineAccountKind kind)
    {
        var seed=await SeedAsync(kind);
        await using var db=database.CreateContext(seed.TenantId);
        var account=await db.ObligationAccounts.SingleAsync(x=>x.Id==seed.Account.AccountId);
        var rate=ObligationRate.Create(seed.TenantId,account.Id,new(2026,1,1),900,"test");
        var period=ObligationPeriod.Assess(account,rate,new(2026,9,1),"test");
        db.ObligationRates.Add(rate);db.ObligationPeriods.Add(period);await db.SaveChangesAsync();
        var workflow=Workflow(db,seed);
        var choices=await workflow.PeriodsAsync(seed.Account,2026);
        var choice=Assert.Single(choices.Value!);
        Assert.Equal(period.Id,choice.Reference.ObligationPeriodId);
        Assert.Equal("September 2026",choice.Label);
        var request=Request(seed) with {RelatedPeriod=choice.Reference};
        var saved=await workflow.CreateAsync(request);Assert.True(saved.IsSuccess,saved.Error);
        db.ChangeTracker.Clear();
        var read=Assert.Single((await workflow.ReadAsync(kind,account.Id)).Value!);
        Assert.Equal(choice,read.RelatedPeriod);
        Assert.Contains("September 2026",(await db.FineAssessmentEvents.SingleAsync()).Evidence);
        Assert.Equal(900,(await db.ObligationPeriods.SingleAsync()).AssessedAmount);
        Assert.Equal(1,(await db.ObligationPeriods.SingleAsync()).SettlementVersion);
        Assert.Empty(await db.Collections.ToListAsync());Assert.Empty(await db.CollectionAllocations.ToListAsync());
        Assert.Equal(saved.Value!.Id,(await workflow.CreateAsync(request)).Value!.Id);
        Assert.Equal(409,(await workflow.CreateAsync(request with {RelatedPeriod=null})).StatusCode);
        Assert.True((await workflow.CreateAsync(request with {ClientOperationId=Guid.NewGuid(),IncidentReference="CASE-002"})).IsSuccess);
        Assert.Equal(2,await db.FineAssessments.CountAsync()); // Same month, distinct incident.
    }

    [SkippableFact]
    public async Task UnassessedAndWrongAccountPeriodsAreRejectedWhileGeneralIncidentNeedsNoPeriod()
    {
        var seed=await SeedAsync();await using var db=database.CreateContext(seed.TenantId);
        var workflow=Workflow(db,seed);
        var fake=new FinePeriodRef(FineAffectedPeriodKind.ObligationPeriod,new(2026,9,1),new(2026,9,1),Guid.NewGuid());
        Assert.False((await workflow.CreateAsync(Request(seed) with {RelatedPeriod=fake})).IsSuccess);
        var other=ObligationAccount.Create(seed.TenantId,ObligationKind.KanmanggaySpaceRental,Guid.Empty,null,"Space 2",null,null,new(2023,1,1),"head",actualOccupant:"Other liable holder");
        var rate=ObligationRate.Create(seed.TenantId,other.Id,new(2026,1,1),900,"test");
        var otherPeriod=ObligationPeriod.Assess(other,rate,new(2026,9,1),"test");
        db.ObligationAccounts.Add(other);db.ObligationRates.Add(rate);db.ObligationPeriods.Add(otherPeriod);await db.SaveChangesAsync();
        Assert.False((await workflow.CreateAsync(Request(seed) with {RelatedPeriod=fake with {ObligationPeriodId=otherPeriod.Id}})).IsSuccess);
        Assert.Empty(await db.FineAssessments.ToListAsync());
        var general=await workflow.CreateAsync(Request(seed));Assert.True(general.IsSuccess,general.Error);Assert.Null(general.Value!.RelatedPeriod);
        Assert.Empty(await db.Collections.ToListAsync());
        await using var foreign=database.CreateContext(Guid.NewGuid());
        Assert.False((await Workflow(foreign,seed).PeriodsAsync(seed.Account,2026)).IsSuccess);
    }

    [SkippableFact]
    public async Task DatabaseRejectsIncompletePeriodEvidenceWithoutChangingSavedAssessment()
    {
        var seed=await SeedAsync();await using var db=database.CreateContext(seed.TenantId);
        var saved=await Workflow(db,seed).CreateAsync(Request(seed));Assert.True(saved.IsSuccess,saved.Error);
        // PostgreSQL CHECK must reject this rather than accepting SQL UNKNOWN for a null kind.
        var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"FineAssessments\" SET \"RelatedPeriodStart\" = {new DateOnly(2026,9,1)} WHERE \"Id\" = {saved.Value!.Id}"));
        Assert.Equal("23514",error.SqlState);
        db.ChangeTracker.Clear();Assert.Null((await db.FineAssessments.SingleAsync()).RelatedPeriodStart);
        Assert.Empty(await db.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task MonthlyFacilityPeriodUsesOriginalContractAndRejectsSuccessorMonth()
    {
        var seed=await SeedAsync(FineAccountKind.FacilityContract,facilityCode:FacilityCode.ICE);
        await using var db=database.CreateContext(seed.TenantId);
        var periods=await Workflow(db,seed).PeriodsAsync(seed.Account,2024);
        Assert.True(periods.IsSuccess,periods.Error);
        var september=Assert.Single(periods.Value!,p=>p.Reference.Start==new DateOnly(2024,9,1));
        Assert.Equal(FineAffectedPeriodKind.FacilityMonth,september.Reference.Kind);
        var request=Request(seed) with {ViolationDate=new(2024,9,12),RelatedPeriod=september.Reference};
        var saved=await Workflow(db,seed).CreateAsync(request);Assert.True(saved.IsSuccess,saved.Error);
        Assert.False((await Workflow(db,seed).CreateAsync(request with {ClientOperationId=Guid.NewGuid(),IncidentReference="OTHER",RelatedPeriod=september.Reference with {Start=new(2026,9,1),End=new(2026,9,30)}})).IsSuccess);
        Assert.Empty(await db.PaymentRecords.ToListAsync());Assert.Empty(await db.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task RecordedPaidNpmDayCanBeRelatedWithoutInventingMonthlyDebt()
    {
        var seed=await SeedAsync(FineAccountKind.FacilityContract,facilityCode:FacilityCode.NPM);
        await using var db=database.CreateContext(seed.TenantId);
        var contract=await db.Contracts.SingleAsync(x=>x.Id==seed.Account.AccountId);
        var day=DailyCollection.Create(contract.StallId,new(2024,9,12),"test",30);
        day.MarkPaid("TEST-OR",null);db.DailyCollections.Add(day);await db.SaveChangesAsync();
        var workflow=Workflow(db,seed);
        var choice=Assert.Single((await workflow.PeriodsAsync(seed.Account,2024)).Value!);
        Assert.Equal(FineAffectedPeriodKind.NpmDay,choice.Reference.Kind);
        var saved=await workflow.CreateAsync(Request(seed) with {ViolationDate=day.CollectionDate,RelatedPeriod=choice.Reference});
        Assert.True(saved.IsSuccess,saved.Error);
        Assert.Equal(choice,saved.Value!.RelatedPeriod);
        Assert.Equal(30,(await db.DailyCollections.SingleAsync()).DailyFee);
        Assert.Empty(await db.PaymentRecords.ToListAsync());Assert.Empty(await db.Collections.ToListAsync());
    }

    [SkippableFact]
    public async Task NpmEligibleDayReadReusesSourceCalendarAndExcludesExcusedDays()
    {
        var seed=await SeedAsync(FineAccountKind.FacilityContract,facilityCode:FacilityCode.NPM);
        await using var db=database.CreateContext(seed.TenantId);
        var contract=await db.Contracts.SingleAsync(x=>x.Id==seed.Account.AccountId);
        var excused=DailyCollection.Create(contract.StallId,new(2024,9,12),"test",30);
        excused.MarkAbsent();db.DailyCollections.Add(excused);await db.SaveChangesAsync();
        var rate=new EEMOCantilanSDS.Infrastructure.Fees.FeeRateResolver(db);
        var handler=new GetSettleableNpmDaysQueryHandler(new EEMOCantilanSDS.Infrastructure.Repositories.DailyCollectionRepository(db),
            new EEMOCantilanSDS.Infrastructure.Repositories.StallRepository(db,rate,new Clock()),
            new EEMOCantilanSDS.Infrastructure.Repositories.NpmMarketClosureRepository(db),rate,new Clock());
        using var services=new ServiceCollection().AddSingleton<IRequestHandler<GetSettleableNpmDaysQuery,Result<IReadOnlyList<SettleableNpmDayDto>>>>(handler).BuildServiceProvider();
        var workflow=new EEMOCantilanSDS.Application.Common.Revenue.FineAssessmentWorkflow(db,new Actor(seed.TenantId),new Tenant(seed.TenantId),new Clock(),sourceReads:new Mediator(services));
        var choices=await workflow.PeriodsAsync(seed.Account,2024);Assert.True(choices.IsSuccess,choices.Error);
        Assert.DoesNotContain(choices.Value!,x=>x.Reference.Start==excused.CollectionDate);
        var actual=await handler.Handle(new(contract.StallId,2024,9,contract.Id),default);
        Assert.Equal(actual.Value!.Select(x=>x.Date),choices.Value!.Where(x=>x.Reference.Start.Month==9).Select(x=>x.Reference.Start));
        Assert.All(choices.Value!,x=>Assert.Equal(FineAffectedPeriodKind.NpmDay,x.Reference.Kind));
        Assert.Single(await db.DailyCollections.ToListAsync());Assert.Empty(await db.FineAssessments.ToListAsync());Assert.Empty(await db.Collections.ToListAsync());
    }
}
