using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.TransportTerminal.SaveTripOrNumber;

public class SaveTripOrNumberCommandHandler(
    ITrmRepository trmRepo,
    IUnitOfWork uow,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<SaveTripOrNumberCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SaveTripOrNumberCommand request, CancellationToken ct)
    {
        var trip = await trmRepo.GetTripByIdAsync(request.TripId, ct);
        if (trip == null)
            return Result<bool>.NotFound();

        trip.UpdateORNumber(request.ORNumber, "Admin");
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

        return Result<bool>.Success(true);
    }
}
