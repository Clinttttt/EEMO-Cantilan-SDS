using EEMOCantilanSDS.Application.Common;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Application.Queries.Mobile.GetCollectorOperationCapabilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing.Application.Mobile;

/// <summary>
/// Assigned is not collectible. Each WCF gate the posting workflow enforces (collector active, NPM facility for the
/// current Water source, Canonical Water authority, effective CT policy, present CT custody) must hold before Ready;
/// governed services additionally need approved setup and a held document of the resolved instrument; operations with
/// no approved Mobile writer are Unsupported however they are assigned.
/// </summary>
public sealed class GetCollectorOperationCapabilitiesQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 2, 0, 0, DateTimeKind.Utc);

    private sealed class FixedMunicipality(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class Caller(Guid tenantId, Guid userId, string role = "Collector") : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId => userId;
        public string? Username => "capability-test";
        public string? Role => role;
        public Guid? CollectorId => role == "Collector" ? userId : null;
        public string? MunicipalityCode => null;
        public Guid? MunicipalityId => tenantId;
    }

    private sealed record World(DbContextOptions<AppDbContext> Options, Guid TenantId, Guid CollectorId);

    private static AppDbContext Context(DbContextOptions<AppDbContext> options, Guid tenantId) =>
        new(options, new FixedMunicipality(tenantId));

    private static async Task<World> SeedAsync(
        bool npmFacility = true, bool canonicalWater = true, bool ctPolicy = true, bool assignTicket = true,
        bool active = true, params string[] operations)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"capability-{Guid.NewGuid()}")
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;
        var tenantId = Guid.NewGuid();
        var facility = Facility.Create(FacilityCode.NPM, "New Public Market", "NPM", municipalityId: tenantId);
        var stall = Stall.Create(facility.Id, "W-01", 0m, ApplicableFees.Water, MarketSection.FishSection,
            createdBy: "test", municipalityId: tenantId);
        var bill = UtilityBill.Create(stall.Id, 2026, 9, 0m, 0m, 10m, 0m, 2m, 5m, "test");
        CollectionSettlementCutover? cutover = null;
        if (canonicalWater)
        {
            bill.MarkWaterPendingCutover();
            cutover = CollectionSettlementCutover.Freeze(tenantId, CollectionSourceKind.UtilityBill, bill.Id,
                CollectionSourcePart.Water, bill.WaterSourceVersion, Now.AddHours(-2), bill.WaterCharge, 0m,
                bill.WaterCharge, "{}", Guid.NewGuid(), Now.AddHours(-1));
            bill.ActivateCanonicalWaterSettlement(cutover);
        }
        var classification = RevenueClassification.Create(RevenueClassificationCodes.Wcf, tenantId);
        var policy = RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2020, 1, 1), "WCF",
            ctPolicy ? RevenueInstrumentType.CashTicket : RevenueInstrumentType.OfficialReceipt, tenantId);
        var collector = CollectorUser.Create("Field Collector", "C-01", "fieldc", null, null,
            new HashedPassword("hash"), tenantId);
        if (!active) collector.Deactivate("test");
        if (npmFacility)
            collector.FacilityAssignments.Add(CollectorFacilityAssignment.Create(collector.Id, facility.Id, FacilityCode.NPM));
        foreach (var code in operations)
            collector.OperationAssignments.Add(CollectorOperationAssignment.Assign(tenantId, collector.Id, code, "head"));
        var book = AccountableFormBook.Receive(tenantId, RevenueInstrumentType.CashTicket, "CT", "CT-", 1, 1, 4,
            Now.AddDays(-1), "head", "head");
        var ticket = AccountableDocument.Register(book, 1, "head");
        AccountableFormAssignment? custody = null;
        if (assignTicket)
        {
            ticket.AssignTo(collector.Id, "head");
            custody = AccountableFormAssignment.Assign(ticket, collector.Id, "head", Now.AddHours(-3), "head");
        }

        await using var context = Context(options, tenantId);
        context.AddRange(facility, stall, bill, classification, policy, collector, book, ticket);
        if (cutover is not null) context.Add(cutover);
        if (custody is not null) context.Add(custody);
        await context.SaveChangesAsync();
        return new World(options, tenantId, collector.Id);
    }

    private static async Task<Result<CollectorOperationCapabilitiesDto>> RunAsync(World world, string role = "Collector", Guid? tenantClaim = null)
    {
        await using var context = Context(world.Options, world.TenantId);
        return await new GetCollectorOperationCapabilitiesQueryHandler(context,
                new Caller(tenantClaim ?? world.TenantId, world.CollectorId, role),
                new FixedMunicipality(world.TenantId), new FixedClock(Now))
            .Handle(new GetCollectorOperationCapabilitiesQuery(), default);
    }

    private static CollectorOperationCapabilityDto Wcf(Result<CollectorOperationCapabilitiesDto> result)
    {
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.Operations.Single(x => x.OperationCode == CollectorOperationCodes.Wcf);
    }

    [Fact]
    public async Task WcfIsReadyOnlyWhenEveryPostingGateHolds()
    {
        var world = await SeedAsync(operations: [CollectorOperationCodes.Wcf]);

        var wcf = Wcf(await RunAsync(world));

        Assert.Equal(CollectorOperationCapabilityStatus.Ready, wcf.Status);
        Assert.True(wcf.IsCollectible);
        Assert.Empty(wcf.ReasonCodes);
    }

    [Fact]
    public async Task UnassignedOperationsAreNotCollectible()
    {
        var world = await SeedAsync();

        var result = await RunAsync(world);

        Assert.True(result.IsSuccess, result.Error);
        Assert.All(result.Value!.Operations, x =>
        {
            Assert.False(x.IsAssigned);
            Assert.False(x.IsCollectible);
            Assert.Equal(CollectorOperationCapabilityStatus.NotAssigned, x.Status);
        });
    }

    [Theory]
    [InlineData(CollectorOperationCodes.LandingBerthing)]
    [InlineData(CollectorOperationCodes.MarketFees)]
    [InlineData(CollectorOperationCodes.VegetableFruitSpaceRental)]
    [InlineData(CollectorOperationCodes.TransferLargeCattle)]
    public async Task AssignedGovernedOperationWithoutSetupIsNotCollectible_NeedsPolicy(string code)
    {
        // Governed services now have a Mobile writer, but assignment alone is never collectibility: with no approved
        // amount rule and no instrument policy the operation is Setup Required, and never "Unsupported".
        var world = await SeedAsync(operations: [code]);

        var operation = (await RunAsync(world)).Value!.Operations.Single(x => x.OperationCode == code);

        Assert.True(operation.IsAssigned);
        Assert.False(operation.IsCollectible);
        Assert.Equal(CollectorOperationCapabilityStatus.NeedsPolicy, operation.Status);
        Assert.Contains(GetCollectorOperationCapabilitiesQueryHandler.ServiceSetupRequired, operation.ReasonCodes);
        Assert.DoesNotContain(GetCollectorOperationCapabilitiesQueryHandler.NoMobileWriter, operation.ReasonCodes);
    }

    private static async Task ConfigureMarketFeesAsync(
        World world, RevenueInstrumentType instrument = RevenueInstrumentType.CashTicket,
        bool enabled = true, bool mobile = true)
    {
        await using var context = Context(world.Options, world.TenantId);
        var classification = RevenueClassification.Create(RevenueClassificationCodes.MarketFees, world.TenantId);
        context.Add(classification);
        context.Add(RevenueClassificationPolicy.Create(classification.Id, new DateOnly(2020, 1, 1), "Market Fees", instrument, world.TenantId));
        var service = GovernedService.Create(world.TenantId, CollectorOperationCodes.MarketFees, "head");
        context.Add(service);
        context.Add(GovernedServiceSetting.Create(world.TenantId, service.Id, new DateOnly(2026, 1, 1),
            GovernedServiceBasis.DirectApprovedAmount, null, null, enabled, mobile, "head"));
        await context.SaveChangesAsync();
    }

    private static CollectorOperationCapabilityDto MarketFees(Result<CollectorOperationCapabilitiesDto> result) =>
        result.Value!.Operations.Single(x => x.OperationCode == CollectorOperationCodes.MarketFees);

    [Fact]
    public async Task GovernedOperationIsReadyOnlyWithSetupPolicyAndAssignedDocumentOfTheResolvedInstrument()
    {
        var world = await SeedAsync(operations: [CollectorOperationCodes.MarketFees]);
        await ConfigureMarketFeesAsync(world);

        var fees = MarketFees(await RunAsync(world));

        Assert.Equal(CollectorOperationCapabilityStatus.Ready, fees.Status);
        Assert.True(fees.IsCollectible);
        Assert.Empty(fees.ReasonCodes);
    }

    [Fact]
    public async Task GovernedOperationNeedsADocumentWhenNoneOfTheResolvedInstrumentIsHeld()
    {
        // Market Fees resolves to a Cash Ticket; a collector holding only an OR (or nothing) cannot collect it.
        var noTicket = await SeedAsync(assignTicket: false, operations: [CollectorOperationCodes.MarketFees]);
        await ConfigureMarketFeesAsync(noTicket);
        var orPolicy = await SeedAsync(operations: [CollectorOperationCodes.MarketFees]);
        await ConfigureMarketFeesAsync(orPolicy, RevenueInstrumentType.OfficialReceipt);

        var none = MarketFees(await RunAsync(noTicket));
        var wrongInstrument = MarketFees(await RunAsync(orPolicy));

        Assert.Equal(CollectorOperationCapabilityStatus.NeedsDocument, none.Status);
        Assert.Equal([GetCollectorOperationCapabilitiesQueryHandler.NoAssignedDocument], none.ReasonCodes);
        Assert.Equal(CollectorOperationCapabilityStatus.NeedsDocument, wrongInstrument.Status);
        Assert.False(wrongInstrument.IsCollectible);
    }

    [Theory]
    [InlineData(false, true, GetCollectorOperationCapabilitiesQueryHandler.ServiceDisabled)]
    [InlineData(true, false, GetCollectorOperationCapabilitiesQueryHandler.MobileChannelDisabled)]
    public async Task DisabledOrWebOnlyGovernedOperationIsNotCollectibleOnMobile(bool enabled, bool mobile, string reason)
    {
        var world = await SeedAsync(operations: [CollectorOperationCodes.MarketFees]);
        await ConfigureMarketFeesAsync(world, enabled: enabled, mobile: mobile);

        var fees = MarketFees(await RunAsync(world));

        Assert.Equal(CollectorOperationCapabilityStatus.NeedsPolicy, fees.Status);
        Assert.False(fees.IsCollectible);
        Assert.Contains(reason, fees.ReasonCodes);
    }

    [Fact]
    public async Task LegacyWaterSourceIsPendingCutoverEvenWithAssignmentPolicyAndTicket()
    {
        var world = await SeedAsync(canonicalWater: false, operations: [CollectorOperationCodes.Wcf]);

        var wcf = Wcf(await RunAsync(world));

        Assert.Equal(CollectorOperationCapabilityStatus.PendingCutover, wcf.Status);
        Assert.False(wcf.IsCollectible);
        Assert.Equal([GetCollectorOperationCapabilitiesQueryHandler.NoCanonicalSource], wcf.ReasonCodes);
    }

    [Fact]
    public async Task MissingNpmAuthorizationOrInactiveCollectorIsAssignedButInactive()
    {
        var noNpm = Wcf(await RunAsync(await SeedAsync(npmFacility: false, operations: [CollectorOperationCodes.Wcf])));
        var inactive = Wcf(await RunAsync(await SeedAsync(active: false, operations: [CollectorOperationCodes.Wcf])));

        Assert.Equal(CollectorOperationCapabilityStatus.AssignedButInactive, noNpm.Status);
        Assert.Contains(GetCollectorOperationCapabilitiesQueryHandler.NpmFacilityRequired, noNpm.ReasonCodes);
        Assert.Equal(CollectorOperationCapabilityStatus.AssignedButInactive, inactive.Status);
        Assert.Contains(GetCollectorOperationCapabilitiesQueryHandler.CollectorInactive, inactive.ReasonCodes);
    }

    [Fact]
    public async Task NonCtPolicyNeedsPolicyAndNoCustodyNeedsDocument()
    {
        var policy = Wcf(await RunAsync(await SeedAsync(ctPolicy: false, operations: [CollectorOperationCodes.Wcf])));
        var document = Wcf(await RunAsync(await SeedAsync(assignTicket: false, operations: [CollectorOperationCodes.Wcf])));

        Assert.Equal(CollectorOperationCapabilityStatus.NeedsPolicy, policy.Status);
        Assert.Equal(CollectorOperationCapabilityStatus.NeedsDocument, document.Status);
        Assert.False(document.IsCollectible);
    }

    [Fact]
    public async Task OnlyASignedInCollectorOfTheResolvedTenantMayAsk()
    {
        var world = await SeedAsync(operations: [CollectorOperationCodes.Wcf]);

        Assert.Equal(ResultStatus.Forbidden, (await RunAsync(world, role: "Admin")).Status);
        Assert.Equal(ResultStatus.Forbidden, (await RunAsync(world, tenantClaim: Guid.NewGuid())).Status);
    }
}
