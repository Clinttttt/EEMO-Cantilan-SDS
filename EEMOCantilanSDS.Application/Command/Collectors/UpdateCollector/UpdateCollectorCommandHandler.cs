using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Notifications;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Collectors.UpdateCollector;

public class UpdateCollectorCommandHandler(
    ICollectorRepository collectorRepo,
    ICurrentUserService currentUser,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext,
    IFacilityRepository facilityRepo,
    IPushSender pushSender) : IRequestHandler<UpdateCollectorCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(UpdateCollectorCommand request, CancellationToken cancellationToken)
    {
        var collector = await collectorRepo.GetByIdAsync(request.CollectorId, cancellationToken);
        if (collector is null) return Result<bool>.NotFound();

        // Capture the CURRENT assignments before replacing, so we can push only about NEWLY added facilities.
        var existing = collector.FacilityAssignments.Select(a => a.FacilityCode).ToHashSet();

        collector.UpdateProfile(request.FullName, request.ContactNumber, request.Email, currentUser.Username ?? "Admin");
        await collectorRepo.ReplaceFacilityAssignmentsAsync(request.CollectorId, request.AssignedFacilities, cancellationToken);
        await uow.SaveChangesAsync(cancellationToken);
        await cacheInvalidator.InvalidateReferenceDataAsync(tenantContext.TenantCode, cancellationToken);

        // Best-effort: notify the collector of any facility they were just assigned to. Never affects the save.
        var newlyAdded = request.AssignedFacilities.Where(f => !existing.Contains(f)).Distinct().ToList();
        if (newlyAdded.Count > 0)
        {
            try
            {
                // Use the tenant's own facility names (fallback to the canonical label → Cantilan unchanged).
                var facilityNames = await facilityRepo.GetFacilityNamesAsync(cancellationToken);
                var names = string.Join(", ", newlyAdded.Select(c =>
                    facilityNames.TryGetValue(c, out var fn) && !string.IsNullOrWhiteSpace(fn)
                        ? fn
                        : FacilityDisplayNames.Of(c)));
                var body = newlyAdded.Count == 1
                    ? $"You've been assigned to {names}. Open StallTrack to start collecting."
                    : $"You've been assigned to: {names}.";
                await pushSender.SendToCollectorAsync(request.CollectorId, "New facility assignment", body, data: null, cancellationToken);
            }
            catch { /* push is non-critical; the assignment is already saved */ }
        }

        return Result<bool>.Success(true);
    }
}
