using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Notifications;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Notifications;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>The inbox says each fact once, to the people it belongs to, and never more than the owning records say (IA-074).</summary>
[Collection(PostgresCollection.Name)]
public class NotificationSweepTests(PostgresFixture db)
{
    private const decimal Rate = 1_000m;
    private static readonly DateOnly Today = PhilippineTime.Today;
    private static readonly DateOnly ThisMonth = new(Today.Year, Today.Month, 1);
    private static readonly DateOnly Oldest = ThisMonth.AddMonths(-3);
    private static readonly DateOnly DueThisMonth = BillingPolicy.StallTrackDefaults.DueDate(Today.Year, Today.Month);

    private sealed class Caller(Guid id, Guid tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => id;
        public Guid? MunicipalityId => tenant;
        public string? Username => "inbox-test";
        public string? Role => role;
        public Guid? CollectorId => null;
        public string? MunicipalityCode => "TEST";
        public AdminUserDto? GetCurrentUser() => null;
    }

    private sealed class Pinned(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid value) { }
    }

    private sealed class OnDay(DateOnly today) : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public DateTime PhilippineNow => today.ToDateTime(new TimeOnly(9, 0));
        public DateOnly PhilippineToday => today;
    }

    private static NotificationSweep Sweep(AppDbContext context, Guid tenant, IClock? clock = null)
    {
        clock ??= new SystemClock();
        var policy = new BillingPolicyWorkflow(context, new Caller(Guid.NewGuid(), tenant, "SuperAdmin"), new Pinned(tenant), clock);
        return new(context, new PayorRepository(context, null!), policy, new Pinned(tenant), clock);
    }

    private static NotificationInbox Inbox(AppDbContext context, Guid tenant, Guid person, string role) =>
        new(context, new Caller(person, tenant, role), new Pinned(tenant), new SystemClock());

    private async Task<(Guid Tenant, Guid Stall)> SeedAsync(string code, bool reset = true)
    {
        Skip.IfNot(db.Available, db.UnavailableReason ?? "");
        if (reset) await db.ResetAsync();
        var municipality = Municipality.Create(code, $"Municipality {code}", "Surigao del Sur", MunicipalityStatus.Active, tenantCode: code.ToLowerInvariant());
        await using (var setup = db.CreateContext(Guid.Empty))
        {
            setup.Municipalities.Add(municipality);
            await setup.SaveChangesAsync();
        }

        var facility = Facility.Create(FacilityCode.TCC, "Tampak Commercial Center", "TCC", municipalityId: municipality.Id);
        var stall = Stall.Create(facility.Id, "7", Rate, ApplicableFees.BaseRental, municipalityId: municipality.Id);
        await AddAsync(municipality.Id, facility, stall, Contract.Create(stall.Id, "Maria Santos", "Maria Santos", Oldest, 5, Rate),
            BillingPolicyVersion.Create(municipality.Id, Oldest, Oldest, BillingPolicy.StallTrackDefaults with { CountsMonthsFrom = Oldest }, null, Guid.NewGuid(), DateTime.UtcNow));
        return (municipality.Id, stall.Id);
    }

    private async Task AddAsync(Guid tenant, params object[] rows)
    {
        await using var write = db.CreateContext(tenant);
        write.AddRange(rows);
        await write.SaveChangesAsync();
    }

    private async Task<Guid> PayorAsync(Guid tenant, Guid stall, string phone)
    {
        var payor = PayorUser.Create("Maria Santos", phone, new HashedPassword("hash"));
        await AddAsync(tenant, payor, PayorStallLink.Create(payor.Id, stall));
        return payor.Id;
    }

    private async Task<Guid> StaffAsync(Guid tenant, string name, AdminRole role, bool active = true, bool platform = false)
    {
        var person = AdminUser.Create(name, name, $"{name}@example.test", new HashedPassword("hash"), role, tenant, active, platform, false);
        await AddAsync(tenant, person);
        return person.Id;
    }

    private async Task<Guid> PaymentAsync(Guid tenant, Guid stall, Guid payor, DateOnly month, Action<OnlinePaymentTransaction> become, bool opened = true)
    {
        var record = PaymentRecord.Create(stall, month.Year, month.Month, Rate, "test");
        var payment = OnlinePaymentTransaction.Create($"OP-{Guid.NewGuid():N}"[..17], payor, record.Id, Rate, "PayMongo");
        if (opened) payment.SetPending($"cs_{payment.Id:N}"[..15], $"https://pay.test/{payment.Id}");
        become(payment);
        await AddAsync(tenant, record, payment);
        return payment.Id;
    }

    private static void Paid(OnlinePaymentTransaction t) => t.MarkPaid("pay_1", "gcash", DateTime.UtcNow, "{}");

    private static void NeedsReview(OnlinePaymentTransaction t)
    {
        Paid(t);
        t.MarkSettlementReconciliationRequired("Could not post.", capturedAmount: t.Amount);
    }

    private async Task ChangeAsync(Guid tenant, Guid payment, Action<OnlinePaymentTransaction> become)
    {
        await using var write = db.CreateContext(tenant);
        become(await write.OnlinePaymentTransactions.SingleAsync(t => t.Id == payment));
        await write.SaveChangesAsync();
    }

    private async Task<int> RunAsync(Guid tenant)
    {
        await using var work = db.CreateContext(tenant);
        return await Sweep(work, tenant).RunAsync();
    }

    private async Task<int> RemindAsync(Guid tenant, DateOnly today)
    {
        await using var work = db.CreateContext(tenant);
        return await Sweep(work, tenant, new OnDay(today)).RemindAsync();
    }

    private async Task<List<InboxNotification>> MessagesAsync(Guid tenant, Guid person, InboxRecipientKind kind = InboxRecipientKind.Payor)
    {
        await using var read = db.CreateContext(tenant);
        return await read.InboxNotifications.AsNoTracking().Where(n => n.RecipientId == person && n.RecipientKind == kind).ToListAsync();
    }

    private async Task PaidThroughAsync(Guid tenant, Guid stall, DateOnly lastMonth)
    {
        var rows = new List<object>();
        for (var month = Oldest; month <= lastMonth; month = month.AddMonths(1))
        {
            var record = PaymentRecord.Create(stall, month.Year, month.Month, Rate, "test");
            record.UpdateStatus(PaymentStatus.Paid, 0m, null, "test", null);
            rows.Add(record);
        }
        await AddAsync(tenant, [.. rows]);
    }

    // ------------------------------------------------------------------ the office

    [SkippableFact]
    public async Task APaymentNeedingReview_TellsEachActiveHeadAndAdmin_Once()
    {
        var seeded = await SeedAsync("INB-A");
        var head = await StaffAsync(seeded.Tenant, "head", AdminRole.SuperAdmin);
        var admin = await StaffAsync(seeded.Tenant, "admin", AdminRole.Admin);
        var inactive = await StaffAsync(seeded.Tenant, "left", AdminRole.Admin, active: false);
        var operatorAccount = await StaffAsync(seeded.Tenant, "operator", AdminRole.SuperAdmin, platform: true);
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000101");
        await PaymentAsync(seeded.Tenant, seeded.Stall, payor, Oldest, NeedsReview);

        await RunAsync(seeded.Tenant);

        var seen = await MessagesAsync(seeded.Tenant, head, InboxRecipientKind.Staff);
        Assert.Equal("/online-payments", Assert.Single(seen).Link);
        Assert.Contains("₱1,000.00", seen[0].Body);
        Assert.Single(await MessagesAsync(seeded.Tenant, admin, InboxRecipientKind.Staff));
        Assert.Empty(await MessagesAsync(seeded.Tenant, inactive, InboxRecipientKind.Staff));
        Assert.Empty(await MessagesAsync(seeded.Tenant, operatorAccount, InboxRecipientKind.Staff));
        Assert.Equal(0, await RunAsync(seeded.Tenant));
    }

    [SkippableFact]
    public async Task APaymentTheOfficeDecided_IsNotRaisedToAnyoneElse()
    {
        var seeded = await SeedAsync("INB-B");
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000102");
        await PaymentAsync(seeded.Tenant, seeded.Stall, payor, Oldest, t =>
        {
            NeedsReview(t);
            t.RecordOfficeDecision(OnlinePaymentDisposition.Applied, "OR-1", null, "head", DateTime.UtcNow);
        });
        var head = await StaffAsync(seeded.Tenant, "head", AdminRole.SuperAdmin);

        await RunAsync(seeded.Tenant);

        Assert.Empty(await MessagesAsync(seeded.Tenant, head, InboxRecipientKind.Staff));
    }

    [SkippableFact]
    public async Task AnotherOffice_HearsNothingOfThisOne()
    {
        var mine = await SeedAsync("INB-C");
        var other = await SeedAsync("INB-D", reset: false);
        var theirHead = await StaffAsync(other.Tenant, "their-head", AdminRole.SuperAdmin);
        var payor = await PayorAsync(mine.Tenant, mine.Stall, "09170000103");
        await PaymentAsync(mine.Tenant, mine.Stall, payor, Oldest, NeedsReview);

        await RunAsync(mine.Tenant);
        await RunAsync(other.Tenant);

        Assert.Empty(await MessagesAsync(other.Tenant, theirHead, InboxRecipientKind.Staff));
        await using var read = db.CreateContext(other.Tenant);
        Assert.Empty(await read.InboxNotifications.AsNoTracking().ToListAsync());
    }

    [SkippableFact]
    public async Task LatePenalties_ReachStaffOnceAMonth_AndEachChargeReachesThePayor()
    {
        var seeded = await SeedAsync("INB-E");
        var head = await StaffAsync(seeded.Tenant, "head", AdminRole.SuperAdmin);
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000104");
        var rules = BillingPolicy.StallTrackDefaults with { PenaltyBasis = LatePenaltyBasis.FixedAmount, PenaltyValue = 25m, CountsMonthsFrom = Oldest };
        await AddAsync(seeded.Tenant, BillingPolicyVersion.Create(seeded.Tenant, Oldest, Oldest, rules, "Ordinance 12", Guid.NewGuid(), DateTime.UtcNow));
        await AssessAsync(seeded);

        await RunAsync(seeded.Tenant);

        var office = Assert.Single(await MessagesAsync(seeded.Tenant, head, InboxRecipientKind.Staff));
        Assert.Equal("/operations/fines", office.Link);
        Assert.Contains("3 automatic late penalties", office.Body);
        var hers = await MessagesAsync(seeded.Tenant, payor);
        Assert.Equal(3, hers.Count);
        Assert.All(hers, m => Assert.Contains("₱25.00", m.Body));
        Assert.Equal(0, await RunAsync(seeded.Tenant));
    }

    [SkippableFact]
    public async Task AnEarlierOccupantsPenalty_DoesNotReachTheNewOccupant()
    {
        var seeded = await SeedAsync("INB-F");
        var rules = BillingPolicy.StallTrackDefaults with { PenaltyBasis = LatePenaltyBasis.FixedAmount, PenaltyValue = 25m, CountsMonthsFrom = Oldest };
        await AddAsync(seeded.Tenant, BillingPolicyVersion.Create(seeded.Tenant, Oldest, Oldest, rules, "Ordinance 12", Guid.NewGuid(), DateTime.UtcNow));
        await AssessAsync(seeded);
        await using (var write = db.CreateContext(seeded.Tenant))
            await write.Contracts.Where(c => c.StallId == seeded.Stall).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
        var newOccupant = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000105");

        await RunAsync(seeded.Tenant);

        Assert.Empty(await MessagesAsync(seeded.Tenant, newOccupant));
    }

    private async Task AssessAsync((Guid Tenant, Guid Stall) seeded)
    {
        await using var work = db.CreateContext(seeded.Tenant);
        var clock = new SystemClock();
        var reports = new FacilityReportsRepository(work);
        var policy = new BillingPolicyWorkflow(work, new Caller(Guid.NewGuid(), seeded.Tenant, "SuperAdmin"), new Pinned(seeded.Tenant), clock);
        await new LatePenaltyAssessor(work, reports, new(policy, reports, work, clock), new Pinned(seeded.Tenant), clock).AssessStallAsync(seeded.Stall);
    }

    // ------------------------------------------------------------------ a payor's payments

    [SkippableFact]
    public async Task EachChangeOnAPayment_ReachesItsOwnPayor_Once()
    {
        var seeded = await SeedAsync("INB-G");
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000106");
        var bystander = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000107");
        var payment = await PaymentAsync(seeded.Tenant, seeded.Stall, payor, Oldest, Paid);

        await RunAsync(seeded.Tenant);
        var received = Assert.Single(await MessagesAsync(seeded.Tenant, payor));
        Assert.Equal("Payment received", received.Title);
        Assert.Contains($"7, {Oldest:MMMM yyyy}", received.Body);

        await ChangeAsync(seeded.Tenant, payment, t => t.CompleteWithOr("OR-77", "clerk"));
        await RunAsync(seeded.Tenant);

        var both = await MessagesAsync(seeded.Tenant, payor);
        Assert.Equal(2, both.Count);
        Assert.Contains("OR-77", both.Single(m => m.Title == "Official receipt issued").Body);
        Assert.Empty(await MessagesAsync(seeded.Tenant, bystander));
        Assert.Equal(0, await RunAsync(seeded.Tenant));
    }

    [SkippableFact]
    public async Task AReviewedPayment_TellsThePayorNotToPayAgain_ThenWhatWasDecided()
    {
        var seeded = await SeedAsync("INB-H");
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000108");
        var payment = await PaymentAsync(seeded.Tenant, seeded.Stall, payor, Oldest, NeedsReview);

        await RunAsync(seeded.Tenant);
        Assert.Contains("do not pay this month again", Assert.Single(await MessagesAsync(seeded.Tenant, payor)).Body);

        await ChangeAsync(seeded.Tenant, payment, t => t.RecordOfficeDecision(OnlinePaymentDisposition.Refunded, "RF-5", null, "head", DateTime.UtcNow));
        await RunAsync(seeded.Tenant);

        var decided = (await MessagesAsync(seeded.Tenant, payor)).Single(m => m.Title == "Payment review finished");
        Assert.Contains("refund", decided.Body);
        Assert.Contains("RF-5", decided.Body);
    }

    [SkippableFact]
    public async Task AClosedOrFailedCheckout_NeverClaimsNothingWasCharged()
    {
        var seeded = await SeedAsync("INB-I");
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000109");
        await PaymentAsync(seeded.Tenant, seeded.Stall, payor, Oldest, t => t.MarkExpired("{}"));
        await PaymentAsync(seeded.Tenant, seeded.Stall, payor, Oldest.AddMonths(1), t => t.MarkFailed("{}"));

        await RunAsync(seeded.Tenant);

        var messages = await MessagesAsync(seeded.Tenant, payor);
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m =>
        {
            Assert.Contains("If money left your account", m.Body);
            Assert.DoesNotContain("not charged", m.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("nothing was", m.Body, StringComparison.OrdinalIgnoreCase);
        });
    }

    [SkippableFact]
    public async Task ACheckoutTheGatewayNeverOpened_SaysNothing_AndAnOldPaymentIsNotAnnounced()
    {
        var seeded = await SeedAsync("INB-J");
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000110");
        await PaymentAsync(seeded.Tenant, seeded.Stall, payor, Oldest, t => t.MarkFailed("{}"), opened: false);
        await PaymentAsync(seeded.Tenant, seeded.Stall, payor, Oldest.AddMonths(1), Paid);
        await using (var write = db.CreateContext(seeded.Tenant))
        {
            var longAgo = DateTime.UtcNow.AddDays(-8);
            await write.OnlinePaymentTransactions.Where(t => t.Status == OnlinePaymentStatus.Paid)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreatedAt, longAgo).SetProperty(t => t.UpdatedAt, longAgo));
        }

        await RunAsync(seeded.Tenant);

        Assert.Empty(await MessagesAsync(seeded.Tenant, payor));
    }

    // ------------------------------------------------------------------ rent reminders

    [SkippableFact]
    public async Task RentInsideTheWindow_IsRemindedOnce_AndNotBeforeIt()
    {
        var seeded = await SeedAsync("INB-K");
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000111");
        await PaidThroughAsync(seeded.Tenant, seeded.Stall, ThisMonth.AddMonths(-1));

        await RemindAsync(seeded.Tenant, DueThisMonth.AddDays(-4));
        Assert.Empty(await MessagesAsync(seeded.Tenant, payor));

        await RemindAsync(seeded.Tenant, DueThisMonth.AddDays(-3));
        await RemindAsync(seeded.Tenant, DueThisMonth);

        var due = Assert.Single(await MessagesAsync(seeded.Tenant, payor));
        Assert.Equal("Rent coming due", due.Title);
        Assert.Contains("₱1,000.00", due.Body);
    }

    [SkippableFact]
    public async Task RentAlreadyPaid_IsNotChased()
    {
        var seeded = await SeedAsync("INB-L");
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000112");
        await PaidThroughAsync(seeded.Tenant, seeded.Stall, ThisMonth);

        await RemindAsync(seeded.Tenant, DueThisMonth);

        Assert.Empty(await MessagesAsync(seeded.Tenant, payor));
    }

    [SkippableFact]
    public async Task PastDueRent_StatesTheMonthsAndTheTotal_OncePerCalendarMonth()
    {
        var seeded = await SeedAsync("INB-M");
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000113");

        await RemindAsync(seeded.Tenant, Today);
        await RemindAsync(seeded.Tenant, Today);

        var overdue = (await MessagesAsync(seeded.Tenant, payor)).Single(m => m.Title == "Rent past due");
        Assert.Contains("3 months", overdue.Body);
        Assert.Contains("₱3,000.00", overdue.Body);
        Assert.Contains(Oldest.ToString("MMMM yyyy"), overdue.Body);

        await RemindAsync(seeded.Tenant, ThisMonth.AddMonths(1));

        Assert.Equal(2, (await MessagesAsync(seeded.Tenant, payor)).Count(m => m.Title == "Rent past due"));
    }

    [SkippableFact]
    public async Task AMonthFromBeforeTheRulesBegin_IsNotChased()
    {
        var seeded = await SeedAsync("INB-S");
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000117");
        await AddAsync(seeded.Tenant, BillingPolicyVersion.Create(seeded.Tenant, Oldest, Oldest, BillingPolicy.StallTrackDefaults with { CountsMonthsFrom = ThisMonth }, null, Guid.NewGuid(), DateTime.UtcNow));

        await RemindAsync(seeded.Tenant, Today);

        Assert.DoesNotContain(await MessagesAsync(seeded.Tenant, payor), m => m.Title == "Rent past due");
    }

    [SkippableFact]
    public async Task RentInsideItsGraceDays_IsNotYetPastDue()
    {
        var seeded = await SeedAsync("INB-R");
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000116");
        await PaidThroughAsync(seeded.Tenant, seeded.Stall, ThisMonth.AddMonths(-1));
        var rules = BillingPolicy.StallTrackDefaults with { GraceDays = 5, CountsMonthsFrom = Oldest };
        await AddAsync(seeded.Tenant, BillingPolicyVersion.Create(seeded.Tenant, Oldest, Oldest, rules, null, Guid.NewGuid(), DateTime.UtcNow));

        await RemindAsync(seeded.Tenant, rules.FirstLateDay(Today.Year, Today.Month).AddDays(-1));
        Assert.DoesNotContain(await MessagesAsync(seeded.Tenant, payor), m => m.Title == "Rent past due");

        await RemindAsync(seeded.Tenant, rules.FirstLateDay(Today.Year, Today.Month));
        Assert.Single(await MessagesAsync(seeded.Tenant, payor), m => m.Title == "Rent past due");
    }

    [SkippableFact]
    public async Task AMonthTheOfficeIsReviewing_IsNotChased()
    {
        var seeded = await SeedAsync("INB-N");
        var payor = await PayorAsync(seeded.Tenant, seeded.Stall, "09170000114");
        await PaidThroughAsync(seeded.Tenant, seeded.Stall, ThisMonth.AddMonths(-2));
        await PaymentAsync(seeded.Tenant, seeded.Stall, payor, ThisMonth.AddMonths(-1), NeedsReview);

        await RemindAsync(seeded.Tenant, Today);

        Assert.DoesNotContain(await MessagesAsync(seeded.Tenant, payor), m => m.Title == "Rent past due");
    }

    // ------------------------------------------------------------------ reading an inbox

    [SkippableFact]
    public async Task AnInbox_ReadsAndClearsOnlyTheCallersOwnMessages()
    {
        var seeded = await SeedAsync("INB-O");
        var (me, you) = (Guid.NewGuid(), Guid.NewGuid());
        var now = DateTime.UtcNow;
        var mine = InboxNotification.Create(seeded.Tenant, InboxRecipientKind.Payor, me, "a", "Mine", "Mine", null, now);
        var yours = InboxNotification.Create(seeded.Tenant, InboxRecipientKind.Payor, you, "a", "Yours", "Yours", null, now);
        var sameIdOnStaff = InboxNotification.Create(seeded.Tenant, InboxRecipientKind.Staff, me, "a", "Staff", "Staff", null, now);
        await AddAsync(seeded.Tenant, mine, yours, sameIdOnStaff);

        await using var work = db.CreateContext(seeded.Tenant);
        var inbox = Inbox(work, seeded.Tenant, me, "Payor");

        var page = (await inbox.ReadMineAsync()).Value!;
        Assert.Equal("Mine", Assert.Single(page.Items).Title);
        Assert.Equal(1, page.Unread);
        Assert.Equal(ResultStatus.NotFound, (await inbox.MarkReadAsync(yours.Id)).Status);
        Assert.Equal(ResultStatus.NotFound, (await inbox.MarkReadAsync(sameIdOnStaff.Id)).Status);

        var cleared = await inbox.MarkReadAsync(null);

        Assert.Equal(0, cleared.Value!.Unread);
        Assert.True(cleared.Value.Items.Single().Read);
        await using var read = db.CreateContext(seeded.Tenant);
        Assert.Equal(2, await read.InboxNotifications.CountAsync(n => n.ReadAt == null));
    }

    [SkippableFact]
    public async Task MarkingOneMessageRead_LeavesTheOthersUnread()
    {
        var seeded = await SeedAsync("INB-S");
        var me = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var first = InboxNotification.Create(seeded.Tenant, InboxRecipientKind.Staff, me, "a", "First", "First", null, now);
        var second = InboxNotification.Create(seeded.Tenant, InboxRecipientKind.Staff, me, "b", "Second", "Second", null, now);
        await AddAsync(seeded.Tenant, first, second);
        await using var work = db.CreateContext(seeded.Tenant);

        var page = (await Inbox(work, seeded.Tenant, me, "Admin").MarkReadAsync(first.Id)).Value!;

        Assert.Equal(1, page.Unread);
        Assert.True(page.Items.Single(i => i.Id == first.Id).Read);
        Assert.False(page.Items.Single(i => i.Id == second.Id).Read);
    }

    [SkippableFact]
    public async Task AnInbox_RefusesAnotherOffice_AndAnyOtherRole()
    {
        var seeded = await SeedAsync("INB-P");
        await using var work = db.CreateContext(seeded.Tenant);

        var otherOffice = new NotificationInbox(work, new Caller(Guid.NewGuid(), Guid.NewGuid(), "SuperAdmin"), new Pinned(seeded.Tenant), new SystemClock());
        Assert.Equal(ResultStatus.Forbidden, (await otherOffice.ReadMineAsync()).Status);
        Assert.Equal(ResultStatus.Forbidden, (await Inbox(work, seeded.Tenant, Guid.NewGuid(), "Collector").ReadMineAsync()).Status);
    }

    /// <summary>Two passes that both read "not sent yet" must not both send. The database is what guarantees it, so that is what this asks directly.</summary>
    [SkippableFact]
    public async Task TheDatabase_RefusesTheSameFactTwiceForOnePerson_ButNotForAnother()
    {
        var seeded = await SeedAsync("INB-Q");
        var (me, you) = (Guid.NewGuid(), Guid.NewGuid());
        InboxNotification Message(Guid person) => InboxNotification.Create(seeded.Tenant, InboxRecipientKind.Staff, person, "online-capture:1", "T", "B", null, DateTime.UtcNow);
        await AddAsync(seeded.Tenant, Message(me));

        await AddAsync(seeded.Tenant, Message(you));

        await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(seeded.Tenant, Message(me)));
    }
}
