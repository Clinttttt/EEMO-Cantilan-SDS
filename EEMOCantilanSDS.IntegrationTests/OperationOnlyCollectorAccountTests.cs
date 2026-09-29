using EEMOCantilanSDS.Application.Command.Collectors.CreateCollector;
using EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;
using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class OperationOnlyCollectorAccountTests(PostgresFixture db)
{
    private sealed class Head : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "integration-head";
        public string? Role => "SuperAdmin";
        public Guid? CollectorId => null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => null;
    }

    private sealed class NoCache : IEemoCacheInvalidator
    {
        public Task InvalidateRegionAsync(string region, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidatePeriodAsync(string tenantCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateFacilityPeriodAsync(string tenantCode, FacilityCode facilityCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidatePaymentAffectedViewsAsync(string tenantCode, FacilityCode? facilityCode, int year, int month, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateReferenceDataAsync(string tenantCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InvalidateTenantAsync(string tenantCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedTenantCode(string code) : ITenantContext
    {
        public string TenantCode => code;
    }

    private sealed class NoPush : IPushSender
    {
        public Task<int> SendToCollectorAsync(Guid collectorId, string title, string body,
            IReadOnlyDictionary<string, string>? data = null, CancellationToken ct = default) => Task.FromResult(0);
    }

    private async Task<Municipality> TenantAsync(string prefix)
    {
        var municipality = Municipality.Create($"{prefix}-{Guid.NewGuid():N}"[..12], prefix, "Province",
            MunicipalityStatus.Active, tenantCode: $"{prefix.ToLowerInvariant()}-{Guid.NewGuid():N}"[..28]);
        await using var setup = db.CreateContext(Guid.Empty);
        setup.Municipalities.Add(municipality);
        await setup.SaveChangesAsync();
        return municipality;
    }

    [SkippableFact]
    public async Task OperationOnlyCollectorIsCreatedAtomicallyWithoutFacilityAndStaysInItsTenant()
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? string.Empty);
        await db.ResetAsync();
        var tenantA = await TenantAsync("OPA");
        var tenantB = await TenantAsync("OPB");
        await using (var setupA = db.CreateContext(tenantA.Id))
        {
            setupA.Facilities.Add(Facility.Create(FacilityCode.NPM, "New Public Market", "NPM", municipalityId: tenantA.Id));
            await setupA.SaveChangesAsync();
        }

        Guid collectorId;
        await using (var context = db.CreateContext(tenantA.Id))
        {
            var repository = new CollectorRepository(context);
            var handler = new CreateCollectorCommandHandler(repository, new UnitOfWork(context), new NoCache(),
                new FixedTenantCode(tenantA.TenantCode), new IdentityPasswordHasher(), new Head());
            var created = await handler.Handle(new CreateCollectorCommand("Landing Collector", "LB-01", "", "",
                $"lb-{Guid.NewGuid():N}"[..14], "Str0ng-Passw0rd!", [],
                [CollectorOperationCodes.LandingBerthing, CollectorOperationCodes.Wcf]), default);
            Assert.True(created.IsSuccess, created.Error);
            Assert.Empty(created.Value!.AssignedFacilities);
            collectorId = created.Value.Id;
        }

        await using (var verifyA = db.CreateContext(tenantA.Id))
        {
            var collector = await verifyA.CollectorUsers.Include(x => x.FacilityAssignments).SingleAsync(x => x.Id == collectorId);
            Assert.Equal(tenantA.Id, collector.MunicipalityId);
            Assert.Empty(collector.FacilityAssignments); // no placeholder NPM or pseudo facility
            var operations = await verifyA.CollectorOperationAssignments.Where(x => x.CollectorId == collectorId)
                .OrderBy(x => x.OperationCode).ToListAsync();
            Assert.Equal([CollectorOperationCodes.LandingBerthing, CollectorOperationCodes.Wcf], operations.Select(x => x.OperationCode));
            Assert.All(operations, x => Assert.Equal(tenantA.Id, x.MunicipalityId));
            Assert.All(operations, x => Assert.Equal("integration-head", x.AssignedBy));
        }

        // Tenant B cannot see or edit tenant A's collector through the same commands.
        await using (var contextB = db.CreateContext(tenantB.Id))
        {
            Assert.Empty(await contextB.CollectorOperationAssignments.ToListAsync());
            var update = new UpdateCollectorCommandHandler(new CollectorRepository(contextB), new Head(),
                new UnitOfWork(contextB), new NoCache(), new FixedTenantCode(tenantB.TenantCode),
                new FacilityRepository(contextB), new NoPush());
            var result = await update.Handle(new UpdateCollectorCommand(collectorId, "Hijacked", "", "", [],
                OperationCodes: [CollectorOperationCodes.MarketFees]), default);
            Assert.Equal(ResultStatus.NotFound, result.Status);
        }

        // In tenant A the collector may swap operations but can never be left with no work.
        await using (var contextA = db.CreateContext(tenantA.Id))
        {
            var update = new UpdateCollectorCommandHandler(new CollectorRepository(contextA), new Head(),
                new UnitOfWork(contextA), new NoCache(), new FixedTenantCode(tenantA.TenantCode),
                new FacilityRepository(contextA), new NoPush());
            var emptied = await update.Handle(new UpdateCollectorCommand(collectorId, "Landing Collector", "", "", [],
                OperationCodes: []), default);
            Assert.Equal(ResultStatus.Invalid, emptied.Status);
            var swapped = await update.Handle(new UpdateCollectorCommand(collectorId, "Landing Collector", "", "", [],
                OperationCodes: [CollectorOperationCodes.LandingBerthing, CollectorOperationCodes.TransferLargeCattle]), default);
            Assert.True(swapped.IsSuccess, swapped.Error);
        }

        await using var final = db.CreateContext(tenantA.Id);
        var finalCodes = await final.CollectorOperationAssignments.Where(x => x.CollectorId == collectorId)
            .OrderBy(x => x.OperationCode).Select(x => x.OperationCode).ToListAsync();
        Assert.Equal([CollectorOperationCodes.LandingBerthing, CollectorOperationCodes.TransferLargeCattle], finalCodes);
    }
}
