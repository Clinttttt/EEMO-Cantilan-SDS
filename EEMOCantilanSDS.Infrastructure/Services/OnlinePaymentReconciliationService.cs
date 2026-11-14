using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Application.Common.Payments;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Infrastructure.Services;

/// <summary>
/// Runs <see cref="OnlinePaymentReconciler"/> over each active municipality's open checkouts. Like the daily backup it does nothing
/// while the app sleeps and catches up when it wakes, and no failure here may reach the host.
/// </summary>
public sealed class OnlinePaymentReconciliationService(
    IServiceScopeFactory scopeFactory,
    ILogger<OnlinePaymentReconciliationService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

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
                    logger.LogError(ex, "The online payment reconciliation pass failed. It will be retried on the next interval.");
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
        using (var rosterScope = scopeFactory.CreateScope())
        {
            offices = await rosterScope.ServiceProvider.GetRequiredService<AppDbContext>().Municipalities
                .AsNoTracking()
                .Where(m => m.IsActive)
                .Select(m => new Office(m.Id, m.TenantCode))
                .ToListAsync(ct);
        }

        foreach (var office in offices)
        {
            try
            {
                await ReconcileOfficeAsync(office, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Online payment reconciliation failed for {Municipality}.", office.TenantCode);
            }
        }
    }

    private async Task ReconcileOfficeAsync(Office office, CancellationToken ct)
    {
        List<Guid> open;
        using (var scope = PinnedScope(office))
        {
            var checkBefore = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow - OnlinePaymentReconciler.CheckAfter;
            var checkouts = await scope.ServiceProvider.GetRequiredService<IOnlinePaymentRepository>()
                .GetUnsettledMerchantCheckoutsAsync(office.Id, OnlinePaymentReconciler.MaxPerPass, ct);
            open = checkouts.Where(t => t.CreatedAt < checkBefore).Select(t => t.Id).Take(OnlinePaymentReconciler.MaxPerPass).ToList();
        }

        foreach (var transactionId in open)
        {
            // A scope per payment, because a lost race leaves its context unusable and must not cost the rest of the pass.
            try
            {
                using var scope = PinnedScope(office);
                await scope.ServiceProvider.GetRequiredService<OnlinePaymentReconciler>().ReconcileAsync(transactionId, ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // The webhook or the payor's return settled it first. The next pass reads the result.
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Could not reconcile online payment {TransactionId}.", transactionId);
            }
        }
    }

    private IServiceScope PinnedScope(Office office)
    {
        var scope = scopeFactory.CreateScope();
        scope.ServiceProvider.GetRequiredService<IRequestTenantScope>().Use(office.Id, office.TenantCode);
        return scope;
    }
}
