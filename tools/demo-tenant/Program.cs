using System.Security.Cryptography;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Notifications;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Tenancy;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Security;
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

// A clearly labelled office with realistic months of rent, for showing the system. It takes a new, empty local database and refuses anything else.
// shortcut: monthly rent only, so market days, slaughter, terminal and water show as empty. Add their writers when a walkthrough needs them.
Require(args.Length == 2 && args[0] == "seed", "Use: seed <file to write the demo sign-in details to>");
var connection = Environment.GetEnvironmentVariable("STALLTRACK_DEMO_CONNECTION");
Require(!string.IsNullOrWhiteSpace(connection), "Set STALLTRACK_DEMO_CONNECTION.");
var target = new NpgsqlConnectionStringBuilder(connection);
Require(target.Host is "127.0.0.1" or "localhost" or "::1", "Only a database on this machine is allowed.");
Require((target.Database ?? "").Contains("demo", StringComparison.OrdinalIgnoreCase) || (target.Database ?? "").Contains("preview", StringComparison.OrdinalIgnoreCase),
    "The database name must contain demo or preview, so a copy of a real office can never receive this data.");

var clock = new SystemClock();
var today = clock.PhilippineToday;
var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(new MunicipalityStampInterceptor()).Options;

Guid tenant;
await using (var setup = new AppDbContext(options, new Pinned(Guid.Empty)))
{
    await setup.Database.MigrateAsync();
    // The only office in its own database, so no real office's rows are even nearby. It runs before the app's first start, which would otherwise seed Cantilan.
    Require(!await setup.Municipalities.IgnoreQueryFilters().AnyAsync(), "This database already has an office. Use a new, empty database.");
    // Its own seal, because a default office without one falls back to Cantilan's on screen.
    var office = Municipality.Create("DEMO", "Demo Municipality", "Demo Province", MunicipalityStatus.Active, tenantCode: "demo",
        officeName: "Demo Economic Enterprise Office", sealPath: "/images/stalltrack-seal.png", officeAcronym: "DEMO", isDefault: true, createdBy: "demo-tenant");
    setup.Municipalities.Add(office);
    await setup.SaveChangesAsync();
    tenant = office.Id;
}

var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
var hash = new IdentityPasswordHasher().Hash(password);
var random = new Random(2026);

await using var db = new AppDbContext(options, new Pinned(tenant));
var pinned = new Pinned(tenant);
var head = AdminUser.Create("Demo Head", "demo.head", "demo.head@example.test", hash, AdminRole.SuperAdmin, tenant, mustChangePassword: false);
var admin = AdminUser.Create("Demo Admin", "demo.admin", "demo.admin@example.test", hash, AdminRole.Admin, tenant, mustChangePassword: false);
var collectors = new[] { "Jose Ramos", "Lina Torres" }
    .Select((name, i) => CollectorUser.Create(name, $"DEMO-C{i + 1}", $"demo.collector{i + 1}", null, null, hash, tenant)).ToArray();
db.AddRange(head, admin);
db.AddRange(collectors.Cast<object>());

var firstMonth = new DateOnly(today.Year - 1, 1, 1);
var thisMonth = new DateOnly(today.Year, today.Month, 1);
var receipt = 1000;
string NextOr() => $"DEMO-{receipt++:000000}";
Guid Collector() => collectors[random.Next(collectors.Length)].Id;
DateTime OnDay(DateOnly month, int day) => new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)), 1, 0, 0, DateTimeKind.Utc);

// Row-level receipts are dated when the money came in, or a whole history would read as collected today.
PaymentRecord Paid(Guid stall, decimal rate, DateOnly month, DateTime received, decimal? partial = null)
{
    var row = PaymentRecord.Create(stall, month.Year, month.Month, rate, "demo-tenant");
    row.RecordPayment(NextOr(), Collector(), partial is null ? PaymentStatus.Paid : PaymentStatus.Partial, partial, remarks: "Demo data", updatedBy: "demo-tenant");
    row.BackdateReceipt(received, "demo-tenant");
    return row;
}

var firstNames = new[] { "Maria", "Juan", "Rosa", "Pedro", "Elena", "Ramon", "Teresa", "Carlos", "Liza", "Andres", "Grace", "Roberto", "Nora", "Manuel", "Sofia", "Danilo", "Carmen", "Felix", "Irene", "Oscar" };
var lastNames = new[] { "Santos", "Reyes", "Cruz", "Bautista", "Garcia", "Mendoza", "Flores", "Villanueva", "Castro", "Navarro", "Salazar", "Aquino", "Pascual", "Domingo", "Gonzales", "Mercado" };
var plan = new[]
{
    (Code: FacilityCode.NCC, Name: "Demo Commercial Center", Short: "DCC", Stalls: 22, Rates: new[] { 1200m, 2400m, 3240m }),
    (Code: FacilityCode.TCC, Name: "Demo Trade Center", Short: "DTC", Stalls: 16, Rates: new[] { 2400m, 3600m, 4800m }),
    (Code: FacilityCode.BBQ, Name: "Demo Food Stands", Short: "DFS", Stalls: 8, Rates: new[] { 800m }),
    (Code: FacilityCode.ICE, Name: "Demo Ice Plant", Short: "DIP", Stalls: 2, Rates: new[] { 6000m }),
};

var occupied = new List<(Stall Stall, decimal Rate, string Occupant)>();
foreach (var f in plan)
{
    var facility = Facility.Create(f.Code, f.Name, f.Short, "Demo data.", municipalityId: tenant);
    db.Add(facility);
    for (var n = 1; n <= f.Stalls; n++)
    {
        var rate = f.Rates[random.Next(f.Rates.Length)];
        var stall = Stall.Create(facility.Id, $"{f.Short[..1]}-{n:00}", rate, ApplicableFees.BaseRental, municipalityId: tenant);
        db.Add(stall);
        if (n % 11 == 0) continue; // a vacant space
        var occupant = $"{firstNames[random.Next(firstNames.Length)]} {lastNames[random.Next(lastNames.Length)]}";
        var effective = n % 7 == 0 ? new DateOnly(today.Year - 1, 7, 1) : firstMonth;
        db.Add(Contract.Create(stall.Id, occupant, occupant, effective, 5, rate, createdBy: "demo-tenant"));
        occupied.Add((stall, rate, occupant));

        // How this occupant pays. Most pay on time; some pay late or in part; a few stopped paying, last year or this year.
        var habit = random.NextDouble();
        var stoppedAt = habit >= .95 ? new DateOnly(today.Year - 1, 9, 1) : habit >= .85 ? thisMonth.AddMonths(-random.Next(1, 5)) : (DateOnly?)null;
        for (var month = effective; month <= thisMonth; month = month.AddMonths(1))
        {
            if (stoppedAt is { } stop && month >= stop) break;
            if (month == thisMonth && random.NextDouble() > .35) continue; // most of this month's rent is not in yet
            if (habit >= .65 && random.NextDouble() < .2 && month < thisMonth)
            {
                db.Add(Paid(stall.Id, rate, month, OnDay(month, random.Next(20, 28)), partial: decimal.Round(rate / 2, 2)));
                continue;
            }
            db.Add(Paid(stall.Id, rate, month, OnDay(month, habit >= .65 ? random.Next(16, 28) : random.Next(1, 15))));
        }
    }
}

// The operations this office declared, stored as activation stores them: a row for every catalog operation, only these listed.
// Weight and Measure gets its classification without a policy, as activation gives it, so Revenue Setup shows it Setup required.
string[] declared = [CollectorOperationCodes.MarketFees, CollectorOperationCodes.WeightAndMeasure];
foreach (var entry in TenantOperationCatalog.All)
    db.Add(TenantOperationSetting.Create(entry.Code, declared.Contains(entry.Code), null, "Onboarding", tenant, "demo-tenant"));
db.Add(RevenueClassification.Create(RevenueClassificationCodes.WeightAndMeasure, tenant, "demo-tenant"));
await db.SaveChangesAsync();

// Three payor accounts whose stories cover what the portal and the office must agree on.
var (onTime, behind, refunded) = (occupied[0], occupied[22], occupied[38]);
var payors = new[] { (onTime, "09170000001"), (behind, "09170000002"), (refunded, "09170000003") }
    .Select(p => (Account: PayorUser.Create(p.Item1.Occupant, p.Item2, hash), p.Item1.Stall)).ToArray();
foreach (var (account, stall) in payors) db.AddRange(account, PayorStallLink.Create(account.Id, stall.Id));

await ClearAsync(onTime.Stall.Id, thisMonth.AddMonths(-2));
await ClearAsync(behind.Stall.Id, thisMonth.AddMonths(-3));
await ClearAsync(refunded.Stall.Id, thisMonth.AddMonths(-1));

// Paid online and waiting for the office's receipt, and already receipted.
db.AddRange(Online(payors[0].Account.Id, onTime, thisMonth.AddMonths(-2), (t, r) =>
{
    Captured(t);
    var or = NextOr();
    r.RecordPayment(or, Collector(), PaymentStatus.Paid, remarks: "Paid online", updatedBy: "demo-tenant");
    t.CompleteWithOr(or, "demo-tenant");
}));
db.AddRange(Online(payors[0].Account.Id, onTime, thisMonth.AddMonths(-1), (t, r) => { Captured(t); r.MarkPaidOnline("Paid online", "demo-tenant"); }));
// Money the gateway took that could not post on its own, and a checkout that closed unpaid.
db.AddRange(Online(payors[1].Account.Id, behind, thisMonth.AddMonths(-3), (t, _) => { Captured(t); t.MarkSettlementReconciliationRequired("The amount the gateway reported differs from the checkout.", capturedAmount: t.Amount + 50m); }));
db.AddRange(Online(payors[1].Account.Id, behind, thisMonth.AddMonths(-2), (t, _) => t.MarkExpired("{}")));
// A captured payment the Head already decided to refund.
db.AddRange(Online(payors[2].Account.Id, refunded, thisMonth.AddMonths(-1), (t, _) =>
{
    Captured(t);
    t.MarkSettlementReconciliationRequired("Already paid at the office.", capturedAmount: t.Amount);
    t.RecordOfficeDecision(OnlinePaymentDisposition.Refunded, "DEMO-RF-1", "Refunded through the gateway.", "demo.head", clock.UtcNow);
}));

// A Head-set policy with a labelled reference, starting this January, so last year's unpaid months carry no penalty.
db.Add(BillingPolicyVersion.Create(tenant, new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 1, 1),
    BillingPolicy.StallTrackDefaults with { DueDay = 15, GraceDays = 5, PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid, PenaltyValue = 2m, CountsMonthsFrom = new DateOnly(today.Year - 1, 1, 1) },
    "Demo Ordinance No. 1 (illustrative, not a real ordinance)", head.Id, clock.UtcNow));
await db.SaveChangesAsync();

var charges = 0;
foreach (var (stall, _, _) in occupied)
{
    await using var work = new AppDbContext(options, pinned);
    var reports = new FacilityReportsRepository(work);
    var policy = new BillingPolicyWorkflow(work, new Caller(head.Id, tenant), pinned, clock);
    charges += await new LatePenaltyAssessor(work, reports, new BillingRules(policy, reports, work, clock), pinned, clock).AssessStallAsync(stall.Id);
}

int messages;
await using (var work = new AppDbContext(options, pinned))
{
    var sweep = new NotificationSweep(work, new PayorRepository(work, null!), new BillingPolicyWorkflow(work, new Caller(head.Id, tenant), pinned, clock), pinned, clock);
    messages = await sweep.RunAsync() + await sweep.RemindAsync();
}

await File.WriteAllLinesAsync(args[1],
[
    "Demo sign-in details. Local demo database only.",
    $"Head: demo.head / {password}",
    $"Admin: demo.admin / {password}",
    $"Payors: {string.Join(", ", payors.Select(p => p.Account.Username))} / {password}",
]);
Console.WriteLine($"Demo office seeded: {plan.Sum(f => f.Stalls)} spaces, {occupied.Count} occupied, {receipt - 1000} receipts, {charges} late penalties, {messages} messages.");

// These stories need their months open, whatever the random history gave them.
async Task ClearAsync(Guid stall, DateOnly from)
{
    await db.SaveChangesAsync();
    await db.PaymentRecords.Where(r => r.StallId == stall && (r.BillingYear > from.Year || r.BillingYear == from.Year && r.BillingMonth >= from.Month)).ExecuteDeleteAsync();
}

static void Captured(OnlinePaymentTransaction t) => t.MarkPaid("pay_demo", "gcash", DateTime.UtcNow, "{}");

object[] Online(Guid payor, (Stall Stall, decimal Rate, string Occupant) space, DateOnly month, Action<OnlinePaymentTransaction, PaymentRecord> become)
{
    var row = PaymentRecord.Create(space.Stall.Id, month.Year, month.Month, space.Rate, "demo-tenant");
    var payment = OnlinePaymentTransaction.Create($"DEMO-OP-{Guid.NewGuid():N}"[..20].ToUpperInvariant(), payor, row.Id, space.Rate, "PayMongo");
    payment.SetPending($"cs_demo_{payment.Id:N}"[..20], "https://pay.example.test/demo");
    become(payment, row);
    return [row, payment];
}

static void Require(bool ok, string message)
{
    if (ok) return;
    Console.Error.WriteLine(message);
    Environment.Exit(1);
}

file sealed class Pinned(Guid id) : ICurrentMunicipalityAccessor
{
    public Guid MunicipalityId => id;
    public void Set(Guid value) { }
}

file sealed class Caller(Guid id, Guid tenant) : ICurrentUserService
{
    public bool IsAuthenticated => true;
    public Guid? UserId => id;
    public Guid? MunicipalityId => tenant;
    public string? Username => "demo-tenant";
    public string? Role => "SuperAdmin";
    public Guid? CollectorId => null;
    public string? MunicipalityCode => "DEMO";
    public AdminUserDto? GetCurrentUser() => null;
}
