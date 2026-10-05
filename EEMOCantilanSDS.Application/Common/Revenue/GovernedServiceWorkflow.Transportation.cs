using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Domain.Constants;

namespace EEMOCantilanSDS.Application.Common.Revenue;

public sealed partial class GovernedServiceWorkflow
{
    /// <summary>Canonical Transportation receipts only. Legacy trips have their own unchanged history contract.</summary>
    public async Task<Result<TransportationCurrentActivityDto>> GetTransportationCurrentAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // The shared activity workflow owns role/tenant/period checks, source selection and frozen facts.
        var activity = await GetActivityAsync(CollectorOperationCodes.Transportation, from, to, ct);
        if (!activity.IsSuccess) return Result<TransportationCurrentActivityDto>.Failure(activity.Error!, activity.Status);
        var today = BusinessToday;
        var todayActivity = from <= today && today <= to
            ? activity
            : await GetActivityAsync(CollectorOperationCodes.Transportation, today, today, ct);
        if (!todayActivity.IsSuccess) return Result<TransportationCurrentActivityDto>.Failure(todayActivity.Error!, todayActivity.Status);
        var classes = await new VehicleClassWorkflow(db, currentUser, municipality, clock).GetAsync(ct);
        if (!classes.IsSuccess) return Result<TransportationCurrentActivityDto>.Failure(classes.Error!, classes.Status);
        var todayRows = todayActivity.Value!.Where(x => x.BusinessDate == today).ToArray();
        var rows = activity.Value!;
        return Result<TransportationCurrentActivityDto>.Success(new(from, to, today,
            todayRows.Sum(x => x.NetAmount ?? x.Amount), todayRows.Count(x => (x.NetAmount ?? x.Amount) > 0m),
            classes.Value!.Count(x => x.IsActive), rows.Sum(x => x.NetAmount ?? x.Amount),
            rows.Count(x => (x.NetAmount ?? x.Amount) > 0m), rows));
    }
}
