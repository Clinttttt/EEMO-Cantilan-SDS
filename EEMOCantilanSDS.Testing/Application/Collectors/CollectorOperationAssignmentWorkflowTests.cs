using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

public sealed class CollectorOperationAssignmentWorkflowTests
{
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class CurrentUser(Guid id, Guid tenantId, string role = "SuperAdmin") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => id;
        public string? Username => "assignment-head";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "assignment-test";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed record Setup(DbContextOptions<AppDbContext> Options, AppDbContext Context,
        Guid TenantId, CollectorUser Collector);

    private static async Task<Setup> CreateAsync(string role = "SuperAdmin")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"collector-operation-{Guid.NewGuid():N}")
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;
        var tenantId = Guid.NewGuid();
        var municipality = Municipality.Create($"OP-{Guid.NewGuid():N}"[..12], "Operation Test",
            "Surigao del Sur", MunicipalityStatus.Active, tenantCode: $"operation-{Guid.NewGuid():N}"[..24]);
        tenantId = municipality.Id;
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM",
            archetype: BillingArchetype.DailyStall, municipalityId: tenantId);
        var collector = CollectorUser.Create("Test Collector", "OP-01", "operation-collector",
            null, null, new HashedPassword("test-hash"), tenantId);
        collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(
            collector.Id, facility.Id, FacilityCode.NPM));

        await using (var setup = new AppDbContext(options, new FixedTenant(tenantId)))
        {
            setup.AddRange(municipality, facility, collector);
            await setup.SaveChangesAsync();
        }

        var context = new AppDbContext(options, new FixedTenant(tenantId));
        return new Setup(options, context, tenantId, collector);
    }

    private static CollectorOperationAssignmentWorkflow Workflow(Setup setup, string role = "SuperAdmin") =>
        new(setup.Context, new CurrentUser(Guid.NewGuid(), setup.TenantId, role), new FixedTenant(setup.TenantId));

    [Fact]
    public async Task CatalogIsStableAndExcludesClassificationOnlyAndRetiredFishMeatIdentities()
    {
        var setup = await CreateAsync();
        await using var _ = setup.Context;

        var result = await Workflow(setup).ListAsync(setup.Collector.Id);

        Assert.True(result.IsSuccess, result.Error);
        var catalog = result.Value!;
        Assert.Equal(new[]
        {
            CollectorOperationCodes.Wcf,
            CollectorOperationCodes.VegetableFruitSpaceRental,
            CollectorOperationCodes.LandingBerthing,
            CollectorOperationCodes.TransferLargeCattle,
            CollectorOperationCodes.MarketFees
        }, catalog.Select(x => x.Code));
        Assert.DoesNotContain(catalog, x => x.Code == RevenueClassificationCodes.WeightAndMeasure);
        Assert.DoesNotContain(catalog, x => x.Code == RevenueClassificationCodes.FishMeatVendorFee);
    }

    [Fact]
    public async Task ReplaceIsDeterministicAndLeavesFacilityAssignmentsUntouched()
    {
        var setup = await CreateAsync();
        await using var _ = setup.Context;
        var workflow = Workflow(setup);

        var first = await workflow.ReplaceAsync(setup.Collector.Id,
            new ReplaceCollectorOperationAssignmentsRequest(new[]
            {
                CollectorOperationCodes.Wcf,
                CollectorOperationCodes.LandingBerthing
            }));
        Assert.True(first.IsSuccess, first.Error);

        var second = await workflow.ReplaceAsync(setup.Collector.Id,
            new ReplaceCollectorOperationAssignmentsRequest(new[] { CollectorOperationCodes.Wcf }));
        Assert.True(second.IsSuccess, second.Error);

        var rows = await setup.Context.CollectorOperationAssignments
            .Where(x => x.CollectorId == setup.Collector.Id).ToListAsync();
        Assert.Equal(new[] { CollectorOperationCodes.Wcf }, rows.Select(x => x.OperationCode));
        Assert.All(rows, x =>
        {
            Assert.Equal(setup.TenantId, x.MunicipalityId);
            Assert.Equal("assignment-head", x.AssignedBy);
            Assert.Equal(DateTimeKind.Utc, x.AssignedAtUtc.Kind);
        });
        Assert.Single(await setup.Context.CollectorFacilityAssignments
            .Where(x => x.CollectorId == setup.Collector.Id).ToListAsync());
        Assert.Empty(await setup.Context.Collections.ToListAsync());
    }

    [Fact]
    public async Task UnknownDuplicateWeightAndMeasureAndFishMeatCodesAreRejected()
    {
        var setup = await CreateAsync();
        await using var _ = setup.Context;
        var workflow = Workflow(setup);

        foreach (var invalidCodes in new IReadOnlyList<string>[]
        {
            new[] { "UNKNOWN_OPERATION" },
            new[] { CollectorOperationCodes.Wcf, CollectorOperationCodes.Wcf },
            new[] { RevenueClassificationCodes.WeightAndMeasure },
            new[] { RevenueClassificationCodes.FishMeatVendorFee }
        })
        {
            var result = await workflow.ReplaceAsync(setup.Collector.Id,
                new ReplaceCollectorOperationAssignmentsRequest(invalidCodes));
            Assert.False(result.IsSuccess);
        }

        Assert.Empty(await setup.Context.CollectorOperationAssignments.ToListAsync());
    }

    [Fact]
    public async Task EmptyReplaceIsValidAndNonHeadCannotManageAssignments()
    {
        var setup = await CreateAsync();
        await using var _ = setup.Context;
        var head = Workflow(setup);
        Assert.True((await head.ReplaceAsync(setup.Collector.Id,
            new ReplaceCollectorOperationAssignmentsRequest(new[] { CollectorOperationCodes.Wcf }))).IsSuccess);
        Assert.True((await head.ReplaceAsync(setup.Collector.Id,
            new ReplaceCollectorOperationAssignmentsRequest(Array.Empty<string>()))).IsSuccess);

        var admin = Workflow(setup, "Admin");
        Assert.Equal(ResultStatus.Forbidden, (await admin.ListAsync(setup.Collector.Id)).Status);
        Assert.Equal(ResultStatus.Forbidden, (await admin.ReplaceAsync(setup.Collector.Id,
            new ReplaceCollectorOperationAssignmentsRequest(new[] { CollectorOperationCodes.Wcf }))).Status);
        Assert.Empty(await setup.Context.CollectorOperationAssignments.ToListAsync());
    }

    [Fact]
    public async Task MissingAndCrossTenantCollectorsCannotBeManaged()
    {
        var setup = await CreateAsync();
        await using var _ = setup.Context;
        var workflow = Workflow(setup);

        Assert.Equal(ResultStatus.NotFound, (await workflow.ListAsync(Guid.NewGuid())).Status);
        Assert.Equal(ResultStatus.NotFound, (await workflow.ReplaceAsync(Guid.NewGuid(),
            new ReplaceCollectorOperationAssignmentsRequest(new[] { CollectorOperationCodes.Wcf }))).Status);

        var otherTenant = Municipality.Create($"OT-{Guid.NewGuid():N}"[..12], "Other Tenant",
            "Province", MunicipalityStatus.Active, tenantCode: $"other-{Guid.NewGuid():N}"[..24]);
        var otherCollector = CollectorUser.Create("Other Collector", "OP-02", "other-collector",
            null, null, new HashedPassword("test-hash"), otherTenant.Id);
        await using (var seed = new AppDbContext(setup.Options))
        {
            // Use the tracked tenant-A context's provider database to add the foreign row without tenant filtering.
            seed.Municipalities.Add(otherTenant);
            seed.CollectorUsers.Add(otherCollector);
            await seed.SaveChangesAsync();
        }

        Assert.Equal(ResultStatus.NotFound, (await workflow.ListAsync(otherCollector.Id)).Status);
        Assert.Equal(ResultStatus.NotFound, (await workflow.ReplaceAsync(otherCollector.Id,
            new ReplaceCollectorOperationAssignmentsRequest(new[] { CollectorOperationCodes.Wcf }))).Status);
    }
}
