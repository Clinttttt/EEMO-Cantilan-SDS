using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.Testing;

/// <summary>
/// Official Receipt custody mirrors the accepted Cash Ticket custody: books are received per instrument, a range is
/// assigned to one active collector of the tenant, and OR and CT are never crossed. Assignment is custody only.
/// </summary>
public sealed class AccountableFormOrCustodyTests
{
    private sealed class FixedTenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    private sealed class CurrentUser(Guid tenantId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser.AdminUserDto? GetCurrentUser() => null;
        public Guid? UserId { get; } = Guid.NewGuid();
        public string? Username => "custody-head";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "custody-test";
        public Guid? MunicipalityId => tenantId;
    }

    private sealed record World(DbContextOptions<AppDbContext> Options, Guid TenantId, Guid OtherTenantId,
        CollectorUser Collector, CollectorUser InactiveCollector, CollectorUser OtherTenantCollector);

    private static async Task<World> CreateAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"or-custody-{Guid.NewGuid():N}")
            .AddInterceptors(new MunicipalityStampInterceptor())
            .Options;
        var tenant = Municipality.Create($"OC-{Guid.NewGuid():N}"[..12], "Custody Test", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"custody-{Guid.NewGuid():N}"[..24]);
        var other = Municipality.Create($"OD-{Guid.NewGuid():N}"[..12], "Other Custody Test", "Surigao del Sur",
            MunicipalityStatus.Active, tenantCode: $"custody-o-{Guid.NewGuid():N}"[..24]);
        var collector = CollectorUser.Create("Ana Reyes", "C-01", "ana-or", null, null, new HashedPassword("h"), tenant.Id);
        var inactive = CollectorUser.Create("Old Collector", "C-02", "old-or", null, null, new HashedPassword("h"), tenant.Id);
        inactive.Deactivate("test");
        var otherCollector = CollectorUser.Create("Other Tenant", "C-03", "other-or", null, null, new HashedPassword("h"), other.Id);
        await using (var setup = new AppDbContext(options, new FixedTenant(tenant.Id)))
        {
            setup.AddRange(tenant, other, collector, inactive, otherCollector);
            await setup.SaveChangesAsync();
        }
        return new World(options, tenant.Id, other.Id, collector, inactive, otherCollector);
    }

    private static (AppDbContext Db, AccountableFormCustodyWorkflow Workflow) Open(
        World world, string role = "SuperAdmin", Guid? tenant = null)
    {
        var tenantId = tenant ?? world.TenantId;
        var db = new AppDbContext(world.Options, new FixedTenant(tenantId));
        return (db, new AccountableFormCustodyWorkflow(db, new CurrentUser(tenantId, role), new FixedTenant(tenantId)));
    }

    private static async Task<AccountableFormBookDto> ReceiveAsync(
        World world, RevenueInstrumentType instrument, string prefix, long first, long last)
    {
        var (db, workflow) = Open(world);
        await using var _ = db;
        var result = await workflow.ReceiveAsync(new ReceiveAccountableFormBookRequest(
            instrument, $"{instrument} {prefix}{first}", prefix, first, last, 6));
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task OrRange_IsAssignedToAnActiveCollector_WithAuditedCustody_AndNoMoneyRecorded()
    {
        var world = await CreateAsync();
        var book = await ReceiveAsync(world, RevenueInstrumentType.OfficialReceipt, "OR", 1, 10);
        var (db, workflow) = Open(world);
        await using var _ = db;

        var result = await workflow.AssignRangeAsync(
            new AssignAccountableFormRangeRequest(book.BookId, world.Collector.Id, 3, 5),
            RevenueInstrumentType.OfficialReceipt);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(3, result.Value);
        var docs = await db.AccountableDocuments.Where(x => x.FormBookId == book.BookId).OrderBy(x => x.SerialNumber).ToListAsync();
        Assert.All(docs.Where(x => x.SerialNumber is >= 3 and <= 5), d =>
        {
            Assert.Equal(AccountableDocumentState.Assigned, d.State);
            Assert.Equal(world.Collector.Id, d.AssignedUserId);
            Assert.Equal(RevenueInstrumentType.OfficialReceipt, d.InstrumentType);
        });
        Assert.All(docs.Where(x => x.SerialNumber is < 3 or > 5), d => Assert.Equal(AccountableDocumentState.InOffice, d.State));
        Assert.Equal(3, await db.AccountableFormAssignments.CountAsync(x => x.AssignedUserId == world.Collector.Id && x.ReturnedAtUtc == null));
        Assert.Empty(db.Collections); // custody is not a collection
        Assert.Empty(db.PostingOperations);
    }

    [Fact]
    public async Task CashTicketRoute_RefusesAnOrBook_AndOrRouteRefusesACashTicketBook()
    {
        var world = await CreateAsync();
        var orBook = await ReceiveAsync(world, RevenueInstrumentType.OfficialReceipt, "OR", 1, 5);
        var ctBook = await ReceiveAsync(world, RevenueInstrumentType.CashTicket, "CT", 1, 5);
        var (db, workflow) = Open(world);
        await using var _ = db;

        var ctRouteOnOr = await workflow.AssignCashTicketRangeAsync(
            new AssignAccountableFormRangeRequest(orBook.BookId, world.Collector.Id, 1, 2));
        var orRouteOnCt = await workflow.AssignRangeAsync(
            new AssignAccountableFormRangeRequest(ctBook.BookId, world.Collector.Id, 1, 2),
            RevenueInstrumentType.OfficialReceipt);

        Assert.Equal(ResultStatus.Invalid, ctRouteOnOr.Status);
        Assert.Equal(ResultStatus.Invalid, orRouteOnCt.Status);
        Assert.All(await db.AccountableDocuments.ToListAsync(), d => Assert.Equal(AccountableDocumentState.InOffice, d.State));
        Assert.Empty(db.AccountableFormAssignments);
    }

    [Fact]
    public async Task AssignmentIsAllOrNothing_AnAlreadyAssignedOrConsumedUnitBlocksTheWholeRange()
    {
        var world = await CreateAsync();
        var book = await ReceiveAsync(world, RevenueInstrumentType.OfficialReceipt, "OR", 1, 6);
        var (db, workflow) = Open(world);
        await using var _ = db;
        Assert.True((await workflow.AssignRangeAsync(
            new AssignAccountableFormRangeRequest(book.BookId, world.Collector.Id, 2, 2))).IsSuccess);
        var consumed = await db.AccountableDocuments.SingleAsync(x => x.FormBookId == book.BookId && x.SerialNumber == 4);
        consumed.Consume(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "test");
        await db.SaveChangesAsync();

        var overlapsAssigned = await workflow.AssignRangeAsync(
            new AssignAccountableFormRangeRequest(book.BookId, world.Collector.Id, 1, 3));
        var overlapsConsumed = await workflow.AssignRangeAsync(
            new AssignAccountableFormRangeRequest(book.BookId, world.Collector.Id, 3, 5));

        Assert.Equal(ResultStatus.Conflict, overlapsAssigned.Status);
        Assert.Equal(ResultStatus.Conflict, overlapsConsumed.Status);
        var states = await db.AccountableDocuments.Where(x => x.FormBookId == book.BookId)
            .OrderBy(x => x.SerialNumber).Select(x => x.State).ToListAsync();
        Assert.Equal(new[]
        {
            AccountableDocumentState.InOffice, AccountableDocumentState.Assigned, AccountableDocumentState.InOffice,
            AccountableDocumentState.Consumed, AccountableDocumentState.InOffice, AccountableDocumentState.InOffice
        }, states);
    }

    [Fact]
    public async Task InactiveOrOtherTenantCollector_CannotHoldOrUnits()
    {
        var world = await CreateAsync();
        var book = await ReceiveAsync(world, RevenueInstrumentType.OfficialReceipt, "OR", 1, 3);
        var (db, workflow) = Open(world);
        await using var _ = db;

        var inactive = await workflow.AssignRangeAsync(
            new AssignAccountableFormRangeRequest(book.BookId, world.InactiveCollector.Id, 1, 1));
        var otherTenant = await workflow.AssignRangeAsync(
            new AssignAccountableFormRangeRequest(book.BookId, world.OtherTenantCollector.Id, 1, 1));

        Assert.Equal(ResultStatus.Invalid, inactive.Status);
        Assert.Equal(ResultStatus.Invalid, otherTenant.Status);
        Assert.All(await db.AccountableDocuments.ToListAsync(), d => Assert.Equal(AccountableDocumentState.InOffice, d.State));
    }

    [Fact]
    public async Task AnotherTenant_CannotSeeOrAssignThisTenantsBook()
    {
        var world = await CreateAsync();
        var book = await ReceiveAsync(world, RevenueInstrumentType.OfficialReceipt, "OR", 1, 3);
        var (db, workflow) = Open(world, tenant: world.OtherTenantId);
        await using var _ = db;

        var list = await workflow.ListAsync();
        var assign = await workflow.AssignRangeAsync(
            new AssignAccountableFormRangeRequest(book.BookId, world.OtherTenantCollector.Id, 1, 1));

        Assert.True(list.IsSuccess);
        Assert.Empty(list.Value!);
        Assert.Equal(ResultStatus.NotFound, assign.Status);
    }

    [Fact]
    public async Task CollectorRole_CannotAssignCustody()
    {
        var world = await CreateAsync();
        var book = await ReceiveAsync(world, RevenueInstrumentType.OfficialReceipt, "OR", 1, 3);
        var (db, workflow) = Open(world, role: "Collector");
        await using var _ = db;

        var result = await workflow.AssignRangeAsync(
            new AssignAccountableFormRangeRequest(book.BookId, world.Collector.Id, 1, 1));

        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }
}
