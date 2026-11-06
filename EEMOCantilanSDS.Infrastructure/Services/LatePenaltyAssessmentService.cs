using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EEMOCantilanSDS.Infrastructure.Services;

/// <summary>
/// Runs <see cref="LatePenaltyAssessor"/> over each active municipality. A charge has one identity, so running often is harmless, and a pass missed while the
/// app slept is caught up by the next. No failure here may reach the host or cost another space its pass.
/// </summary>
public sealed class LatePenaltyAssessmentService(IServiceScopeFactory scopeFactory, ILogger<LatePenaltyAssessmentService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

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
                    logger.LogError(ex, "The late penalty pass failed. It will be retried on the next interval.");
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
                await AssessOfficeAsync(office, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Late penalties failed for {Municipality}.", office.TenantCode);
            }
        }
    }

    private async Task AssessOfficeAsync(Office office, CancellationToken ct)
    {
        IReadOnlyList<Guid> stalls;
        using (var scope = PinnedScope(office))
            stalls = await scope.ServiceProvider.GetRequiredService<LatePenaltyAssessor>().StallsToReviewAsync(ct);

        foreach (var stallId in stalls)
        {
            // A scope per space, because a lost race leaves its context unusable and must not cost the rest of the pass.
            try
            {
                using var scope = PinnedScope(office);
                await scope.ServiceProvider.GetRequiredService<LatePenaltyAssessor>().AssessStallAsync(stallId, ct);
            }
            catch (DbUpdateException)
            {
                // Another pass assessed the same charge first. The next pass reads the result.
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Could not assess late penalties for stall {StallId}.", stallId);
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
