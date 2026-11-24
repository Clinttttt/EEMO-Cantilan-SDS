using System.Security.Cryptography;
using System.Text.Json;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Queries.Auth.GetCurrentUser;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Domain.Entities.Facilities;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Entities.Revenue;
using EEMOCantilanSDS.Domain.Entities.Users;
using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Repositories;
using EEMOCantilanSDS.Infrastructure.Security;
using EEMOCantilanSDS.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

// Sample records for an office that is not live yet, so a walkthrough shows full screens: rent history on the office's own facilities and
// vacant spaces, market days, utility bills, late charges, and collections on the revenue sources the office already set up. It only ever
// ADDS rows: it never changes or deletes a row it did not add (the save is refused if it would), never changes the schema, creates no sign-in
// anyone can use, and sends no message. Every row it adds is written to the record file, and "remove" takes exactly those out again.
// shortcut: registers kept on their own records (Tabo attendance, slaughter, terminal trips) stay empty, as in tools/demo-tenant.
var mode = args.FirstOrDefault();
Require(mode is "seed" or "resume" or "backfill" or "npm" && args.Length == 3 || mode == "remove" && args.Length is 2 or 3,
    "Use: seed <office code> <new record file>   or   resume|backfill|npm <office code> <record file>   or   remove <record file> [--confirm]");
// resume: a seed that stopped after its rental accounts adds the rest (late charges, other collections) to the same record file.
// backfill: like resume, but only the days before each source's first rate rule, under that first rule (no charges, no day posted twice).
var backfill = mode == "backfill";
var npm = mode == "npm";   // npm: sample rent on the office's own monthly-market accounts (see below)
var resume = mode is "resume" or "backfill" or "npm";
var connection = Environment.GetEnvironmentVariable("STALLTRACK_SAMPLE_CONNECTION");
Require(!string.IsNullOrWhiteSpace(connection), "Set STALLTRACK_SAMPLE_CONNECTION.");
if (mode == "remove")
{
    await Removal.RunAsync(connection!, args[1], args.Length == 3 && args[2] == "--confirm");
    return;
}

var recordPath = args[2];
Require(File.Exists(recordPath) == resume, resume ? "No such record file to resume." : "That record file already exists. Each run writes a new one, so its rows can be removed exactly.");
var clock = new SystemClock();
var today = clock.PhilippineToday;
var recorder = new Recorder(recordPath);
var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(new MunicipalityStampInterceptor(), recorder).Options;

Guid tenant;
string officeName;
await using (var look = new AppDbContext(options, new Pinned(Guid.Empty)))
{
    var pending = (await look.Database.GetPendingMigrationsAsync()).ToList();
    Require(pending.Count == 0, $"This database is missing {pending.Count} migration(s), the last {pending.LastOrDefault()}. Deploy first: this tool never changes the schema.");
    var code = args[1].ToUpperInvariant();
    var office = await look.Municipalities.IgnoreQueryFilters().SingleOrDefaultAsync(m => m.Code.ToUpper() == code || m.TenantCode.ToUpper() == code);
    Require(office is not null, $"No office with the code {args[1]}.");
    (tenant, officeName) = (office!.Id, office.Name);
}
var earlier = resume ? File.ReadLines(recordPath).Skip(1).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<Removal.Row>(l)!).ToList() : [];
if (resume) Require(JsonDocument.Parse(File.ReadLines(recordPath).First()).RootElement.GetProperty("office").GetGuid() == tenant, "That record file is for another office.");
else recorder.Begin(tenant, clock.UtcNow);
Console.WriteLine($"Adding sample records to {officeName}. Record file: {recordPath}");

var random = new Random(2026);
await using var db = new AppDbContext(options, new Pinned(tenant));
var pinned = new Pinned(tenant);
var head = await db.AdminUsers.Where(a => a.Role == AdminRole.SuperAdmin).OrderBy(a => a.Username).FirstOrDefaultAsync()
    ?? await db.AdminUsers.OrderBy(a => a.Username).FirstOrDefaultAsync();
Require(head is not null, "This office has no Head account to record its billing rules under.");
var by = head!.Username ?? "office";

// The office's own collectors; two without any usable sign-in when it has none yet.
var collectors = await db.CollectorUsers.Where(c => c.IsActive).OrderBy(c => c.FullName).Take(3).ToArrayAsync();
if (collectors.Length == 0)
{
    var unusable = new IdentityPasswordHasher().Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));   // never written anywhere
    collectors = new[] { ("Jose", "Ramos"), ("Lina", "Torres") }
        .Select(n => CollectorUser.Create($"{n.Item1} {n.Item2}", $"EMP-{RandomNumberGenerator.GetInt32(10_000, 99_999)}",
            $"{n.Item1.ToLowerInvariant()}.{n.Item2.ToLowerInvariant()}{RandomNumberGenerator.GetInt32(10, 99)}", null, null, unusable, tenant))
        .ToArray();
    db.AddRange(collectors.Cast<object>());
}
string By(CollectorUser c) => c.Username ?? by;

var firstMonth = new DateOnly(today.Year - 1, 1, 1);
var thisMonth = new DateOnly(today.Year, today.Month, 1);
var receipts = 0;
// Every receipt number any office already used, so a sample receipt never takes a number from a real booklet.
var usedOrs = new HashSet<string>();
await using (var conn = new NpgsqlConnection(connection))
{
    await conn.OpenAsync();
    var columns = new List<(string Table, string Column)>();
    await using (var find = new NpgsqlCommand("SELECT table_name, column_name FROM information_schema.columns WHERE table_schema = 'public' AND data_type IN ('text', 'character varying') AND (column_name ILIKE '%ORNumber' OR column_name ILIKE '%ReceiptReference%' OR column_name ILIKE '%DocumentNumber%')", conn))
    await using (var reader = await find.ExecuteReaderAsync())
        while (await reader.ReadAsync()) columns.Add((reader.GetString(0), reader.GetString(1)));
    foreach (var (table, column) in columns)
    {
        await using var read = new NpgsqlCommand($"SELECT DISTINCT \"{column}\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL", conn);
        await using var reader = await read.ExecuteReaderAsync();
        while (await reader.ReadAsync()) usedOrs.Add(reader.GetString(0).Trim());
    }
}
var series = new Dictionary<(Guid, string), long>();
string OrFor(Guid collector, string kind)
{
    var start = (kind, Array.FindIndex(collectors, c => c.Id == collector)) switch
    {
        ("rent", 0) => 59_727_601L, ("rent", 1) => 60_314_451L, ("rent", 2) => 60_901_301L,
        ("utility", 0) => 59_805_201L, ("utility", 1) => 60_392_001L, ("utility", 2) => 60_979_001L,
        ("fees", 0) => 61_020_001L, ("fees", 1) => 61_540_001L, ("fees", 2) => 62_060_001L,
        _ => 58_100_001L,
    };
    var next = series.GetValueOrDefault((collector, kind), start);
    while (!usedOrs.Add(next.ToString())) next++;
    series[(collector, kind)] = next + 1;
    receipts++;
    return next.ToString();
}
CollectorUser Collector() => collectors[random.Next(collectors.Length)];
DateTime OnDay(DateOnly month, int day) => new(month.Year, month.Month, Math.Min(day, DateTime.DaysInMonth(month.Year, month.Month)), 1, 0, 0, DateTimeKind.Utc);

PaymentRecord Paid(Guid stall, decimal rate, DateOnly month, DateTime received, decimal? partial)
{
    var row = PaymentRecord.Create(stall, month.Year, month.Month, rate, by);
    var collector = Collector();
    row.RecordPayment(OrFor(collector.Id, "rent"), collector.Id, partial is null ? PaymentStatus.Paid : PaymentStatus.Partial, partial, remarks: null, updatedBy: By(collector));
    row.BackdateReceipt(received, By(collector));
    return row;
}

var firstNames = new[] { "Maria", "Juan", "Rosa", "Pedro", "Elena", "Ramon", "Teresa", "Carlos", "Liza", "Andres", "Grace", "Roberto", "Nora", "Manuel", "Sofia", "Danilo", "Carmen", "Felix", "Irene", "Oscar" };
var lastNames = new[] { "Santos", "Reyes", "Cruz", "Bautista", "Garcia", "Mendoza", "Flores", "Villanueva", "Castro", "Navarro", "Salazar", "Aquino", "Pascual", "Domingo", "Gonzales", "Mercado" };
string Person() => $"{firstNames[random.Next(firstNames.Length)]} {lastNames[random.Next(lastNames.Length)]}";

// Rental facilities: the office's own, with its own spaces first (only ones never let, so no real history is overlaid), then new spaces
// numbered after them. A facility the office does not have is skipped, never created.
var plan = new[]
{
    (Code: FacilityCode.NCC, Stalls: 40, Rates: new[] { 1200m, 2400m, 3240m }),
    (Code: FacilityCode.TCC, Stalls: 32, Rates: new[] { 2400m, 3600m, 4800m }),
    (Code: FacilityCode.BBQ, Stalls: 16, Rates: new[] { 800m }),
    (Code: FacilityCode.ICE, Stalls: 4, Rates: new[] { 6000m }),
};
var facilities = (await db.Facilities.ToListAsync()).GroupBy(f => f.Code).ToDictionary(g => g.Key, g => g.First());
var occupied = new List<(Stall Stall, decimal Rate, string Occupant)>();
var newStalls = new HashSet<Guid>();
var rentPaid = new List<(Guid Stall, decimal Rate, DateOnly Month, DateTime Received, decimal? Partial)>();

async Task<List<Stall>> SpacesAsync(Facility facility, MarketSection? section, string prefix, int count, Func<decimal> rate)
{
    var all = await db.Stalls.Include(s => s.Contracts).Where(s => s.FacilityId == facility.Id).ToListAsync();
    var used = all.Where(s => s.Section == section).Select(s => s.StallNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var spaces = all.Where(s => s.Section == section && s.Status == StallStatus.Active && s.Contracts.Count == 0 && s.MonthlyRate > 0m)
        .OrderBy(s => s.StallNo.Length).ThenBy(s => s.StallNo, StringComparer.OrdinalIgnoreCase).Take(count).ToList();
    for (var n = 1; spaces.Count < count; n++)
    {
        var number = $"{prefix}-{n:00}";
        if (!used.Add(number)) continue;
        var stall = section is { } s
            ? Stall.Create(facility.Id, number, rate(), ApplicableFees.BaseRental, s, municipalityId: tenant)
            : Stall.Create(facility.Id, number, rate(), ApplicableFees.BaseRental, municipalityId: tenant);
        db.Add(stall);
        newStalls.Add(stall.Id);
        spaces.Add(stall);
    }
    return spaces;
}

foreach (var f in plan.Where(_ => !resume))
{
    if (!facilities.TryGetValue(f.Code, out var facility)) { Console.WriteLine($"  {f.Code}: not set up in this office, skipped."); continue; }
    var prefix = string.IsNullOrWhiteSpace(facility.ShortName) ? "S" : facility.ShortName[..1].ToUpperInvariant();
    var spaces = await SpacesAsync(facility, null, prefix, f.Stalls, () => f.Rates[random.Next(f.Rates.Length)]);
    for (var i = 0; i < spaces.Count; i++)
    {
        var stall = spaces[i];
        if ((i + 1) % 11 == 0) continue; // a vacant space
        var rate = stall.MonthlyRate;
        var occupant = Person();
        var effective = (i + 1) % 7 == 0 ? new DateOnly(today.Year - 1, 7, 1) : firstMonth;
        db.Add(Contract.Create(stall.Id, occupant, occupant, effective, 5, rate, createdBy: by));
        occupied.Add((stall, rate, occupant));

        // Most pay on time; some pay late or in part; a few stopped paying, last year or this year.
        var habit = random.NextDouble();
        var stoppedAt = habit >= .95 ? new DateOnly(today.Year - 1, 9, 1) : habit >= .85 ? thisMonth.AddMonths(-random.Next(1, 5)) : (DateOnly?)null;
        for (var month = effective; month <= thisMonth; month = month.AddMonths(1))
        {
            if (stoppedAt is { } stop && month >= stop) break;
            if (month == thisMonth && random.NextDouble() > .65) continue;
            if (habit >= .65 && random.NextDouble() < .2 && month < thisMonth)
            {
                rentPaid.Add((stall.Id, rate, month, OnDay(month, random.Next(20, 28)), decimal.Round(rate / 2, 2)));
                continue;
            }
            rentPaid.Add((stall.Id, rate, month, OnDay(month, habit >= .65 ? random.Next(16, 28) : random.Next(1, 15)), null));
        }
    }
    Console.WriteLine($"  {facility.Name}: {spaces.Count} spaces ({spaces.Count(s => newStalls.Contains(s.Id))} new).");
}
foreach (var p in rentPaid.OrderBy(p => p.Received))
    db.Add(Paid(p.Stall, p.Rate, p.Month, p.Received > clock.UtcNow ? clock.UtcNow.Date.AddHours(1) : p.Received, p.Partial));
await db.SaveChangesAsync();

// Market days on the office's own day rate; skipped when the market or its rate is not set up.
var marketDays = 0;
var dayRate = await db.FacilityRates.Where(r => r.FacilityCode == FacilityCode.NPM && r.RateKey == FeeRateKey.NpmDailyStall && r.EffectiveDate <= today)
    .OrderByDescending(r => r.EffectiveDate).FirstOrDefaultAsync();
if (!resume && facilities.TryGetValue(FacilityCode.NPM, out var market) && !market.IsMonthlyRentMarket && dayRate is { Amount: > 0m })
{
    var from = dayRate.EffectiveDate > firstMonth ? dayRate.EffectiveDate : firstMonth;
    foreach (var (section, letter, count) in new[] { (MarketSection.VegetableArea, "V", 20), (MarketSection.FishSection, "F", 10), (MarketSection.MeatSection, "M", 8) })
    {
        foreach (var stall in await SpacesAsync(market, section, letter, count, () => dayRate.Amount * 30))
        {
            var occupant = Person();
            db.Add(Contract.Create(stall.Id, occupant, occupant, from, 5, stall.MonthlyRate, createdBy: by));
            var turnsUp = random.NextDouble() < .75 ? .92 : .6;
            for (var day = from; day < today; day = day.AddDays(1))
            {
                var row = DailyCollection.Create(stall.Id, day, by, dayRate.Amount);
                if (random.NextDouble() <= turnsUp)
                {
                    var collector = Collector();
                    row.MarkPaid(string.Empty, collector.Id, updatedBy: By(collector));
                    marketDays++;
                }
                else if (random.NextDouble() < .9) row.MarkAbsent(by);
                else continue;
                row.UpdatedAt = day.ToDateTime(new TimeOnly(1, 0), DateTimeKind.Utc);
                db.Add(row);
            }
        }
        await db.SaveChangesAsync();
    }
}
else if (!resume) Console.WriteLine("  Market days skipped: no day-billed market with a day rate in this office.");

// Electricity and water for one new space in three (an existing space's meters are the office's to set up).
var utilityBills = 0;
var utilityPaid = new List<(UtilityBill Bill, DateTime PaidOn)>();
foreach (var (stall, _, _) in occupied.Where(o => newStalls.Contains(o.Stall.Id)).Where((_, i) => i % 3 == 1))
{
    stall.AddUtilityFees(electricity: true, water: true, by);
    decimal kwh = random.Next(1000, 5000), cubic = random.Next(100, 400);
    for (var month = firstMonth; month < thisMonth; month = month.AddMonths(1))
    {
        var (usedKwh, usedCubic) = (random.Next(40, 180), random.Next(3, 15));
        var bill = UtilityBill.Create(stall.Id, month.Year, month.Month, kwh, kwh + usedKwh, 12.50m, cubic, cubic + usedCubic, 35m, by);
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
    bill.RecordPayment(OrFor(collector.Id, "utility"), OrFor(collector.Id, "utility"), collector.Id, PaymentStatus.Paid, null, PaymentStatus.Paid, null, null, By(collector));
    var entry = db.Entry(bill);
    entry.Property(b => b.ElecPaidAt).CurrentValue = paidOn;
    entry.Property(b => b.WaterPaidAt).CurrentValue = paidOn;
    entry.Property(b => b.PaidAt).CurrentValue = paidOn;
    bill.UpdatedAt = paidOn;
}
await db.SaveChangesAsync();

// npm: the office's own monthly-npmMarket accounts get sample rent, by the same habits, only on months with no rent row yet. Every row is
// recorded and "remove" deletes exactly those; the office's own rows and contracts are never changed.
var npmStalls = new List<Guid>();
if (npm)
{
    if (!facilities.TryGetValue(FacilityCode.NPM, out var npmMarket) || npmMarket.Archetype != BillingArchetype.MonthlyRental)
        Console.WriteLine("  No monthly-billed market in this office; nothing to add.");
    else
    {
        var marketStalls = await db.Stalls.Include(s => s.Contracts).Where(s => s.FacilityId == npmMarket.Id && s.Status == StallStatus.Active).ToListAsync();
        var ids = marketStalls.Select(s => s.Id).ToList();
        var hasRow = (await db.PaymentRecords.Where(p => ids.Contains(p.StallId)).Select(p => new { p.StallId, p.BillingYear, p.BillingMonth }).ToListAsync())
            .Select(p => (p.StallId, p.BillingYear, p.BillingMonth)).ToHashSet();
        var marketPaid = new List<(Guid Stall, decimal Rate, DateOnly Month, DateTime Received, decimal? Partial)>();
        foreach (var stall in marketStalls)
        {
            if (stall.Occupancies(today).LastOrDefault(o => o.IsCurrent) is not { } held) continue;
            npmStalls.Add(stall.Id);
            var from = new DateOnly(held.Start.Year, held.Start.Month, 1);
            if (from < firstMonth) from = firstMonth;
            var habit = random.NextDouble();
            var stoppedAt = habit >= .9 ? thisMonth.AddMonths(-random.Next(1, 6)) : (DateOnly?)null;
            for (var month = from; month <= thisMonth; month = month.AddMonths(1))
            {
                if (stoppedAt is { } stop && month >= stop) break;
                if (hasRow.Contains((stall.Id, month.Year, month.Month))) continue;
                if (month == thisMonth && random.NextDouble() > .65) continue;
                var rate = stall.MonthlyRentFor(month.Year, month.Month, today);
                if (rate <= 0m) continue;
                var partial = habit >= .65 && month < thisMonth && random.NextDouble() < .2 ? decimal.Round(rate / 2, 2) : (decimal?)null;
                marketPaid.Add((stall.Id, rate, month, OnDay(month, habit >= .65 ? random.Next(16, 28) : random.Next(1, 15)), partial));
            }
        }
        foreach (var p in marketPaid.OrderBy(p => p.Received))
            db.Add(Paid(p.Stall, p.Rate, p.Month, p.Received > clock.UtcNow ? clock.UtcNow.Date.AddHours(1) : p.Received, p.Partial));
        await db.SaveChangesAsync();
        Console.WriteLine($"  {npmMarket.Name}: {marketPaid.Count} rent months on {npmStalls.Count} of the office's own accounts.");
    }
}

// Late charges need billing rules. The office's own when it has set them; otherwise the ruled ones (25% surcharge once per late month, 2%
// interest a month on rent and surcharge up to 36 months, RA 7160 Sec. 168) from the first sample month, so every late month carries both.
// "remove" takes these rules out again. Backdated on purpose: the domain refuses a past start for a real policy, so "today" is passed as that day.
var rulesAdded = false;
var versions = await db.BillingPolicyVersions.ToListAsync();
if (versions.Count == 0)
{
    db.Add(BillingPolicyVersion.Create(tenant, firstMonth, firstMonth,
        BillingPolicy.StallTrackDefaults with { DueDay = 15, GraceDays = 5, PenaltyBasis = LatePenaltyBasis.PercentOfUnpaid, PenaltyValue = 25m, InterestPercent = 2m, InterestMaxMonths = 36, CountsMonthsFrom = firstMonth },
        "RA 7160 Sec. 168", head.Id, clock.UtcNow));
    await db.SaveChangesAsync();
    rulesAdded = true;
}
else if (BillingPolicyVersion.InForce(versions, today) is not { InterestPercent: > 0m })
    Console.WriteLine("  WARNING: this office's own billing rules charge no interest, so most sample accounts show no late charges. They were left as they are.");
var charges = 0;
var sampleContracts = earlier.Where(r => r.T == "Contracts").Select(r => Guid.Parse(r.V[0])).ToList();
var chargeStalls = npm ? npmStalls : backfill ? new List<Guid>() : resume ? await db.Contracts.Where(c => sampleContracts.Contains(c.Id)).Select(c => c.StallId).Distinct().ToListAsync() : occupied.Select(o => o.Stall.Id).ToList();
foreach (var stallId in chargeStalls)
{
    // The office's own late-charge job may charge the same space at the same moment (same charge id): its charge stands, nothing of ours is saved.
    try
    {
        await using var work = new AppDbContext(options, pinned);
        var reports = new FacilityReportsRepository(work);
        var policy = new BillingPolicyWorkflow(work, new Caller(head.Id, tenant, by), pinned, clock);
        charges += await new LatePenaltyAssessor(work, reports, new BillingRules(policy, reports, work, clock), pinned, clock).AssessStallAsync(stallId);
    }
    catch (Exception e) when (e is DbUpdateException or InvalidOperationException) { Console.WriteLine($"  Charges on one space left to the office's own job ({e.GetBaseException().Message.Split('\n')[0]})."); }
}

// Collections on the revenue sources the office already set up, under the policy in force on each day; a source with none is skipped.
var other = new Random(2027);
var feeLines = new (string Code, int PerMonth, int Low, int High)[]
{
    (RevenueClassificationCodes.MarketFees, 30, 20, 150), (RevenueClassificationCodes.Tabo, 8, 300, 1500),
    (RevenueClassificationCodes.FishMeatVendorFee, 20, 100, 600), (RevenueClassificationCodes.LandingBerthing, 16, 50, 400),
    (RevenueClassificationCodes.TransportationParking, 30, 20, 120), (RevenueClassificationCodes.WeightAndMeasure, 20, 50, 500),
    (RevenueClassificationCodes.TransferLargeCattle, 5, 200, 600), (RevenueClassificationCodes.VegetableFruitSpaceRental, 24, 30, 200),
    (RevenueClassificationCodes.KanmanggaySpaceRental, 8, 500, 2000), (RevenueClassificationCodes.FiestaArawLotRental, 4, 300, 1500),
    (RevenueClassificationCodes.PenaltiesAndFines, 3, 100, 500), (RevenueClassificationCodes.TerminalComfortRoom, 26, 100, 400),
    (RevenueClassificationCodes.TerminalPullPulVansCargoVans, 26, 200, 800), (RevenueClassificationCodes.TerminalTricycad, 26, 100, 500),
    (RevenueClassificationCodes.Slaughterhouse, 16, 150, 900),
};
var postedAt = new Dated();
var otherCollections = 0;
var skippedSources = new HashSet<string>();
// Backfill: a collection may not predate its rule (Collection.Post), so each source whose first rule starts after the sample's first
// month gets one sample rule from that month, same receipt type, recorded like every sample row and removed with them.
var backfillBefore = new Dictionary<Guid, DateOnly>();
if (backfill)
{
    await using var setup = new AppDbContext(options, pinned);
    var codes = feeLines.Select(l => l.Code).ToHashSet();
    var firstRules = (await setup.RevenueClassificationPolicies
            .Where(p => p.BusinessContext == RevenuePolicyContext.Default && p.PermittedInstrumentType != null).ToListAsync())
        .GroupBy(p => p.RevenueClassificationId).Select(g => g.MinBy(p => p.EffectiveDate)!).ToList();
    var bySource = (await setup.RevenueClassifications.ToListAsync()).Where(c => codes.Contains(c.SemanticCode)).Select(c => c.Id).ToHashSet();
    foreach (var first in firstRules.Where(r => bySource.Contains(r.RevenueClassificationId) && r.EffectiveDate > firstMonth))
    {
        backfillBefore[first.RevenueClassificationId] = first.EffectiveDate;
        setup.Add(RevenueClassificationPolicy.Create(first.RevenueClassificationId, firstMonth, first.DisplayName, first.PermittedInstrumentType,
            tenant, "Sample history before the office's first rule; removed with the sample records.", "StallTrack sample"));
    }
    await setup.SaveChangesAsync();
    Console.WriteLine($"  Earlier rules added for {backfillBefore.Count} sources, from {firstMonth:MMMM yyyy}.");
}
for (var month = firstMonth; !npm && month <= thisMonth; month = month.AddMonths(1))
{
    var length = DateTime.DaysInMonth(month.Year, month.Month);
    var days = month == thisMonth ? today.Day - 1 : length;
    if (days < 1) continue;
    await using var work = new AppDbContext(options, pinned);
    var posting = new CanonicalCollectionPostingCoordinator(work, postedAt);
    var sources = (await work.RevenueClassifications.ToListAsync()).GroupBy(x => x.SemanticCode).ToDictionary(g => g.Key, g => g.First());
    var policies = await work.RevenueClassificationPolicies.Where(p => p.BusinessContext == RevenuePolicyContext.Default && p.PermittedInstrumentType != null).ToListAsync();
    var batch = new List<(DateTime At, DateOnly Day, RevenueClassification Source, RevenueClassificationPolicy Policy, SourceNativeChargeIntent Charge, bool Terminal, decimal Amount, CollectorUser Collector, string? Payer)>();
    foreach (var line in feeLines)
    {
        TerminalSection? section = line.Code switch
        {
            RevenueClassificationCodes.TerminalComfortRoom => TerminalSection.ComfortRoom,
            RevenueClassificationCodes.TerminalPullPulVansCargoVans => TerminalSection.PullPulVansCargoVans,
            RevenueClassificationCodes.TerminalTricycad => TerminalSection.Tricycad,
            _ => null,
        };
        var terminal = section is not null;
        var charge = new SourceNativeChargeIntent(terminal ? CollectorOperationCodes.Terminal
            : line.Code == RevenueClassificationCodes.TransportationParking ? CollectorOperationCodes.Transportation : line.Code, Section: section);
        var perMonth = line.Code == RevenueClassificationCodes.FiestaArawLotRental && month.Month is 5 or 12 ? line.PerMonth * 4 : line.PerMonth;
        for (var i = 0; i < perMonth * days / length; i++)
        {
            var day = month.AddDays(other.Next(days));
            var amount = (decimal)other.Next(line.Low, line.High + 1);
            var collector = collectors[other.Next(collectors.Length)];
            var payer = $"{firstNames[other.Next(firstNames.Length)]} {lastNames[other.Next(lastNames.Length)]}";
            var at = day.ToDateTime(new TimeOnly(other.Next(0, 9), other.Next(60)), DateTimeKind.Utc);
            var inForce = sources.TryGetValue(line.Code, out var source)
                ? policies.Where(p => p.RevenueClassificationId == source.Id && p.EffectiveDate <= day).MaxBy(p => p.EffectiveDate)
                : null;
            // Backfill takes only the days before the office's own first rule, under the sample rule added for them.
            var policy = backfill && (source is null || day >= backfillBefore.GetValueOrDefault(source.Id)) ? null : inForce;
            if (policy is null) { if (inForce is null) skippedSources.Add(line.Code); continue; }
            batch.Add((at, day, source!, policy, charge, terminal, amount, collector, policy.PermittedInstrumentType == RevenueInstrumentType.OfficialReceipt ? payer : null));
        }
    }
    foreach (var c in batch.OrderBy(c => c.At))
    {
        postedAt.UtcNow = c.At;
        var instrument = c.Policy.PermittedInstrumentType!.Value;
        var snapshot = JsonSerializer.Serialize(new { Version = 1, SourceIdentity = (Guid?)null, PayerSnapshot = c.Payer, charge = c.Charge,
            Calculation = (object?)null, Amount = c.Amount, PolicyId = c.Policy.Id, Instrument = instrument, EffectiveFrom = c.Policy.EffectiveDate });
        await posting.PostAsync(tenant, Guid.NewGuid(), 1, JsonSerializer.Serialize(new { Version = 1, BusinessDate = c.Day, Amount = c.Amount, Charge = c.Charge, PayerSnapshot = c.Payer }),
            "SourceNativeOfficeCollection", c.Collector.Id.ToString("N"), c.Collector.FullName ?? "Collector", "Collector", c.Day, By(c.Collector),
            [new(c.Source, c.Policy, c.Amount, c.Terminal ? CollectionSourceKind.TerminalSection : null, c.Terminal ? c.Source.Id : null, CalculationSnapshot: snapshot)],
            collectorId: c.Collector.Id, payerName: c.Payer, receiptReference: instrument == RevenueInstrumentType.OfficialReceipt ? OrFor(c.Collector.Id, "fees") : null);
        otherCollections++;
    }
    Console.WriteLine($"  {month:MMMM yyyy}: {batch.Count} collections.");
}
if (skippedSources.Count > 0) Console.WriteLine($"  Sources without a policy in this office, skipped: {string.Join(", ", skippedSources.Order())}.");

// Read back: every Billing Register row must state what its Statement of Account states.
var mismatches = 0;
await using (var read = new AppDbContext(options, pinned))
{
    var reports = new FacilityReportsRepository(read);
    foreach (var f in plan.Select(p => p.Code).Append(FacilityCode.NPM).Where(c => facilities.ContainsKey(c) && (c != FacilityCode.NPM || npmStalls.Count > 0)))
    {
        var register = await reports.GetBillingRegisterAsync(f, CancellationToken.None);
        foreach (var row in register?.Rows ?? [])
        {
            var s = await reports.GetStallStatementAsync(row.StallId, CancellationToken.None);
            if (s is null || s.TotalDue != row.TotalDue || s.PreviousBalance != row.PreviousBalance || s.PenaltiesDue != row.PenaltiesDue) mismatches++;
        }
        Console.WriteLine($"  {facilities[f].Name} register: {register?.Rows.Count ?? 0} accounts, total due {register?.Rows.Sum(r => r.TotalDue) ?? 0m:N2}.");
    }
}
Console.WriteLine($"Done: {occupied.Count} rental accounts, {receipts} receipts, {marketDays} market days, {utilityBills} utility bills, {otherCollections} other collections, " +
    $"{charges} late charges{(rulesAdded ? " (billing rules added)" : "")}. Register vs statements: {mismatches} mismatches. {recorder.Count} rows recorded in {recordPath}.");

static void Require(bool ok, string message)
{
    if (ok) return;
    Console.Error.WriteLine(message);
    Environment.Exit(1);
}

/// <summary>
/// Writes every row a save adds to the record file once it is saved, and refuses a save that would change or delete a row this run did not add.
/// </summary>
file sealed class Recorder(string path) : SaveChangesInterceptor
{
    private readonly HashSet<string> _ours = [];
    private List<EntityEntry> _added = [];
    public int Count { get; private set; }

    public void Begin(Guid office, DateTime at) => File.WriteAllText(path, JsonSerializer.Serialize(new { office, seededAt = at }) + "\n");

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context!);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        Capture(eventData.Context!);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Write();
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        Write();
        return ValueTask.FromResult(result);
    }

    private void Capture(DbContext context)
    {
        var entries = context.ChangeTracker.Entries().ToList();
        var foreign = entries.Where(e => e.State is EntityState.Modified or EntityState.Deleted && !e.Metadata.IsOwned() && !_ours.Contains(Key(e)))
            .Select(e => e.Metadata.ClrType.Name).Distinct().ToList();
        if (foreign.Count > 0)
            throw new InvalidOperationException($"Refused: this save would change rows this tool did not add ({string.Join(", ", foreign)}). Nothing in it was written.");
        _added = entries.Where(e => e.State == EntityState.Added && !e.Metadata.IsOwned() && e.Metadata.GetTableName() is not null).ToList();
    }

    private void Write()
    {
        var lines = new List<string>();
        foreach (var e in _added)
        {
            var key = Key(e);
            if (!_ours.Add(key)) continue;
            var store = StoreObjectIdentifier.Table(e.Metadata.GetTableName()!, e.Metadata.GetSchema());
            var props = e.Metadata.FindPrimaryKey()!.Properties;
            lines.Add(JsonSerializer.Serialize(new Removal.Row(e.Metadata.GetTableName()!, e.Metadata.GetSchema(),
                props.Select(p => p.GetColumnName(store)!).ToArray(),
                props.Select(p => Convert.ToString(e.Property(p.Name).CurrentValue, System.Globalization.CultureInfo.InvariantCulture)!).ToArray(),
                props.Select(p => Removal.TypeOf(p.ClrType)).ToArray())));
        }
        _added = [];
        Count += lines.Count;
        if (lines.Count > 0) File.AppendAllLines(path, lines);
    }

    private static string Key(EntityEntry e) => e.Metadata.GetTableName() + ":" + string.Join("|",
        e.Metadata.FindPrimaryKey()!.Properties.Select(p => Convert.ToString(e.Property(p.Name).CurrentValue, System.Globalization.CultureInfo.InvariantCulture)));
}

/// <summary>
/// Takes out exactly what one seed run added, newest first, plus what the office later recorded on those sample accounts (payments, market days,
/// utility bills and charges on them) and the late charges the sample billing rules made elsewhere. A dry run unless --confirm.
/// </summary>
static class Removal
{
    public sealed record Row(string T, string? S, string[] C, string[] V, string[] Y);

    public static string TypeOf(Type type) => (Nullable.GetUnderlyingType(type) ?? type) switch
    {
        var t when t == typeof(Guid) => "uuid",
        var t when t == typeof(int) => "int",
        var t when t == typeof(long) => "bigint",
        _ => "text",
    };

    private static object Value(string type, string value) => type switch
    {
        "uuid" => Guid.Parse(value),
        "int" => int.Parse(value),
        "bigint" => long.Parse(value),
        _ => value,
    };

    private static string Name(string? schema, string table) => schema is null ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";

    public static async Task RunAsync(string connection, string path, bool confirm)
    {
        var lines = await File.ReadAllLinesAsync(path);
        var header = JsonDocument.Parse(lines[0]).RootElement;
        var office = header.GetProperty("office").GetGuid();
        var seededAt = header.GetProperty("seededAt").GetDateTime();
        var rows = lines.Skip(1).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<Row>(l)!).ToList();
        Guid[] Ids(string table) => rows.Where(r => r.T == table && r.C.Length == 1 && r.Y[0] == "uuid").Select(r => Guid.Parse(r.V[0])).ToArray();

        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        async Task<int> Run(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = new NpgsqlCommand(sql, db, tx);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            return await command.ExecuteNonQueryAsync();
        }

        // A charge's history rows point at it, so they go first.
        async Task<int> Charges(string where, params (string Name, object Value)[] parameters)
        {
            await Run($"DELETE FROM \"FineAssessmentEvents\" WHERE \"AssessmentId\" IN (SELECT \"Id\" FROM \"FineAssessments\" WHERE {where})", parameters);
            return await Run($"DELETE FROM \"FineAssessments\" WHERE {where}", parameters);
        }

        var contracts = Ids("Contracts");
        var stalls = new List<Guid>();
        await using (var command = new NpgsqlCommand("SELECT \"StallId\" FROM \"Contracts\" WHERE \"Id\" = ANY(@ids)", db, tx))
        {
            command.Parameters.AddWithValue("ids", contracts);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) stalls.Add(reader.GetGuid(0));
        }
        var space = stalls.Distinct().ToArray();
        var report = new List<string>
        {
            $"Charges on sample accounts: {await Charges("\"MunicipalityId\" = @o AND \"ContractId\" = ANY(@c)", ("o", office), ("c", contracts))}",
            $"Rent rows on sample spaces since the seed: {await Run("DELETE FROM \"PaymentRecords\" WHERE \"MunicipalityId\" = @o AND \"StallId\" = ANY(@s) AND \"CreatedAt\" >= @at", ("o", office), ("s", space), ("at", seededAt))}",
            $"Market days on sample spaces since the seed: {await Run("DELETE FROM \"DailyCollections\" WHERE \"MunicipalityId\" = @o AND \"StallId\" = ANY(@s) AND \"CreatedAt\" >= @at", ("o", office), ("s", space), ("at", seededAt))}",
            $"Utility bills on sample spaces since the seed: {await Run("DELETE FROM \"UtilityBills\" WHERE \"MunicipalityId\" = @o AND \"StallId\" = ANY(@s) AND \"CreatedAt\" >= @at", ("o", office), ("s", space), ("at", seededAt))}",
        };
        // The sample billing rules charged other accounts too while they were in force; the office's own rules, recorded later, are left alone.
        if (Ids("BillingPolicyVersions") is { Length: > 0 } rules)
        {
            var later = (long)(await new NpgsqlCommand("SELECT count(*) FROM \"BillingPolicyVersions\" WHERE \"MunicipalityId\" = @o AND NOT (\"Id\" = ANY(@r))", db, tx)
                { Parameters = { new("o", office), new("r", rules) } }.ExecuteScalarAsync())!;
            report.Add(later == 0
                ? $"Late charges made under the sample billing rules: {await Charges("\"MunicipalityId\" = @o AND \"PenaltyCode\" IN ('LATE_SURCHARGE', 'LATE_INTEREST') AND \"RecordedAtUtc\" >= @at", ("o", office), ("at", seededAt))}"
                : "Late charges on other accounts kept: the office has recorded its own billing rules since.");
        }

        // A table goes before every table it points at, so no row is deleted while another still refers to it.
        await using var modelContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options, new Pinned(office));
        var refs = modelContext.Model.GetEntityTypes().Where(e => e.GetTableName() is not null)
            .SelectMany(e => e.GetForeignKeys().Select(fk => (Child: e.GetTableName()!, Parent: fk.PrincipalEntityType.GetTableName())))
            .Where(x => x.Parent is not null && x.Child != x.Parent).Distinct().ToList();
        var left = rows.Select(r => r.T).Distinct().ToList();
        var order = new List<string>();
        while (left.Count > 0)
        {
            var next = left.FirstOrDefault(t => !refs.Any(x => x.Parent == t && left.Contains(x.Child))) ?? left[0];
            order.Add(next);
            left.Remove(next);
        }

        var removed = 0;
        foreach (var name in order)
            foreach (var table in rows.Where(r => r.T == name).GroupBy(r => (r.T, r.S, Columns: string.Join(",", r.C))))
            {
                var first = table.First();
                if (first.C.Length == 1)
                {
                    var values = table.Select(r => Value(r.Y[0], r.V[0])).ToArray();
                    await using var command = new NpgsqlCommand($"DELETE FROM {Name(first.S, first.T)} WHERE \"{first.C[0]}\" = ANY(@v)", db, tx);
                    command.Parameters.Add(first.Y[0] switch
                    {
                        "uuid" => new NpgsqlParameter("v", values.Cast<Guid>().ToArray()),
                        "int" => new NpgsqlParameter("v", values.Cast<int>().ToArray()),
                        "bigint" => new NpgsqlParameter("v", values.Cast<long>().ToArray()),
                        _ => new NpgsqlParameter("v", values.Cast<string>().ToArray()),
                    });
                    removed += await command.ExecuteNonQueryAsync();
                }
                else
                    foreach (var r in table)
                    {
                        await using var command = new NpgsqlCommand($"DELETE FROM {Name(r.S, r.T)} WHERE " + string.Join(" AND ", r.C.Select((c, i) => $"\"{c}\" = @p{i}")), db, tx);
                        for (var i = 0; i < r.C.Length; i++) command.Parameters.AddWithValue($"p{i}", Value(r.Y[i], r.V[i]));
                        removed += await command.ExecuteNonQueryAsync();
                    }
            }
        report.Add($"Rows the seed added: {removed} of {rows.Count}");

        foreach (var line in report) Console.WriteLine("  " + line);
        if (confirm)
        {
            await tx.CommitAsync();
            Console.WriteLine("Removed.");
        }
        else
        {
            await tx.RollbackAsync();
            Console.WriteLine("Dry run: nothing was removed. Run again with --confirm to remove.");
        }
    }
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

file sealed class Caller(Guid id, Guid tenant, string username) : ICurrentUserService
{
    public bool IsAuthenticated => true;
    public Guid? UserId => id;
    public Guid? MunicipalityId => tenant;
    public string? Username => username;
    public string? Role => "SuperAdmin";
    public Guid? CollectorId => null;
    public string? MunicipalityCode => null;
    public AdminUserDto? GetCurrentUser() => null;
}
