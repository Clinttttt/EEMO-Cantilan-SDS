using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Fees;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// ST-19: the Collector Mobile Arrears and Delinquency workspace reads the same sources as the Head, one row per liable account, keyed to the ORIGINAL
/// contract (stall rent) or obligation account (space rental). A successor never carries a predecessor's debt and a collector sees only what they are assigned to.
/// </summary>
[Collection(PostgresCollection.Name)]
public partial class HistoricalArrearsReaderTests(PostgresFixture db)
{
    private static readonly DateOnly Now = new(2026, 10, 19);

    private sealed class FixedClock(DateOnly today) : IClock
    {
        public DateOnly PhilippineToday => today;
        public DateTime UtcNow => DateTime.SpecifyKind(today.ToDateTime(new TimeOnly(4, 0)), DateTimeKind.Utc);
        public DateTime PhilippineNow => DateTime.SpecifyKind(today.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Unspecified);
    }
    private sealed record Tenant(Guid MunicipalityId) : ICurrentMunicipalityAccessor { public void Set(Guid id) { } }
    private sealed record Caller(Guid Id, Guid TenantId, string Role = "Collector") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => Id;
        public Guid? CollectorId => Role == "Collector" ? Id : null;
        public Guid? MunicipalityId => TenantId;
        public string? Username => "arrears-test";
        public string? MunicipalityCode => "arrears-test";
        public AdminUserDto? GetCurrentUser() => null;
    }

    private sealed record World(Guid TenantId, Guid FacilityId, Guid CollectorId, Guid StallId, Guid AnnaId, Guid BenId, Guid SpaceAccountId);

    private async Task<World> SeedAsync(bool assignSpace = true)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "Docker unavailable");
        await db.ResetAsync();
        var code = "ARR-" + Guid.NewGuid().ToString("N")[..8];
        var municipality = Municipality.Create(code, "Arrears test", "Province", MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty)) { setup.Municipalities.Add(municipality); await setup.SaveChangesAsync(); }
        var tenant = municipality.Id;
        await using var write = db.CreateContext(tenant);
        var facility = Facility.Create(FacilityCode.TCC, "Commercial Center", "TCC", municipalityId: tenant);
        var stall = Stall.Create(facility.Id, "11", 1_000m, ApplicableFees.BaseRental, municipalityId: tenant);
        // Anna held the stall Jan 2025 - Mar 2026 and Ben since 1 Apr 2026; neither ever paid.
        var anna = Contract.Create(stall.Id, "Anna Cruz", "Anna Cruz", new DateOnly(2025, 1, 1), 5, 1_000m);
        var ben = Contract.Create(stall.Id, "Ben Lopez", "Ben Lopez", new DateOnly(2026, 4, 1), 5, 1_000m);
        var collector = CollectorUser.Create("Arrears Collector", "C-9", "arr-" + Guid.NewGuid().ToString("N")[..8], null, null, new HashedPassword("x"), tenant);
        collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, facility.Id, FacilityCode.TCC));
        var payor = Payor.Create(tenant, "Space Holder", BusinessPayorKind.Person, "test");
        var classification = RevenueClassification.Create(ObligationAccount.ClassificationCodeFor(ObligationKind.KanmanggaySpaceRental), tenant);
        write.AddRange(facility, stall, anna, ben, collector, payor, classification,
            RevenueClassificationPolicy.Create(classification.Id, Now.AddYears(-2), "Space", RevenueInstrumentType.OfficialReceipt, tenant));
        if (assignSpace) write.Add(CollectorOperationAssignment.Assign(tenant, collector.Id, CollectorOperationCodes.KanmanggaySpaceRental, "head"));
        await write.SaveChangesAsync();
        var head = new Caller(Guid.NewGuid(), tenant, "SuperAdmin");
        var account = (await new ObligationWorkflow(write, head, new Tenant(tenant), new FixedClock(Now)).CreateAccountAsync(new(ObligationKind.KanmanggaySpaceRental,
            payor.Id, null, "", null, null, new DateOnly(2026, 4, 1), 1_000m, OccupancyArrangement.SpaceOnly))).Value!;
        return new(tenant, facility.Id, collector.Id, stall.Id, anna.Id, ben.Id, account.Id);
    }

    private HistoricalArrearsReader Reader(AppDbContext read, Caller caller)
    {
        var clock = new FixedClock(Now);
        var tenant = new Tenant(caller.TenantId);
        return new(new FacilityReportsRepository(read, new FeeRateResolver(read), clock), new StallRepository(read, new FeeRateResolver(read), clock),
            new SpaceFollowUpWorkflow(read, caller, tenant), read, caller, tenant, clock);
    }

    [SkippableFact]
    public async Task One_row_per_liable_account_keyed_to_the_original_contract_and_the_successor_never_carries_the_predecessors_debt()
    {
        var w = await SeedAsync();
        await using var read = db.CreateContext(w.TenantId);
        var result = await Reader(read, new(w.CollectorId, w.TenantId)).GetForCollectorAsync(default);

        var anna = Assert.Single(result.Accounts, a => a.ContractId == w.AnnaId);
        Assert.Equal((15_000m, 0m, "arrears", "Re-let", "Ended occupancy"), (anna.ArrearsAmount, anna.DelinquentAmount, anna.Reason, anna.Status, anna.ArrearsBasis));
        Assert.Equal(new CollectionSourceIdentity(SourceIdentityKind.Occupancy, w.AnnaId), anna.Identity);
        Assert.Equal("Anna Cruz", anna.Payer);

        var ben = Assert.Single(result.Accounts, a => a.ContractId == w.BenId);
        Assert.Equal((0m, 6_000m, "delinquent", "Active"), (ben.ArrearsAmount, ben.DelinquentAmount, ben.Reason, ben.Status));
        Assert.Equal(6, ben.UnpaidMonths);
        Assert.Equal("2026-04", ben.FirstPeriod);

        var space = Assert.Single(result.Accounts, a => a.ObligationAccountId == w.SpaceAccountId);
        Assert.Equal(ArrearsAccountKind.SpaceRental, space.Kind);
        Assert.Equal(new CollectionSourceIdentity(SourceIdentityKind.SpaceAccount, w.SpaceAccountId), space.Identity);
        Assert.Equal(6_000m, space.Total);                                           // Apr-Sep, the month in progress is not owed yet

        Assert.Equal(3, result.Accounts.Count);                                      // account-level: no row per month
        Assert.Equal(15_000m + 6_000m + 6_000m, result.Total);
        Assert.Equal(result.Total, result.ArrearsTotal + result.DelinquentTotal);
    }

    [SkippableFact]
    public async Task A_collector_sees_only_the_facilities_and_operations_assigned_to_them()
    {
        var w = await SeedAsync(assignSpace: false);
        await using (var write = db.CreateContext(w.TenantId))
        {
            write.RemoveRange(write.CollectorFacilityAssignments.Where(x => x.CollectorId == w.CollectorId));
            await write.SaveChangesAsync();
        }
        await using var read = db.CreateContext(w.TenantId);
        var result = await Reader(read, new(w.CollectorId, w.TenantId)).GetForCollectorAsync(default);
        Assert.Empty(result.Accounts);

        // A Head or another tenant's token gets nothing from the collector workspace.
        Assert.Empty((await Reader(read, new(Guid.NewGuid(), w.TenantId, "SuperAdmin")).GetForCollectorAsync(default)).Accounts);
        Assert.Empty((await Reader(read, new(w.CollectorId, Guid.NewGuid())).GetForCollectorAsync(default)).Accounts);
    }
}
