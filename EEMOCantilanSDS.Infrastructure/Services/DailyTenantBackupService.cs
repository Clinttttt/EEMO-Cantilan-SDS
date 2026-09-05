using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Infrastructure.Services;

/// <summary>
/// Takes one backup of every active municipality per day, so an office's records are recoverable without somebody remembering to
/// ask.
/// </summary>
/// <remarks>
/// WHY IT ASKS "HAS TODAY'S BEEN TAKEN?" RATHER THAN SLEEPING FOR A DAY. Always On is switched off on this App Service from time to
/// time to keep the bill down, and a sleeping app runs nothing at all. A service that waited twenty-four hours from startup would
/// either never fire, or fire once per restart and take a backup every time the app woke. So this asks a question instead: does each
/// municipality already have an AUTOMATED backup dated today, in Philippine time? If it does, nothing happens. If it does not, one
/// is taken.
///
/// <para>That makes the schedule self-healing rather than exact. With Always On enabled a backup lands early in the day. With it
/// disabled, one lands shortly after the first time anybody uses the system that day - which is the honest best a sleeping app can
/// do, and better than nothing, which is what the office had before. Missing a day entirely is possible if nobody signs in at all;
/// that is accepted deliberately, because the alternative is paying to keep the app awake.</para>
///
/// <para>NOTHING HERE MAY BREAK THE APP. Every municipality is attempted inside its own try/catch, so one failure cannot stop the
/// rest; the outer loop swallows everything, because an exception escaping a BackgroundService can take the host down with it; and
/// the first pass waits for startup to finish so it never competes with migrations. If this service never runs, the platform is
/// exactly what it was - the office's own backup button and the nightly whole-database dump in CI are untouched.</para>
///
/// <para>It writes through <see cref="ITenantBackupRepository"/> directly rather than the command handler, because that handler
/// requires a signed-in SuperAdmin and a scheduled job has no user. Which tenant it writes for is set explicitly per municipality
/// through <see cref="IRequestTenantScope"/> - the same mechanism the anonymous payment webhook uses to settle under a specific
/// LGU - so the query filters and write-stamping behave as they do in a request.</para>
/// </remarks>
public sealed class DailyTenantBackupService(
    IServiceScopeFactory scopeFactory,
    IClock clock,
    ILogger<DailyTenantBackupService> logger) : BackgroundService
{
    /// <summary>How long to leave the app alone after start, so migrations and warm-up finish first.</summary>
    private static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How often to ask whether today's backup has been taken.
    /// </summary>
    /// <remarks>
    /// The question is two cheap counts per municipality, so asking often costs almost nothing. Half an hour means that when the
    /// app has been asleep, the day's backup follows soon after it wakes rather than waiting for a fixed hour that has passed.
    /// </remarks>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    /// <summary>The note recorded against an automated entry, so the office can see at a glance who asked for it.</summary>
    private const string Note = "Automated daily backup";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupGrace, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOncePerMunicipalityAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Deliberately swallowed. An exception leaving ExecuteAsync can bring the host down, and a failed backup must
                // never cost the office its portal.
                logger.LogError(ex, "The daily tenant backup pass failed. It will be retried on the next interval.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>An active municipality, reduced to what a backup pass needs: which tenant to pin to, and a name for the log.</summary>
    private readonly record struct MunicipalityToBackUp(Guid Id, string Code);

    private async Task RunOncePerMunicipalityAsync(CancellationToken ct)
    {
        var today = clock.PhilippineToday;

        // Read the roster on its own scope, and materialise it before taking any backup: each backup runs in a scope of its own,
        // and holding a context open across all of them would keep one connection for the whole pass.
        List<MunicipalityToBackUp> municipalities;
        using (var rosterScope = scopeFactory.CreateScope())
        {
            var context = rosterScope.ServiceProvider.GetRequiredService<AppDbContext>();
            municipalities = await context.Municipalities
                .AsNoTracking()
                .Where(m => m.IsActive)
                .OrderBy(m => m.Code)
                .Select(m => new MunicipalityToBackUp(m.Id, m.Code))
                .ToListAsync(ct);
        }

        foreach (var municipality in municipalities)
        {
            if (ct.IsCancellationRequested) return;

            try
            {
                await BackUpIfNotDoneTodayAsync(municipality, today, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One municipality's failure is not the others'. Logged and stepped over.
                logger.LogError(ex, "Automated backup failed for {Municipality}.", municipality.Code);
            }
        }
    }

    private async Task BackUpIfNotDoneTodayAsync(MunicipalityToBackUp municipality, DateOnly today, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();

        // Pin this unit of work to the municipality, exactly as the anonymous webhook does. Everything below - the query filter
        // that decides what a snapshot contains, and the stamping that decides who owns the row written - follows from this.
        scope.ServiceProvider.GetRequiredService<IRequestTenantScope>().Use(municipality.Id, municipality.Code);

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Philippine days, because that is the day the office keeps. Comparing UTC dates would take a second backup just after
        // midnight PH and skip the one for the day that had actually begun.
        var alreadyToday = await context.TenantBackups
            .AsNoTracking()
            .Where(b => b.IsAutomated)
            .AnyAsync(b => DateOnly.FromDateTime(b.CreatedAtUtc.Add(PhilippineTime.Offset)) == today, ct);

        if (alreadyToday) return;

        // Nothing to protect yet. A municipality that has been activated but holds no records would otherwise accumulate an empty
        // backup every day, and an empty restore point is a thing an office could restore FROM by mistake.
        var hasRecords = await context.Stalls.AsNoTracking().AnyAsync(ct);
        if (!hasRecords)
            return;

        var repository = scope.ServiceProvider.GetRequiredService<ITenantBackupRepository>();
        var info = await repository.CreateAsync(Note, automated: true, ct);

        logger.LogInformation(
            "Automated backup taken for {Municipality}: {Rows} rows across {Tables} tables.",
            municipality.Code, info.RowCount, info.TableCount);
    }
}
