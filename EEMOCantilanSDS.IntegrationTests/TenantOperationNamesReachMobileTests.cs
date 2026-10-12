using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// An office's own operation names reach the Collector app and the Head's assignment list through the server-supplied Name, so an
/// installed app shows them without an update. Only the Name changes: codes, assignment, status and reasons are exactly what they were,
/// and an office that never adopted tenant configuration keeps the standard names.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TenantOperationNamesReachMobileTests(PostgresFixture db)
{
    private sealed class Actor(Guid userId, Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => role == "Collector" ? "collector" : "head";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => "names";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Clock : IClock
    {
        public DateOnly PhilippineToday => PhilippineTime.Today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => PhilippineTime.Now;
    }

    private async Task<(Guid Tenant, Guid Collector)> SeedAsync(bool adopted, string? kanmanggayName)
    {
        await db.ResetAsync();
        var municipality = Municipality.Create($"NM-{Guid.NewGuid():N}"[..14], "Names", "Province", MunicipalityStatus.Active, tenantCode: $"nm-{Guid.NewGuid():N}"[..20]);
        var collector = CollectorUser.Create("Field Collector", "C-01", $"c{Guid.NewGuid():N}"[..12], null, null, new HashedPassword("h"), municipality.Id);
        await using var setup = db.CreateContext(Guid.Empty);
        setup.Municipalities.Add(municipality);
        setup.CollectorUsers.Add(collector);
        if (adopted)
            foreach (var entry in TenantOperationCatalog.All)
                setup.TenantOperationSettings.Add(TenantOperationSetting.Create(
                    entry.Code, true, entry.Code == CollectorOperationCodes.KanmanggaySpaceRental ? kanmanggayName : null, "Onboarding", municipality.Id, "test"));
        await setup.SaveChangesAsync();
        return (municipality.Id, collector.Id);
    }

    private async Task<IReadOnlyList<EEMOCantilanSDS.Application.Dtos.Mobile.CollectorOperationCapabilityDto>> CapabilitiesAsync(Guid tenant, Guid collector)
    {
        await using var ctx = db.CreateContext(tenant);
        var result = await new GetCollectorOperationCapabilitiesQueryHandler(ctx, new Actor(collector, tenant, "Collector"), new FixedTenant(tenant), new Clock())
            .Handle(new GetCollectorOperationCapabilitiesQuery(), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.Operations;
    }

    [SkippableFact]
    public async Task AnOfficeThatNeverAdopted_KeepsTheStandardNames_ExactlyAsBefore()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var (tenant, collector) = await SeedAsync(adopted: false, null);

        var ops = await CapabilitiesAsync(tenant, collector);

        Assert.Equal("Kanmanggay Space Rental", ops.Single(o => o.OperationCode == CollectorOperationCodes.KanmanggaySpaceRental).Name);
        Assert.Equal("Fiesta / Araw Lot Rental", ops.Single(o => o.OperationCode == CollectorOperationCodes.FiestaArawLotRental).Name);
    }

    [SkippableFact]
    public async Task AnAdoptedOffice_SeesItsOwnName_OrTheNeutralOne_NeverCantilans_AndNothingElseChanges()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var (legacyTenant, legacyCollector) = await SeedAsync(adopted: false, null);
        var legacy = await CapabilitiesAsync(legacyTenant, legacyCollector);
        var (tenant, collector) = await SeedAsync(adopted: true, "Municipal Plaza Rental");

        var ops = await CapabilitiesAsync(tenant, collector);

        Assert.Equal("Municipal Plaza Rental", ops.Single(o => o.OperationCode == CollectorOperationCodes.KanmanggaySpaceRental).Name);
        Assert.Equal("Event / Temporary Lot Rental", ops.Single(o => o.OperationCode == CollectorOperationCodes.FiestaArawLotRental).Name);
        // The terminal is facility-backed, so it keeps the platform's name.
        Assert.Equal(legacy.Single(o => o.OperationCode == CollectorOperationCodes.Terminal).Name,
                     ops.Single(o => o.OperationCode == CollectorOperationCodes.Terminal).Name);
        // Everything that decides what a collector may do is identical to an office that never adopted.
        Assert.Equal(legacy.Select(o => (o.OperationCode, o.IsAssigned, o.Status, o.IsCollectible, o.Family)),
                     ops.Select(o => (o.OperationCode, o.IsAssigned, o.Status, o.IsCollectible, o.Family)));
        Assert.All(ops, o => Assert.False(o.IsCollectible)); // nothing is assigned, so nothing is collectible: names grant nothing
    }

    [SkippableFact]
    public async Task TheHeadsAssignmentList_UsesTheSameNames_AndStillAssignsByStableCode()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        var (tenant, collector) = await SeedAsync(adopted: true, "Municipal Plaza Rental");

        await using var ctx = db.CreateContext(tenant);
        var list = await new CollectorOperationAssignmentWorkflow(ctx, new Actor(Guid.NewGuid(), tenant, "SuperAdmin"), new FixedTenant(tenant))
            .ListAsync(collector, CancellationToken.None);

        Assert.True(list.IsSuccess, list.Error);
        var kanmanggay = list.Value!.Single(o => o.Code == CollectorOperationCodes.KanmanggaySpaceRental);
        Assert.Equal("Municipal Plaza Rental", kanmanggay.DisplayName);
        Assert.False(kanmanggay.Assigned);
    }
}
