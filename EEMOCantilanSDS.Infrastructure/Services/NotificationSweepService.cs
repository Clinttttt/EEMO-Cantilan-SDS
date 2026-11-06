using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Notifications;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Infrastructure.Services;

/// <summary>
/// Runs <see cref="NotificationSweep"/> over each active municipality. A message has one identity per person, so repeating a pass is harmless
/// and an app that slept catches up. No failure here may reach the host or cost another office its pass.
/// </summary>
public sealed class NotificationSweepService(IServiceScopeFactory scopeFactory, ILogger<NotificationSweepService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    // In memory on purpose: a restart repeats one day's reminder pass, and the keys make that a no-op.
    private readonly Dictionary<Guid, DateOnly> _remindedOn = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupGrace, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunPassAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "The notification pass failed. It will be retried on the next interval.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private readonly record struct Office(Guid Id, string TenantCode);

    private async Task RunPassAsync(CancellationToken ct)
    {
        List<Office> offices;
        DateOnly today;
        using (var rosterScope = scopeFactory.CreateScope())
        {
            today = rosterScope.ServiceProvider.GetRequiredService<IClock>().PhilippineToday;
            offices = await rosterScope.ServiceProvider.GetRequiredService<AppDbContext>().Municipalities
                .AsNoTracking()
                .Where(m => m.IsActive)
                .Select(m => new Office(m.Id, m.TenantCode))
                .ToListAsync(ct);
        }

        foreach (var office in offices)
        {
            await GuardedAsync(office, sweep => sweep.RunAsync(ct), ct);
            if (_remindedOn.GetValueOrDefault(office.Id) == today) continue;
            if (await GuardedAsync(office, sweep => sweep.RemindAsync(ct), ct)) _remindedOn[office.Id] = today;
        }
    }

    /// <summary>A scope per step, because a lost race leaves its context unusable and must not cost the next step.</summary>
    private async Task<bool> GuardedAsync(Office office, Func<NotificationSweep, Task<int>> step, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<IRequestTenantScope>().Use(office.Id, office.TenantCode);
            await step(scope.ServiceProvider.GetRequiredService<NotificationSweep>());
            return true;
        }
        catch (DbUpdateException)
        {
            // Another instance sent the same message first. The next pass adds whatever is still missing.
            return false;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "Notifications failed for {Municipality}.", office.TenantCode);
            return false;
        }
    }
}
