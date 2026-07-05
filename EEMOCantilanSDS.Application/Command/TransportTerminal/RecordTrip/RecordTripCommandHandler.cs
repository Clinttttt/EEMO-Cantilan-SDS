using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Fees;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.TransportTerminal;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.TransportTerminal;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;

public class RecordTripCommandHandler(
    ITrmRepository trmRepo,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    IFeeRateResolver feeRateResolver,
    ITenantContext tenantContext) : IRequestHandler<RecordTripCommand, Result<TrmTripDto>>
{
    public async Task<Result<TrmTripDto>> Handle(RecordTripCommand request, CancellationToken ct)
    {
        // A registered transporter is optional — "Record a Trip" allows ad-hoc/walk-in trips that
        // are NOT added to the permanent roster. When an id is supplied it must resolve.
        TrmTransporter? transporter = null;
        if (request.TransporterId is { } transporterId)
        {
            transporter = await trmRepo.GetTransporterByIdAsync(transporterId, ct);
            if (transporter == null)
                return Result<TrmTripDto>.NotFound();
        }

        // Collectors may only record trips if assigned to the transport terminal; admins are unrestricted.
        if (currentUser.Role == "Collector")
        {
            if (currentUser.CollectorId is not { } actingCollectorId)
                return Result<TrmTripDto>.Forbidden();

            var actingCollector = await collectorRepository.GetByIdAsync(actingCollectorId, ct);
            if (actingCollector is null ||
                !actingCollector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.TRM))
            {
                return Result<TrmTripDto>.Forbidden();
            }
        }

        var tripNumber = await trmRepo.GetNextTripNumberForTodayAsync(ct);

        // Org precedence: a registered transporter's org wins; otherwise the entered org; else default.
        var organization = transporter?.Organization
            ?? (string.IsNullOrWhiteSpace(request.Organization) ? "Non-associated" : request.Organization.Trim());

        // Resolve this municipality's per-trip fee as of the trip's business date (falls back to the
        // ordinance constant, so Cantilan stamps the same ₱30 as before).
        var rateSnapshot = await feeRateResolver.GetSnapshotAsync(ct);
        var occurredAt = request.OccurredAt ?? DateTime.UtcNow;
        var tripBusinessDate = occurredAt.Kind == DateTimeKind.Utc
            ? PhilippineTime.ToPhilippineTime(occurredAt)
            : occurredAt;
        var tripFee = rateSnapshot.Resolve(FeeRateKey.TrmPerTrip, DateOnly.FromDateTime(tripBusinessDate));

        var trip = TrmTrip.Create(
            request.TransporterId,
            tripNumber,
            request.DriverName,
            request.PlateNumber,
            request.Route,
            request.ORNumber,
            organization: organization,
            collectorId: currentUser.CollectorId,
            remarks: request.Remarks,
            createdBy: currentUser.Username ?? "Admin",
            recordedAt: request.OccurredAt,
            fee: tripFee);

        if (request.ClientOperationId is { } clientOpId)
            trip.SetClientOperationId(clientOpId);

        await trmRepo.AddTripAsync(trip, ct);
        await uow.SaveChangesAsync(ct);
        var businessDate = trip.RecordedAt.Kind == DateTimeKind.Utc
            ? PhilippineTime.ToPhilippineTime(trip.RecordedAt)
            : trip.RecordedAt;
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode,
            FacilityCode.TRM,
            businessDate.Year,
            businessDate.Month,
            ct);

        return Result<TrmTripDto>.Success(new TrmTripDto
        {
            Id = trip.Id,
            TransporterId = trip.TransporterId,
            TripNumber = trip.TripNumber,
            DriverName = trip.DriverName,
            Organization = trip.Organization,
            PlateNumber = trip.PlateNumber,
            Route = trip.Route,
            Fee = trip.Fee,
            ORNumber = trip.ORNumber,
            RecordedAt = trip.RecordedAt
        });
    }
}
