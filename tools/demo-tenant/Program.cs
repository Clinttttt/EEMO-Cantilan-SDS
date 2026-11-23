using System.Security.Cryptography;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Notifications;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
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

// A clearly labelled office with realistic months on every Monthly Income line, for showing the system. It takes a new, empty local database and refuses anything else.
// shortcut: the fee, space, fine, terminal and slaughterhouse lines are posted as collections only, so registers kept on their own records
// (Tabo attendance, slaughter transactions, terminal trips, vendor registrations) stay empty. Add those writers when a walkthrough needs them.
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

// A re-seed may keep the sign-in the office already uses; otherwise a new random one.
var password = Environment.GetEnvironmentVariable("STALLTRACK_DEMO_PASSWORD") is { Length: >= 12 } kept ? kept : Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
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
// Official Receipts as printed: eight-digit serials, one booklet run per collector and kind of collection, issued in date order.
// Guid.Empty is the office counter (online payments it receipts).
var receipts = 0;
var series = new Dictionary<(Guid, string), long>();
string OrFor(Guid collector, string kind)
{
    var start = (kind, Array.FindIndex(collectors, c => c.Id == collector)) switch
    {
        ("rent", 0) => 59_727_601L, ("rent", 1) => 60_314_451L,
        ("utility", 0) => 59_805_201L, ("utility", 1) => 60_392_001L,
        ("fees", 0) => 61_020_001L, ("fees", 1) => 61_540_001L,
        _ => 58_100_001L,
    };
    var next = series.GetValueOrDefault((collector, kind), start);
    series[(collector, kind)] = next + 1;
    receipts++;
    return next.ToString();
}
Guid Collector() => collectors[random.Next(collectors.Length)].Id;
DateTime OnDay(DateOnly month, int day) => new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)), 1, 0, 0, DateTimeKind.Utc);

// Row-level receipts are dated when the money came in, or a whole history would read as collected today.
PaymentRecord Paid(Guid stall, decimal rate, DateOnly month, DateTime received, decimal? partial = null)
{
    var row = PaymentRecord.Create(stall, month.Year, month.Month, rate, "demo-tenant");
    var collector = Collector();
    row.RecordPayment(OrFor(collector, "rent"), collector, partial is null ? PaymentStatus.Paid : PaymentStatus.Partial, partial, remarks: "Demo data", updatedBy: "demo-tenant");
    row.BackdateReceipt(received, "demo-tenant");
    return row;
}

var firstNames = new[] { "Maria", "Juan", "Rosa", "Pedro", "Elena", "Ramon", "Teresa", "Carlos", "Liza", "Andres", "Grace", "Roberto", "Nora", "Manuel", "Sofia", "Danilo", "Carmen", "Felix", "Irene", "Oscar" };
var lastNames = new[] { "Santos", "Reyes", "Cruz", "Bautista", "Garcia", "Mendoza", "Flores", "Villanueva", "Castro", "Navarro", "Salazar", "Aquino", "Pascual", "Domingo", "Gonzales", "Mercado" };
var plan = new[]
{
    (Code: FacilityCode.NCC, Name: "Demo Commercial Center", Short: "DCC", Stalls: 40, Rates: new[] { 1200m, 2400m, 3240m }),
    (Code: FacilityCode.TCC, Name: "Demo Trade Center", Short: "DTC", Stalls: 32, Rates: new[] { 2400m, 3600m, 4800m }),
    (Code: FacilityCode.BBQ, Name: "Demo Food Stands", Short: "DFS", Stalls: 16, Rates: new[] { 800m }),
    (Code: FacilityCode.ICE, Name: "Demo Ice Plant", Short: "DIP", Stalls: 4, Rates: new[] { 6000m }),
};

var occupied = new List<(Stall Stall, decimal Rate, string Occupant)>();
var facilities = new Dictionary<FacilityCode, Facility>();
// Rent receipts are written after every history is drawn, in date order, so each booklet's numbers run with the calendar.
var rentPaid = new List<(Guid Stall, decimal Rate, DateOnly Month, DateTime Received, decimal? Partial)>();
foreach (var f in plan)
{
    var facility = Facility.Create(f.Code, f.Name, f.Short, "Demo data.", municipalityId: tenant);
    db.Add(facility);
    facilities[f.Code] = facility;
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
            if (month == thisMonth && random.NextDouble() > .65) continue; // about a third of this month's rent is not in yet
            if (habit >= .65 && random.NextDouble() < .2 && month < thisMonth)
            {
                rentPaid.Add((stall.Id, rate, month, OnDay(month, random.Next(20, 28)), decimal.Round(rate / 2, 2)));
                continue;
            }
            rentPaid.Add((stall.Id, rate, month, OnDay(month, habit >= .65 ? random.Next(16, 28) : random.Next(1, 15)), null));
        }
    }
}
// Nothing this month is dated after today.
foreach (var p in rentPaid.OrderBy(p => p.Received))
    db.Add(Paid(p.Stall, p.Rate, p.Month, p.Received > clock.UtcNow ? clock.UtcNow.Date.AddHours(1) : p.Received, p.Partial));

// Market days: each market space pays its day fee on a Cash Ticket, so no receipt number. Reports date a day by its
// last update, so each is dated the day it was collected, or two years of days would read as collected today.
var market = Facility.Create(FacilityCode.NPM, "Demo Public Market", "DPM", "Demo data.", municipalityId: tenant);
db.Add(market);
// The market's day rate, as the office approves it, so each month bills what its days came to.
db.Add(FacilityRate.Create(FacilityCode.NPM, FeeRateKey.NpmDailyStall, FeeRates.NpmDailyFee, firstMonth, tenant, "demo-tenant"));
var marketDays = 0;
foreach (var (section, letter, count) in new[] { (MarketSection.VegetableArea, "V", 20), (MarketSection.FishSection, "F", 10), (MarketSection.MeatSection, "M", 8) })
{
    for (var n = 1; n <= count; n++)
    {
        var stall = Stall.Create(market.Id, $"{letter}-{n:00}", FeeRates.NpmDailyFee * 30, ApplicableFees.BaseRental, section, municipalityId: tenant);
        var occupant = $"{firstNames[random.Next(firstNames.Length)]} {lastNames[random.Next(lastNames.Length)]}";
        db.AddRange(stall, Contract.Create(stall.Id, occupant, occupant, firstMonth, 5, stall.MonthlyRate, createdBy: "demo-tenant"));
        var turnsUp = random.NextDouble() < .75 ? .92 : .6; // most vendors come nearly every day; some often miss
        for (var day = firstMonth; day < today; day = day.AddDays(1))
        {
            var row = DailyCollection.Create(stall.Id, day, "demo-tenant", FeeRates.NpmDailyFee);
            if (random.NextDouble() <= turnsUp)
            {
                row.MarkPaid(string.Empty, Collector(), updatedBy: "demo-tenant");
                marketDays++;
            }
            // A vendor who did not come in is marked absent and owes nothing for the day; now and then the day is left unpaid and owed.
            else if (random.NextDouble() < .9) row.MarkAbsent("demo-tenant");
            else continue;
            row.UpdatedAt = day.ToDateTime(new TimeOnly(1, 0), DateTimeKind.Utc);
            db.Add(row);
        }
    }
}

// Electricity and water for one rental space in three, billed monthly from meter readings at illustrative rates.
// Each is paid early the next month, except some of the last two months, which are still owed.
var utilityBills = 0;
var utilityPaid = new List<(UtilityBill Bill, DateTime PaidOn)>();
foreach (var (stall, _, _) in occupied.Where((_, i) => i % 3 == 1))
{
    stall.AddUtilityFees(electricity: true, water: true, "demo-tenant");
    decimal kwh = random.Next(1000, 5000), cubic = random.Next(100, 400);
    for (var month = firstMonth; month < thisMonth; month = month.AddMonths(1))
    {
        var (usedKwh, usedCubic) = (random.Next(40, 180), random.Next(3, 15));
        var bill = UtilityBill.Create(stall.Id, month.Year, month.Month, kwh, kwh + usedKwh, 12.50m, cubic, cubic + usedCubic, 35m, "demo-tenant");
        (kwh, cubic) = (kwh + usedKwh, cubic + usedCubic);
        db.Add(bill);
        utilityBills++;
        var paidOn = OnDay(month.AddMonths(1), random.Next(5, 20));
        if (paidOn > clock.UtcNow || month >= thisMonth.AddMonths(-2) && random.NextDouble() < .35) continue;
        utilityPaid.Add((bill, paidOn));
    }
}
foreach (var (bill, paidOn) in utilityPaid.OrderBy(p => p.PaidOn))
{
    var collector = Collector();
    bill.RecordPayment(OrFor(collector, "utility"), OrFor(collector, "utility"), collector, PaymentStatus.Paid, null, PaymentStatus.Paid, null, "Demo data", "demo-tenant");
    var entry = db.Entry(bill);
    entry.Property(b => b.ElecPaidAt).CurrentValue = paidOn;
    entry.Property(b => b.WaterPaidAt).CurrentValue = paidOn;
    entry.Property(b => b.PaidAt).CurrentValue = paidOn;
    bill.UpdatedAt = paidOn;
}

// The collection setup the office made on its own: the market's day fee is receipted as permanent stall rent (rulebook: OR),
// collected on the app; and Jose Ramos covers the Trade Center.
var stallRent = RevenueClassification.Create(RevenueClassificationCodes.PermanentStallRent, tenant, "demo-tenant");
db.AddRange(stallRent,
    RevenueClassificationPolicy.Create(stallRent.Id, firstMonth, "Permanent Stall Rent", RevenueInstrumentType.OfficialReceipt, tenant, "Demo data.", "demo-tenant"));
var marketFee = GovernedService.Create(tenant, CollectorOperationCodes.NpmDaily, "demo.head");
db.AddRange(marketFee, GovernedServiceSetting.Create(tenant, marketFee.Id, today, (GovernedServiceBasis)2, null, null, true, true, "demo.head"));
db.Add(CollectorFacilityAssignment.Create(collectors[0].Id, facilities[FacilityCode.TCC].Id, FacilityCode.TCC));

// The operations this office declared, stored as activation stores them: every catalog operation, listed. Their sources and
// policies are added with their collections below.
foreach (var entry in TenantOperationCatalog.All)
    db.Add(TenantOperationSetting.Create(entry.Code, true, null, "Onboarding", tenant, "demo-tenant"));
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
    var or = OrFor(Guid.Empty, "office");
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

// A Head-set policy with a labelled reference, starting this January, so last year's unpaid months carry no late charges: a 25% surcharge once
// and 2% interest a month on the rent and surcharge, up to 36 months (RA 7160 Sec. 168, ruled 2026-11-24).
db.Add(BillingPolicyVersion.Create(tenant, new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 1, 1),
    BillingPolicy.StallTrackDefaults with { DueDay = 15, GraceDays = 5, PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid, PenaltyValue = 25m, InterestPercent = 2m, InterestMaxMonths = 36, CountsMonthsFrom = new DateOnly(today.Year - 1, 1, 1) },
    "Demo Ordinance No. 1 (illustrative, not a real ordinance)", head.Id, clock.UtcNow));
// The Head's later change, as it was entered on the demo: rent due on the 20th from today.
db.Add(BillingPolicyVersion.Create(tenant, today, today,
    BillingPolicy.StallTrackDefaults with { DueDay = 20, GraceDays = 5, PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid, PenaltyValue = 25m, InterestPercent = 2m, InterestMaxMonths = 36, CountsMonthsFrom = new DateOnly(today.Year - 1, 1, 1) },
    "RA 7160 Sec. 168", head.Id, clock.UtcNow));
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

// Every other Monthly Income line, recorded as the office and collectors record it: one Collection per receipt or cash ticket through
// the shared canonical coordinator, dated the day the money came in. Its own random stream, so the history above stays exactly as it was.
var other = new Random(2027);
var feeLines = new (string Code, string Name, RevenueInstrumentType Instrument, int PerMonth, int Low, int High)[]
{
    (RevenueClassificationCodes.MarketFees, "Market Fees", RevenueInstrumentType.CashTicket, 30, 20, 150),
    (RevenueClassificationCodes.Tabo, "Tabo", RevenueInstrumentType.OfficialReceipt, 8, 300, 1500),
    (RevenueClassificationCodes.FishMeatVendorFee, "Fish / Meat Vendor Fee", RevenueInstrumentType.OfficialReceipt, 20, 100, 600),
    (RevenueClassificationCodes.LandingBerthing, "Landing / Berthing", RevenueInstrumentType.CashTicket, 16, 50, 400),
    (RevenueClassificationCodes.TransportationParking, "Transportation / Parking", RevenueInstrumentType.CashTicket, 30, 20, 120),
    (RevenueClassificationCodes.WeightAndMeasure, "Weight & Measure", RevenueInstrumentType.OfficialReceipt, 20, 50, 500),
    (RevenueClassificationCodes.TransferLargeCattle, "Transfer Large Cattle", RevenueInstrumentType.OfficialReceipt, 5, 200, 600),
    (RevenueClassificationCodes.VegetableFruitSpaceRental, "Vegetable / Fruit Space Rental", RevenueInstrumentType.CashTicket, 24, 30, 200),
    (RevenueClassificationCodes.KanmanggaySpaceRental, "Municipal Space Rental", RevenueInstrumentType.OfficialReceipt, 8, 500, 2000),
    (RevenueClassificationCodes.FiestaArawLotRental, "Event / Temporary Lot Rental", RevenueInstrumentType.OfficialReceipt, 4, 300, 1500),
    (RevenueClassificationCodes.PenaltiesAndFines, "Fines", RevenueInstrumentType.OfficialReceipt, 3, 100, 500),
    (RevenueClassificationCodes.TerminalComfortRoom, "Comfort Room", RevenueInstrumentType.CashTicket, 26, 100, 400),
    (RevenueClassificationCodes.TerminalPullPulVansCargoVans, "Pull Pul Vans, Cargo Vans", RevenueInstrumentType.CashTicket, 26, 200, 800),
    (RevenueClassificationCodes.TerminalTricycad, "Tricycad", RevenueInstrumentType.CashTicket, 26, 100, 500),
    (RevenueClassificationCodes.Slaughterhouse, "Slaughterhouse", RevenueInstrumentType.OfficialReceipt, 16, 150, 900),
};
await using (var work = new AppDbContext(options, pinned))
{
    foreach (var line in feeLines)
    {
        // Late penalties above may already have made the Fines source.
        var source = await work.RevenueClassifications.SingleOrDefaultAsync(x => x.SemanticCode == line.Code)
            ?? work.RevenueClassifications.Add(RevenueClassification.Create(line.Code, tenant, "demo-tenant")).Entity;
        work.Add(RevenueClassificationPolicy.Create(source.Id, firstMonth, line.Name, line.Instrument, tenant, "Demo data.", "demo-tenant"));
    }
    await work.SaveChangesAsync();
}
var postedAt = new Dated();
var otherCollections = 0;
for (var month = firstMonth; month <= thisMonth; month = month.AddMonths(1))
{
    // Up to yesterday, so nothing reads as recorded later than now.
    var length = DateTime.DaysInMonth(month.Year, month.Month);
    var days = month == thisMonth ? today.Day - 1 : length;
    if (days < 1) continue;
    await using var work = new AppDbContext(options, pinned);
    var posting = new CanonicalCollectionPostingCoordinator(work, postedAt);
    var sources = await work.RevenueClassifications.ToDictionaryAsync(x => x.SemanticCode);
    var policies = await work.RevenueClassificationPolicies.Where(x => x.EffectiveDate == firstMonth).ToDictionaryAsync(x => x.RevenueClassificationId);
    // Drawn line by line (so the random stream is unchanged), then posted in time order so each booklet runs with the calendar.
    var batch = new List<(DateTime At, DateOnly Day, RevenueClassification Source, RevenueClassificationPolicy Policy, SourceNativeChargeIntent Charge, bool Terminal,
        decimal Amount, CollectorUser Collector, RevenueInstrumentType Instrument, string? Payer)>();
    foreach (var line in feeLines)
    {
        var (source, policy) = (sources[line.Code], policies[sources[line.Code].Id]);
        TerminalSection? section = line.Code switch
        {
            RevenueClassificationCodes.TerminalComfortRoom => TerminalSection.ComfortRoom,
            RevenueClassificationCodes.TerminalPullPulVansCargoVans => TerminalSection.PullPulVansCargoVans,
            RevenueClassificationCodes.TerminalTricycad => TerminalSection.Tricycad,
            _ => null,
        };
        var terminal = section is not null;
        // The office's charge, as its own posting freezes it: the collector reports read the operation from it.
        var charge = new SourceNativeChargeIntent(terminal ? CollectorOperationCodes.Terminal
            : line.Code == RevenueClassificationCodes.TransportationParking ? CollectorOperationCodes.Transportation : line.Code, Section: section);
        var perMonth = line.Code == RevenueClassificationCodes.FiestaArawLotRental && month.Month is 5 or 12 ? line.PerMonth * 4 : line.PerMonth; // fiesta months
        for (var i = 0; i < perMonth * days / length; i++)
        {
            var day = month.AddDays(other.Next(days));
            var amount = (decimal)other.Next(line.Low, line.High + 1);
            var collector = collectors[other.Next(collectors.Length)];
            var receipted = line.Instrument == RevenueInstrumentType.OfficialReceipt;
            var payer = receipted ? $"{firstNames[other.Next(firstNames.Length)]} {lastNames[other.Next(lastNames.Length)]}" : null;
            var at = day.ToDateTime(new TimeOnly(other.Next(0, 9), other.Next(60)), DateTimeKind.Utc); // 8 a.m. to 5 p.m. in the office
            batch.Add((at, day, source, policy, charge, terminal, amount, collector, line.Instrument, payer));
        }
    }
    foreach (var c in batch.OrderBy(c => c.At))
    {
        postedAt.UtcNow = c.At;
        var snapshot = JsonSerializer.Serialize(new { Version = 1, SourceIdentity = (Guid?)null, PayerSnapshot = c.Payer, charge = c.Charge,
            Calculation = (object?)null, Amount = c.Amount, PolicyId = c.Policy.Id, Instrument = c.Instrument, EffectiveFrom = firstMonth });
        await posting.PostAsync(tenant, Guid.NewGuid(), 1, JsonSerializer.Serialize(new { Version = 1, BusinessDate = c.Day, Amount = c.Amount, Charge = c.Charge, PayerSnapshot = c.Payer }),
            "SourceNativeOfficeCollection", c.Collector.Id.ToString("N"), c.Collector.FullName ?? "Collector", "Collector", c.Day, "demo-tenant",
            [new(c.Source, c.Policy, c.Amount, c.Terminal ? CollectionSourceKind.TerminalSection : null, c.Terminal ? c.Source.Id : null, CalculationSnapshot: snapshot)],
            collectorId: c.Collector.Id, payerName: c.Payer, receiptReference: c.Instrument == RevenueInstrumentType.OfficialReceipt ? OrFor(c.Collector.Id, "fees") : null);
        otherCollections++;
    }
}

await File.WriteAllLinesAsync(args[1],
[
    "Demo sign-in details. Local demo database only.",
    $"Head: demo.head / {password}",
    $"Admin: demo.admin / {password}",
    $"Payors: {string.Join(", ", payors.Select(p => p.Account.Username))} / {password}",
]);
Console.WriteLine($"Demo office seeded: {plan.Sum(f => f.Stalls)} spaces, {occupied.Count} occupied, {receipts} receipts, {marketDays} market days, {utilityBills} utility bills, {otherCollections} other collections, {charges} late penalties, {messages} messages.");

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

/// <summary>A clock set to the moment each historical collection came in.</summary>
file sealed class Dated : IClock
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;
    public DateTime PhilippineNow => DateTime.SpecifyKind(UtcNow.AddHours(8), DateTimeKind.Unspecified);
    public DateOnly PhilippineToday => DateOnly.FromDateTime(PhilippineNow);
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
