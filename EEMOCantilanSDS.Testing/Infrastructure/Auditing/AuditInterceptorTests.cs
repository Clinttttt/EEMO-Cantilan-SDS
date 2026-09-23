using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Repositories.Audit;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EEMOCantilanSDS.Testing;

public class AuditInterceptorTests
{
    private sealed class FixedMunicipality(Guid municipalityId) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => municipalityId;
        public void Set(Guid id) { }
    }

    [Fact]
    public async Task FinancialMutation_WritesAttributedAuditLog()
    {
        var actorId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(actorId);
        currentUser.SetupGet(c => c.Username).Returns("head");
        currentUser.SetupGet(c => c.Role).Returns("Admin");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(currentUser.Object))
            .Options;

        using var context = new AppDbContext(options);
        var payment = PaymentRecord.Create(Guid.NewGuid(), 2026, 1, 900m);
        context.Add(payment);
        await context.SaveChangesAsync();

        var log = await context.AuditLogs.SingleAsync();
        Assert.Equal("Created", log.Action);
        Assert.Equal(nameof(PaymentRecord), log.EntityType);
        Assert.Equal(payment.Id, log.EntityId);
        Assert.Equal(actorId.ToString(), log.ActorId);
        Assert.Equal("head", log.ActorName);
        Assert.NotNull(log.NewValues);
    }

    [Fact]
    public async Task UtilityBillFinancialMutation_IsAuditedWithUsefulDetailsAndOwningTenant()
    {
        var municipalityId = Guid.NewGuid();
        var stallId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(actorId);
        currentUser.SetupGet(c => c.Username).Returns("head");
        currentUser.SetupGet(c => c.Role).Returns("SuperAdmin");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(
                new AuditSaveChangesInterceptor(currentUser.Object),
                new MunicipalityStampInterceptor())
            .Options;

        using (var context = new AppDbContext(options, new FixedMunicipality(municipalityId)))
        {
            var bill = UtilityBill.Create(
                stallId, 2026, 9,
                100m, 110m, 8m,
                20m, 22m, 30m,
                createdBy: "head");
            context.UtilityBills.Add(bill);
            await context.SaveChangesAsync();

            bill.RecordPayment(
                "UT-OR-17", null, collectorId: null,
                PaymentStatus.Partial, 25m,
                PaymentStatus.Unpaid, 0m,
                remarks: "not shown in audit detail", updatedBy: "head");
            await context.SaveChangesAsync();

            Assert.Equal(municipalityId, bill.MunicipalityId);

            var logs = await context.AuditLogs
                .Where(a => a.EntityType == nameof(UtilityBill))
                .OrderBy(a => a.Action)
                .ToListAsync();
            Assert.Equal(2, logs.Count);

            var updated = Assert.Single(logs, a => a.Action == "Updated");
            Assert.Equal(actorId.ToString(), updated.ActorId);
            Assert.Equal(municipalityId, updated.MunicipalityId);
            Assert.NotNull(updated.OldValues);
            Assert.Contains("ElecStatus", updated.NewValues!);
            Assert.Contains("ElecPartialAmount", updated.NewValues!);
            Assert.Contains("ElecORNumber", updated.NewValues!);

            var lookup = new AuditDetailComposer.Lookup(
                new Dictionary<Guid, AuditDetailComposer.StallRef>
                {
                    [stallId] = new("12", "New Public Market", "Vegetable Area", "Ana Reyes")
                },
                new Dictionary<Guid, string>(),
                new Dictionary<string, string>());
            var details = AuditDetailComposer.Describe(
                updated.Action, updated.EntityType, updated.EntityId,
                updated.NewValues, updated.OldValues, lookup);
            var changes = AuditDetailComposer.Changes(updated.OldValues, updated.NewValues, lookup);

            Assert.Contains("Updated the utility bill for", details);
            Assert.Contains("Stall 12", details);
            Assert.Contains("September 2026", details);
            Assert.Contains(changes, c => c == "Electricity status Unpaid → Partial");
            Assert.Contains(changes, c => c.StartsWith("Electricity partial amount "));
            Assert.Contains(changes, c => c == "Electricity OR no. — → UT-OR-17");
            Assert.DoesNotContain("not shown in audit detail", details);
            Assert.DoesNotContain(changes, c => c.Contains("Remarks", StringComparison.OrdinalIgnoreCase));
        }

        using var otherTenant = new AppDbContext(options, new FixedMunicipality(Guid.NewGuid()));
        Assert.Empty(await otherTenant.AuditLogs.ToListAsync());
        Assert.Empty(await otherTenant.UtilityBills.ToListAsync());
    }

    [Fact]
    public async Task DormantCollectionAggregate_CreatesTenantAndActorAttributedFinancialAuditEntries()
    {
        var municipalityId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(actorId);
        currentUser.SetupGet(c => c.Username).Returns("ledger-head");
        currentUser.SetupGet(c => c.Role).Returns("SuperAdmin");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(
                new AuditSaveChangesInterceptor(currentUser.Object),
                new MunicipalityStampInterceptor())
            .Options;

        using var context = new AppDbContext(options, new FixedMunicipality(municipalityId));
        var classification = RevenueClassification.Create("MARKET_FEES", municipalityId);
        var policy = RevenueClassificationPolicy.Create(
            classification.Id, new DateOnly(2026, 9, 23), "Market Fees", RevenueInstrumentType.CashTicket,
            municipalityId);
        context.AddRange(classification, policy);
        await context.SaveChangesAsync();

        var collection = Collection.Post(
            new DateOnly(2026, 9, 23),
            DateTime.SpecifyKind(new DateTime(2026, 9, 23, 8, 30, 0), DateTimeKind.Utc),
            actorId.ToString(), "ledger-head", "SuperAdmin",
            [new CollectionLineDraft(classification, policy, 125m,
                CollectionSourceKind.TrmTrip, Guid.NewGuid())],
            clientOperationId: Guid.NewGuid());
        context.Collections.Add(collection);
        await context.SaveChangesAsync();

        var logs = await context.AuditLogs
            .Where(x => x.EntityType == nameof(Collection) || x.EntityType == nameof(CollectionLine))
            .OrderBy(x => x.EntityType)
            .ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log =>
        {
            Assert.Equal("Created", log.Action);
            Assert.Equal(municipalityId, log.MunicipalityId);
            Assert.Equal(actorId.ToString(), log.ActorId);
            Assert.Equal("ledger-head", log.ActorName);
            Assert.Equal("SuperAdmin", log.ActorRole);
            Assert.NotNull(log.NewValues);
        });

        var collectionLog = Assert.Single(logs, x => x.EntityType == nameof(Collection));
        Assert.Equal(collection.Id, collectionLog.EntityId);
        Assert.Contains("TotalAmount", collectionLog.NewValues!);
        Assert.Contains("125", collectionLog.NewValues!);

        var line = Assert.Single(logs, x => x.EntityType == nameof(CollectionLine));
        Assert.Equal(collection.Lines.Single().Id, line.EntityId);
        Assert.Contains("RevenueClassificationPolicyId", line.NewValues!);
        Assert.Contains("\"SourceKind\":6", line.NewValues!);
        Assert.Contains("125", line.NewValues!);
    }

    [Fact]
    public async Task NonFinancialFacilityMutation_RemainsOutsideFinancialAuditAllowlist()
    {
        using var context = NewAuditedContext();
        context.Facilities.Add(Facility.Create(FacilityCode.NPM, "New Public Market", "NPM"));

        await context.SaveChangesAsync();

        Assert.Empty(await context.AuditLogs.ToListAsync());
    }

    private static AppDbContext NewAuditedContext()
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(Guid.NewGuid());
        currentUser.SetupGet(c => c.Username).Returns("head");
        currentUser.SetupGet(c => c.Role).Returns("SuperAdmin");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(currentUser.Object))
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task AccountCreation_IsAudited_WithPasswordRedacted()
    {
        using var context = NewAuditedContext();
        var admin = AdminUser.Create("New Admin", "newadmin", "n@a.com", TestPasswords.Hash("Secret123!"), AdminRole.Admin);
        context.Add(admin);
        await context.SaveChangesAsync();

        var log = await context.AuditLogs.SingleAsync(a => a.EntityType == "AdminUser");
        Assert.Equal("Created", log.Action);
        Assert.Equal(admin.Id, log.EntityId);
        Assert.NotNull(log.NewValues);
        // The password hash must never appear in the audit snapshot.
        Assert.DoesNotContain(admin.PasswordHash, log.NewValues!);
        Assert.Contains("[redacted]", log.NewValues!);
    }

    [Fact]
    public async Task Login_TokenRefresh_IsNotAudited_But_ProfileUpdate_Is()
    {
        using var context = NewAuditedContext();
        var admin = AdminUser.Create("New Admin", "newadmin", "n@a.com", TestPasswords.Hash("Secret123!"), AdminRole.Admin);
        context.Add(admin);
        await context.SaveChangesAsync();   // 1 audit row: account creation

        // Routine token refresh — only auth-housekeeping columns change → must NOT be audited.
        admin.SetRefreshToken("a-token", DateTime.UtcNow.AddDays(7));
        await context.SaveChangesAsync();
        Assert.Equal(1, await context.AuditLogs.CountAsync(a => a.EntityType == "AdminUser"));

        // A meaningful profile change → audited.
        admin.UpdateProfile("Renamed Admin", "newadmin", "n@a.com", "head");
        await context.SaveChangesAsync();
        Assert.Equal(2, await context.AuditLogs.CountAsync(a => a.EntityType == "AdminUser"));
    }

    [Fact]
    public async Task ChangingAnOccupancy_LeavesATrail()
    {
        // Stall was audited; Contract was not. So the facts that DECIDE what a payor owes - who the lessee is,
        // the rent, the term, the effectivity date, whether it was terminated - could be changed with nothing
        // recorded at all. A register whose figures can move without a trace is not a record the office can stand
        // behind, and this is the table an audit would ask about first.
        var actorId = Guid.NewGuid();
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(c => c.UserId).Returns(actorId);
        currentUser.SetupGet(c => c.Username).Returns("head");
        currentUser.SetupGet(c => c.Role).Returns("Admin");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new AuditSaveChangesInterceptor(currentUser.Object))
            .Options;

        using var context = new AppDbContext(options);

        var contract = Contract.Create(
            stallId: Guid.NewGuid(),
            actualOccupant: "Bernadette Lim",
            nameOnContract: "Bernadette Lim",
            effectivityDate: new DateOnly(2026, 8, 1),
            durationYears: 3,
            monthlyRate: 1_500m);

        context.Add(contract);
        await context.SaveChangesAsync();

        var created = await context.AuditLogs.SingleAsync(a => a.EntityType == nameof(Contract));
        Assert.Equal("Created", created.Action);
        Assert.Equal(contract.Id, created.EntityId);
        Assert.Equal("head", created.ActorName);

        // And a later edit is its own entry, so the sequence of changes can be followed rather than only the
        // latest state being visible. Changing a term silently is exactly what an audit would want to see.
        contract.UpdateTerms(new DateOnly(2026, 9, 1), durationYears: 5, updatedBy: "head");

        await context.SaveChangesAsync();

        Assert.Equal(2, await context.AuditLogs.CountAsync(a => a.EntityType == nameof(Contract)));
    }

}
