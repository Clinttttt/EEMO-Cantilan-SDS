using System.Net;
using System.Net.Http.Json;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Monitoring;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ContractsMonitoringTests(PostgresFixture database)
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private sealed class Clock : IClock
    {
        public DateTime UtcNow => new(2026, 10, 13, 16, 30, 0, DateTimeKind.Utc);
        public DateTime PhilippineNow => PhilippineTime.ToPhilippineTime(UtcNow);
        public DateOnly PhilippineToday => DateOnly.FromDateTime(PhilippineNow);
    }
    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid value) { }
    }
    private sealed record Seed(Guid TenantId, Guid OtherTenantId, Guid NccId, Guid OtherFacilityId,
        Dictionary<string, Guid> Records, Guid RenewedStallId, Guid FutureRenewedStallId);

    private async Task<Seed> SeedAsync()
    {
        Skip.IfNot(database.Available, database.UnavailableReason ?? "Docker unavailable");
        await database.ResetAsync();
        var tenant = Municipality.Create("MONITOR", "Monitor Municipality", "Province", MunicipalityStatus.Active, tenantCode: "monitor");
        var other = Municipality.Create("FOREIGN", "Foreign Municipality", "Province", MunicipalityStatus.Active, tenantCode: "foreign");
        await using (var setup = database.CreateContext(Guid.Empty))
        {
            setup.AddRange(tenant, other); await setup.SaveChangesAsync();
        }
        var ids = new Dictionary<string, Guid>();
        await using var db = database.CreateContext(tenant.Id);
        var ncc = Facility.Create(FacilityCode.NCC, "Tenant Commercial", "NCC", municipalityId: tenant.Id);
        var tcc = Facility.Create(FacilityCode.TCC, "Tenant Tampak", "TCC", municipalityId: tenant.Id);
        var npm = Facility.Create(FacilityCode.NPM, "Tenant Market", "NPM", municipalityId: tenant.Id);
        var custom = Facility.Create(FacilityCode.Custom1, "Tenant Custom Rental", "C1", municipalityId: tenant.Id);
        db.AddRange(ncc, tcc, npm, custom);
        (Stall Stall, Contract Contract) Add(string name, Facility facility, DateOnly start, OccupancyArrangement basis = OccupancyArrangement.SignedContract)
        {
            var stall = Stall.Create(facility.Id, name, 9999m, ApplicableFees.BaseRental, municipalityId: tenant.Id, areaSqm: 9.5);
            var contract = Contract.Create(stall.Id, name, basis == OccupancyArrangement.SignedContract ? "Signed " + name : null,
                start, 1, 1200m, actualMonthlyRental: 800m, arrangement: basis);
            db.AddRange(stall, contract); ids[name] = contract.Id; return (stall, contract);
        }
        (Stall Stall, Contract Contract) Expires(string name, int days, Facility? f = null) => Add(name, f ?? ncc, Today.AddDays(days + 1).AddYears(-1));
        Expires("Active", 31); Expires("Seven", 7); Expires("Thirty", 30, tcc); Expires("Expired", -1); Expires("Today", 0);
        Add("SpaceOnly", ncc, Today.AddYears(-2), OccupancyArrangement.SpaceOnly);
        Add("Extension", tcc, Today.AddYears(-2), OccupancyArrangement.Extension);
        Add("Future", ncc, Today.AddDays(20));
        Expires("Closed", 60).Stall.Close(Today.AddDays(-5));
        Expires("Ended", 60).Contract.Terminate("test", Today.AddDays(-2));
        var renewed = Expires("Renewed", -10);
        renewed.Contract.Terminate("test", Today.AddDays(-11));
        var newTerm = Contract.Create(renewed.Stall.Id, "Renewed holder", "Renewed name", Today, 1, 1500m);
        db.Add(newTerm); ids["Renewed"] = newTerm.Id;
        var ahead = Expires("Ahead", 6);
        ahead.Contract.Terminate("test", Today.AddDays(6));
        db.Add(Contract.Create(ahead.Stall.Id, "Future renewal", "Future renewal", Today.AddDays(7), 1, 5000m));
        Expires("Daily", 60, npm); Expires("Custom", 60, custom); Expires("100% literal_holder", 60);
        void Account(string name, ObligationKind kind, OccupancyArrangement? basis, DateOnly? date, int? years, bool closed = false)
        {
            var account = ObligationAccount.Create(tenant.Id, kind, Guid.Empty, null, name, null, null, date ?? Today.AddMonths(-1), "test", actualOccupant: name);
            account.SetOccupancyBasis(basis, basis == OccupancyArrangement.SignedContract ? "DOC-" + name : null);
            account.SetLeaseDetails(basis == OccupancyArrangement.SignedContract ? "Signed " + name : null, date, years, 7.5m, "Market corner",
                basis == OccupancyArrangement.SignedContract ? 1200m : null);
            if (closed) account.Close(Today.AddDays(-1));
            db.Add(account); ids[name] = account.Id;
            db.Add(ObligationRate.Create(tenant.Id, account.Id, account.ActiveFrom, 900m, "test"));
            db.Add(ObligationRate.Create(tenant.Id, account.Id, Today.AddDays(1), 5000m, "test"));
        }
        Account("KM Signed", ObligationKind.KanmanggaySpaceRental, OccupancyArrangement.SignedContract, Today.AddDays(8).AddYears(-3), 3);
        Account("KM Unknown", ObligationKind.KanmanggaySpaceRental, null, null, null);
        Account("VF Signed", ObligationKind.VegetableFruitSpaceRental, OccupancyArrangement.SignedContract, Today.AddDays(31).AddYears(-1), 1);
        Account("VF Unknown", ObligationKind.VegetableFruitSpaceRental, OccupancyArrangement.SignedContract, Today.AddMonths(-1), null);
        Account("KM Space", ObligationKind.KanmanggaySpaceRental, OccupancyArrangement.SpaceOnly, null, null);
        Account("KM Closed", ObligationKind.KanmanggaySpaceRental, OccupancyArrangement.SignedContract, Today.AddYears(-1), 3, true);
        var fiesta = ObligationAccount.Create(tenant.Id, ObligationKind.FiestaArawLotRental, Guid.Empty, null, "Fiesta event", LotRentalEvent.Fiesta, Today, Today, "test", actualOccupant: "Event holder");
        fiesta.SetOccupancyBasis(OccupancyArrangement.SignedContract, "EVENT-DOC"); db.Add(fiesta); ids["Fiesta"] = fiesta.Id;
        db.Add(TenantOperationSetting.Create(CollectorOperationCodes.KanmanggaySpaceRental, false, "Tenant Spaces", "Head", tenant.Id, "test"));
        await db.SaveChangesAsync();
        await using var foreign = database.CreateContext(other.Id);
        var foreignFacility = Facility.Create(FacilityCode.NCC, "Foreign Secret", "NCC", municipalityId: other.Id);
        var foreignStall = Stall.Create(foreignFacility.Id, "Active", 500m, ApplicableFees.BaseRental, municipalityId: other.Id);
        foreign.AddRange(foreignFacility, foreignStall, Contract.Create(foreignStall.Id, "Foreign Secret", "Secret", Today.AddMonths(-1), 1, 500m));
        await foreign.SaveChangesAsync();
        return new(tenant.Id, other.Id, ncc.Id, foreignFacility.Id, ids, renewed.Stall.Id, ahead.Stall.Id);
    }

    private async Task<ContractsMonitoringPageDto> Read(Seed seed, ContractsMonitoringRequest? request = null)
    {
        await using var db = database.CreateContext(seed.TenantId);
        var page = await new ContractsMonitoringQueries(db, new Tenant(seed.TenantId)).GetAsync(request ?? new() { PageSize = 100 }, Today, default);
        Assert.Empty(db.ChangeTracker.Entries()); // Monitoring never attaches/mutates source entities.
        return page;
    }

    [SkippableFact]
    public async Task CanonicalMixedRegisterHasOneOccupancyPerStallAndCompleteStatusSummary()
    {
        var seed = await SeedAsync(); var page = await Read(seed);
        Assert.Equal(21, page.TotalCount); Assert.Equal(21, page.Items.Count);
        Assert.Equal(new ContractsMonitoringSummaryDto(11, 4, 6, 1, 3, 3, 2, 1, 21), page.Summary);
        ContractMonitoringRowDto Row(string name) => Assert.Single(page.Items, r => r.SourceRecordId == seed.Records[name]);
        Assert.Equal(ContractMonitoringStatus.Active, Row("Active").Status);
        Assert.Equal(7, Row("Seven").DaysUntilExpiry); Assert.Equal(30, Row("Thirty").DaysUntilExpiry);
        Assert.Equal(ContractMonitoringStatus.Expired, Row("Expired").Status);
        Assert.Equal(ContractMonitoringStatus.ExpiringSoon, Row("Today").Status); Assert.Equal(0, Row("Today").DaysUntilExpiry);
        Assert.Equal(ContractMonitoringStatus.NotYetEffective, Row("Future").Status);
        Assert.Equal(ContractMonitoringStatus.Ended, Row("Closed").Status); Assert.Equal(Today.AddDays(-5), Row("Closed").ClosureDate);
        Assert.Equal(ContractMonitoringStatus.Ended, Row("Ended").Status);
        Assert.Equal(ContractMonitoringStatus.NoContract, Row("SpaceOnly").Status); Assert.Null(Row("SpaceOnly").ExpiryDate); Assert.Null(Row("SpaceOnly").DurationYears);
        Assert.Equal(ContractMonitoringStatus.NoContract, Row("Extension").Status);
        Assert.Equal(ContractMonitoringStatus.Unknown, Row("KM Unknown").Status); Assert.Null(Row("KM Unknown").EffectivityDate);
        Assert.Equal(ContractMonitoringStatus.Unknown, Row("VF Unknown").Status); Assert.Null(Row("VF Unknown").ExpiryDate);
        Assert.Single(page.Items, r => r.StallId == seed.RenewedStallId); Assert.Equal(seed.Records["Renewed"], Row("Renewed").SourceRecordId);
        Assert.Single(page.Items, r => r.StallId == seed.FutureRenewedStallId); Assert.Equal(6, Row("Ahead").DaysUntilExpiry);
        Assert.DoesNotContain(page.Items, r => r.SourceRecordId == seed.Records["Fiesta"]);
        Assert.DoesNotContain(page.Items, r => r.OperationDisplayName == "Foreign Secret");
        Assert.Equal((800m, 1200m), (Row("Active").ApprovedCharge, Row("Active").ContractStatedAmount));
        Assert.Equal((900m, 1200m, 3), (Row("KM Signed").ApprovedCharge, Row("KM Signed").ContractStatedAmount, Row("KM Signed").DurationYears));
        Assert.Equal("Tenant Spaces", Row("KM Signed").OperationDisplayName); // Hidden directory entry remains accountable evidence.
        Assert.Equal(1, Row("VF Signed").DurationYears);
        Assert.Null(Row("Daily").ApprovedCharge); Assert.Equal(ContractMonitoringChargeFrequency.Daily, Row("Daily").ChargeFrequency);
        Assert.Equal(7.5m, Row("VF Signed").AreaSqm);
        Assert.All(page.Items.Where(r => r.SourceType == ContractMonitoringSourceType.FacilityContract), r => { Assert.NotNull(r.StallId); Assert.NotNull(r.FacilityId); Assert.Null(r.ObligationKind); Assert.Null(r.ContractReference); });
        Assert.All(page.Items.Where(r => r.SourceType == ContractMonitoringSourceType.ObligationAccount), r => { Assert.Null(r.StallId); Assert.Null(r.FacilityId); Assert.NotNull(r.ObligationKind); });
    }

    [SkippableFact]
    public async Task FiltersAndExpiryWindowsShareASummaryScopeWithoutLeakingTenantData()
    {
        var seed = await SeedAsync();
        var seven = await Read(seed, new() { ExpiryWindow = 7 }); Assert.Equal(4, seven.TotalCount); Assert.Equal(21, seven.Summary.TotalMonitoredOccupancies);
        var thirty = await Read(seed, new() { ExpiryWindow = 30 }); Assert.Equal(6, thirty.TotalCount);
        var impossible = await Read(seed, new() { Status = "Expired", ExpiryWindow = 7 }); Assert.Equal(0, impossible.TotalCount); Assert.Equal(seven.Summary, impossible.Summary);
        var active = await Read(seed, new() { Status = "Active" }); Assert.Equal(11, active.TotalCount); Assert.Equal(11, active.Summary.ActiveCount);
        Assert.All(active.Items, r => Assert.Contains(r.Status, new[] { ContractMonitoringStatus.Active, ContractMonitoringStatus.ExpiringSoon }));
        var km = await Read(seed, new() { Operation = "km" }); Assert.Equal(4, km.TotalCount); Assert.Equal(4, km.Summary.TotalMonitoredOccupancies);
        var vf = await Read(seed, new() { Operation = "VF", OccupancyBasis = "SignedContract" }); Assert.Equal(2, vf.TotalCount);
        var unknown = await Read(seed, new() { OccupancyBasis = "Unknown" }); Assert.Equal(1, unknown.TotalCount);
        var name = await Read(seed, new() { Search = "tenant spaces" }); Assert.Equal(4, name.TotalCount);
        Assert.Single((await Read(seed, new() { Search = "100% literal_" })).Items);
        Assert.Equal(0, (await Read(seed, new() { Search = "Foreign Secret" })).TotalCount);
        Assert.Equal(0, (await Read(seed, new() { FacilityId = seed.OtherFacilityId })).Summary.TotalMonitoredOccupancies);
        var ncc = await Read(seed, new() { FacilityId = seed.NccId, PageSize = 100 }); Assert.All(ncc.Items, r => Assert.Equal(seed.NccId, r.FacilityId));
        await using var unresolved = database.CreateContext(Guid.Empty);
        Assert.Equal(0, (await new ContractsMonitoringQueries(unresolved, new Tenant(Guid.Empty)).GetAsync(new(), Today, default)).TotalCount);
    }

    [SkippableTheory]
    [InlineData("expiry")]
    [InlineData("expiryDesc")]
    [InlineData("occupant")]
    [InlineData("operation")]
    [InlineData("space")]
    public async Task PaginationIsBoundedStableAndDoesNotDuplicateTiedRows(string sort)
    {
        var seed = await SeedAsync(); var all = await Read(seed, new() { PageSize = 100, Sort = sort });
        var ids = new List<Guid>();
        for (var page = 1; page <= 7; page++)
        {
            var result = await Read(seed, new() { Page = page, PageSize = 3, Sort = sort });
            Assert.Equal(21, result.TotalCount); Assert.Equal(page, result.Page); Assert.Equal(3, result.Items.Count);
            Assert.Equal(all.Summary, result.Summary); ids.AddRange(result.Items.Select(r => r.SourceRecordId));
        }
        Assert.Equal(all.Items.Select(r => r.SourceRecordId), ids); Assert.Equal(21, ids.Distinct().Count());
        Assert.Empty((await Read(seed, new() { Page = 10000, PageSize = 100, Sort = sort })).Items);
    }

    [SkippableFact]
    public async Task SqlExpiryAgreesWithCanonicalHelperForLeapDayAndIncompleteEvidence()
    {
        var seed = await SeedAsync();
        await using (var db = database.CreateContext(seed.TenantId))
        {
            var facility = await db.Facilities.SingleAsync(f => f.Id == seed.NccId);
            foreach (var years in new[] { 1, 3, 4 })
            {
                var stall = Stall.Create(facility.Id, "LEAP-" + years, 100m, ApplicableFees.BaseRental, municipalityId: seed.TenantId);
                db.AddRange(stall, Contract.Create(stall.Id, "Leap " + years, "Leap name", new(2024, 2, 29), years, 100m));
            }
            await db.SaveChangesAsync();
        }
        var rows = (await Read(seed, new() { Search = "LEAP-", PageSize = 100 })).Items;
        Assert.Equal(3, rows.Count);
        foreach (var r in rows) Assert.Equal(DomainRules.TermLastDay(new DateOnly(2024, 2, 29), r.DurationYears!.Value), r.ExpiryDate);
    }

    [SkippableTheory]
    [InlineData("SuperAdmin")]
    [InlineData("Admin")]
    public async Task ActualApiAndTypedClientUseAuthenticatedTenantAndPhilippineDate(string role)
    {
        var seed = await SeedAsync();
        await using var host = await CollectorApiHost.StartAsync(database, Guid.NewGuid(), seed.TenantId, role: role, clock: new Clock());
        var response = await host.Client.GetAsync("/api/monitoring/contracts?pageSize=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<ContractsMonitoringPageDto>(CollectorApiHost.Json);
        Assert.Equal(Today, page!.AsOf); Assert.Equal(21, page.TotalCount);
        Assert.Contains("\"FacilityContract\"", await response.Content.ReadAsStringAsync());
        var typed = new EEMOCantilanSDS.HttpClients.ApiClients.ReportsApiClient(host.Client);
        var result = await typed.GetContractsMonitoringAsync(new() { Search = "100% literal_", Sort = "space" });
        Assert.True(result.IsSuccess, result.Error); Assert.Single(result.Value!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/monitoring/contracts?asOf=2026-10-13")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/monitoring/contracts?asOf=2026-10-15")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync("/api/monitoring/contracts?asOf=2026-10-14")).StatusCode);
    }

    [SkippableFact]
    public async Task StoredArchetypeGovernsFacilityEligibilityRatherThanCantilanCodeAssumptions()
    {
        var seed = await SeedAsync();
        await using (var db = database.CreateContext(seed.TenantId))
        {
            var rental = Facility.Create(FacilityCode.TRM, "Tenant rental enterprise", "TRM", archetype: BillingArchetype.MonthlyRental, municipalityId: seed.TenantId);
            var weekly = Facility.Create(FacilityCode.TPM, "Weekly market", "TPM", municipalityId: seed.TenantId);
            var slaughter = Facility.Create(FacilityCode.SLH, "Slaughter", "SLH", municipalityId: seed.TenantId);
            foreach (var facility in new[] { rental, weekly, slaughter })
            {
                var stall = Stall.Create(facility.Id, "Tenant unit", 9999m, ApplicableFees.BaseRental, municipalityId: seed.TenantId);
                db.AddRange(facility, stall, Contract.Create(stall.Id, "Tenant occupant", "Recorded tenant contract", Today.AddMonths(-1), 1, 500m));
            }
            await db.SaveChangesAsync();
        }
        var all = await Read(seed); Assert.Equal(22, all.TotalCount);
        await using var host = await CollectorApiHost.StartAsync(database, Guid.NewGuid(), seed.TenantId, role: "Admin", clock: new Clock());
        var result = await host.Client.GetFromJsonAsync<ContractsMonitoringPageDto>("/api/monitoring/contracts?operation=TRM", CollectorApiHost.Json);
        var row = Assert.Single(result!.Items); Assert.Equal("TRM", row.Operation); Assert.Equal(500m, row.ApprovedCharge);
        Assert.Equal(ContractMonitoringChargeFrequency.Monthly, row.ChargeFrequency);
        Assert.DoesNotContain(all.Items, r => r.FacilityCode is FacilityCode.TPM or FacilityCode.SLH);
    }

    [SkippableTheory]
    [InlineData("Collector")]
    [InlineData("Payor")]
    public async Task ApiDeniesWrongRolesAndAnonymousRequests(string role)
    {
        var seed = await SeedAsync();
        await using var host = await CollectorApiHost.StartAsync(database, Guid.NewGuid(), seed.TenantId, role: role);
        Assert.Equal(HttpStatusCode.Forbidden, (await host.Client.GetAsync("/api/monitoring/contracts")).StatusCode);
        host.Client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync("/api/monitoring/contracts")).StatusCode);
    }

    [SkippableFact]
    public async Task ApiTenantAndSourceDrillDownRemainScopedEvenWithForeignQueryIds()
    {
        var seed = await SeedAsync();
        await using var own = await CollectorApiHost.StartAsync(database, Guid.NewGuid(), seed.TenantId, role: "Admin", clock: new Clock());
        var ownPage = await own.Client.GetFromJsonAsync<ContractsMonitoringPageDto>("/api/monitoring/contracts?municipalityId=" + seed.OtherTenantId, CollectorApiHost.Json);
        Assert.Equal(21, ownPage!.TotalCount);
        var account = Assert.Single((await Read(seed)).Items, r => r.SourceRecordId == seed.Records["KM Signed"]);
        Assert.Equal(HttpStatusCode.OK, (await own.Client.GetAsync($"/api/obligations/accounts/{account.SourceRecordId}/register")).StatusCode);
        await using var foreign = await CollectorApiHost.StartAsync(database, Guid.NewGuid(), seed.OtherTenantId, role: "Admin", clock: new Clock());
        var page = await foreign.Client.GetFromJsonAsync<ContractsMonitoringPageDto>("/api/monitoring/contracts", CollectorApiHost.Json);
        Assert.Equal(1, page!.TotalCount); Assert.Equal(1, page.Summary.TotalMonitoredOccupancies);
        Assert.Equal("Foreign Secret", Assert.Single(page.Items).ActualOccupant);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Client.GetAsync($"/api/obligations/accounts/{account.SourceRecordId}/register")).StatusCode);
        var filtered = await foreign.Client.GetFromJsonAsync<ContractsMonitoringPageDto>($"/api/monitoring/contracts?facilityId={seed.NccId}", CollectorApiHost.Json);
        Assert.Equal(0, filtered!.TotalCount); Assert.Equal(0, filtered.Summary.TotalMonitoredOccupancies);
        await using var db = database.CreateContext(seed.TenantId);
        foreach (var row in (await Read(seed)).Items)
        {
            if (row.SourceType == ContractMonitoringSourceType.FacilityContract)
                Assert.True(await db.Contracts.AnyAsync(c => c.Id == row.SourceRecordId && c.StallId == row.StallId));
            else
                Assert.True(await db.ObligationAccounts.AnyAsync(a => a.Id == row.SourceRecordId && a.Kind == row.ObligationKind));
        }
    }

    [SkippableTheory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=1")]
    [InlineData("operation=FA")]
    [InlineData("occupancyBasis=Invalid")]
    [InlineData("expiryWindow=15")]
    [InlineData("sort=arbitrary-sql")]
    [InlineData("facilityId=not-a-guid")]
    public async Task ApiRejectsInvalidUnboundedOrUnsupportedParameters(string query)
    {
        var seed = await SeedAsync();
        await using var host = await CollectorApiHost.StartAsync(database, Guid.NewGuid(), seed.TenantId, role: "Admin", clock: new Clock());
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.GetAsync("/api/monitoring/contracts?" + query)).StatusCode);
    }
}
