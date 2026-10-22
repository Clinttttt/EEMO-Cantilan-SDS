using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Persistence;
using EEMOCantilanSDS.Infrastructure.Persistence.Interceptors;
using EEMOCantilanSDS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;

namespace EEMOCantilanSDS.IntegrationTests;

/// <summary>
/// READ-ONLY investigation of the arrears/delinquency readers against a disposable COPY of the reference data. Skipped in every normal run: it
/// needs STALLTRACK_ARR_CONNECTION (never logged), STALLTRACK_ARR_DATABASE/PORT and refuses the retained municipal database, the accepted NCC
/// preview and the working copy. It reads through the same repository the reports use and writes nothing.
/// </summary>
public sealed class ArrearsInvestigationRun(ITestOutputHelper output)
{
    private sealed class Tenant(Guid id) : ICurrentMunicipalityAccessor
    {
        public Guid MunicipalityId => id;
        public void Set(Guid municipalityId) { }
    }

    [SkippableFact]
    public async Task Compare_the_delinquency_readers_on_the_reference_copy()
    {
        var connection = Environment.GetEnvironmentVariable("STALLTRACK_ARR_CONNECTION");
        var database = Environment.GetEnvironmentVariable("STALLTRACK_ARR_DATABASE");
        var portText = Environment.GetEnvironmentVariable("STALLTRACK_ARR_PORT");
        Skip.If(string.IsNullOrWhiteSpace(connection) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(portText), "Not requested.");
        var port = int.Parse(portText!);
        Assert.DoesNotContain(port, new[] { 5432, 55489, 55491 });
        Assert.DoesNotContain(database!, new[] { "EEMOCantilanSDS.DB", "stalltrack_preview", "stalltrack_workcopy_main" });
        var builder = new NpgsqlConnectionStringBuilder(connection);
        Assert.Equal(database, builder.Database);

        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(new MunicipalityStampInterceptor()).Options;
        await using var lookup = new AppDbContext(options, new Tenant(Guid.Empty));
        var cantilan = await lookup.Municipalities.AsNoTracking().SingleAsync(x => x.Code == "CANTILAN");
        await using var context = new AppDbContext(options, new Tenant(cantilan.Id));
        var repo = new FacilityReportsRepository(context);
        var today = PhilippineTime.Today;
        var (year, month) = (today.Year, today.Month);

        var financial = (await repo.GetDelinquentStallsAsync(null, year, month, includeClosed: true, wholeAccount: true, default)).Where(d => d.MonthsUnpaid >= 1).ToList();
        var queue = (await repo.GetDelinquentStallsAsync(null, year, month, default)).Where(d => d.MonthsUnpaid >= 1).ToList();
        output.WriteLine($"as of {today}: financial(whole account)={financial.Count} outstanding={financial.Sum(d => d.OutstandingBalance):N2}; queue(rolling 12)={queue.Count} outstanding={queue.Sum(d => d.OutstandingBalance):N2}");
        string K(Application.Dtos.Facilities.DelinquentStallDto d) => $"{d.FacilityCode}|{d.StallId}";
        var duplicatesFinancial = financial.GroupBy(K).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        var duplicatesQueue = queue.GroupBy(K).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        output.WriteLine($"duplicate keys: financial={duplicatesFinancial.Count} queue={duplicatesQueue.Count}");
        var onlyQueue = queue.Where(q => financial.All(f => K(f) != K(q))).ToList();
        var onlyFinancial = financial.Where(f => queue.All(q => K(q) != K(f))).ToList();
        output.WriteLine($"in queue only={onlyQueue.Count}; in financial only={onlyFinancial.Count}");
        foreach (var d in onlyQueue) output.WriteLine($"  queue-only: {d.FacilityCode} stall {d.StallNo} [{d.Section}] months={d.MonthsUnpaid} balance={d.OutstandingBalance:N2} lapsed={d.TermLapsed}");
        foreach (var d in onlyFinancial) output.WriteLine($"  financial-only: {d.FacilityCode} stall {d.StallNo} [{d.Section}] months={d.MonthsUnpaid} balance={d.OutstandingBalance:N2} lapsed={d.TermLapsed}");
        var both = financial.Join(queue, K, K, (f, q) => (f, q)).ToList();
        output.WriteLine($"in both={both.Count}; whole-account months>rolling months in {both.Count(x => x.f.MonthsUnpaid > x.q.MonthsUnpaid)}; equal in {both.Count(x => x.f.MonthsUnpaid == x.q.MonthsUnpaid)}");
        // The follow-up queue ALSO labels ended-occupancy balances from the Closed / Inactive register as "delinquent" when they have an elapsed unpaid month.
        var closed = await new StallRepository(context).GetClosedStallAccountsAsync(default);
        var ended = closed.Where(a => a.Uncollected > 0m && a.State != EEMOCantilanSDS.Domain.Enums.InactiveAccountState.Lapsed).ToList();
        output.WriteLine($"closed register: {closed.Count} accounts; non-lapsed with a balance={ended.Count} outstanding={ended.Sum(a => a.Uncollected):N2}; with elapsed unpaid months={ended.Count(a => a.MonthsUnpaid >= 1)}");
        foreach (var a in ended.Where(a => a.MonthsUnpaid >= 1).Take(10)) output.WriteLine($"  ended: {a.FacilityCode} stall {a.StallNo} state={a.State} months={a.MonthsUnpaid} uncollected={a.Uncollected:N2} endedOn={a.OccupancyEndedOn}");
        var monthStart = new DateOnly(year, month, 1);
        var endedThisPeriod = (await new StallRepository(context).GetClosedStallAccountsForPeriodAsync(monthStart, today, default)).Where(a => a.Uncollected > 0m && a.State != EEMOCantilanSDS.Domain.Enums.InactiveAccountState.Lapsed).ToList();
        output.WriteLine($"ended THIS month (what the live queue is handed): {endedThisPeriod.Count}");
        var classified = financial.Where(d => d.Classified).ToList();
        var partition = classified.Select(d => (d, parts: d.ArrearsAmount + d.DelinquentAmount, diff: d.OutstandingBalance - d.ArrearsAmount - d.DelinquentAmount)).ToList();
        output.WriteLine($"classified accounts={classified.Count}: arrears accounts={classified.Count(d => d.ArrearsAmount > 0m || d.ArrearsMonths > 0)} amount={classified.Sum(d => d.ArrearsAmount):N2}; delinquent accounts={classified.Count(d => d.DelinquentMonths >= 1)} amount={classified.Sum(d => d.DelinquentAmount):N2}; both={classified.Count(d => d.ArrearsMonths > 0 && d.DelinquentMonths > 0)}");
        output.WriteLine($"month-sum vs account balance: exact={partition.Count(x => x.diff == 0m)}, differing={partition.Count(x => x.diff != 0m)}, total balance={classified.Sum(d => d.OutstandingBalance):N2}, total of months={partition.Sum(x => x.parts):N2}");
        foreach (var x in partition.Where(x => x.diff != 0m).OrderByDescending(x => Math.Abs(x.diff)).Take(12)) output.WriteLine($"  diff {x.d.FacilityCode} stall {x.d.StallNo} balance={x.d.OutstandingBalance:N2} months={x.d.MonthsUnpaid} arrears={x.d.ArrearsAmount:N2}/{x.d.ArrearsMonths} delinquent={x.d.DelinquentAmount:N2}/{x.d.DelinquentMonths} lapsed={x.d.TermLapsed} diff={x.diff:N2}");
        var byFacility = financial.GroupBy(d => d.FacilityCode).Select(g => $"{g.Key}:{g.Count()}");
        output.WriteLine("financial by facility: " + string.Join(", ", byFacility));
        output.WriteLine("queue by facility: " + string.Join(", ", queue.GroupBy(d => d.FacilityCode).Select(g => $"{g.Key}:{g.Count()}")));
    }
}
