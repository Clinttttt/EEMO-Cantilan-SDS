using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;
using EEMOCantilanSDS.Application.Command.TaboanMarket.AddVendor;
using EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;

public sealed class SyncOfflineCollectionsCommandHandler(
    ISender sender,
    ISyncRepository syncRepository,
    ICurrentUserService currentUser)
    : IRequestHandler<SyncOfflineCollectionsCommand, Result<SyncOfflineCollectionsResultDto>>
{
    public async Task<Result<SyncOfflineCollectionsResultDto>> Handle(SyncOfflineCollectionsCommand request, CancellationToken ct)
    {
        // Only collectors sync; attribution comes from the token (the dispatched commands enforce
        // facility assignment per operation).
        if (currentUser.CollectorId is null)
            return Result<SyncOfflineCollectionsResultDto>.Forbidden();

        var results = new List<SyncOperationResultDto>(request.Operations.Count);

        foreach (var op in request.Operations)
        {
            // Idempotent: a record already carrying this client operation id means it was synced.
            if (await syncRepository.IsOperationProcessedAsync(op.ClientOperationId, ct))
            {
                results.Add(new SyncOperationResultDto(op.ClientOperationId, SyncResultStatus.Synced, "Already synced."));
                continue;
            }

            var (ok, statusCode, message) = await DispatchAsync(op, ct);
            var status = ok
                ? SyncResultStatus.Synced
                : IsTransient(statusCode) ? SyncResultStatus.Failed : SyncResultStatus.Rejected;

            results.Add(new SyncOperationResultDto(op.ClientOperationId, status, ok ? null : message));
        }

        var dto = new SyncOfflineCollectionsResultDto(
            results.Count(r => r.Status == SyncResultStatus.Synced),
            results.Count(r => r.Status == SyncResultStatus.Rejected),
            results.Count(r => r.Status == SyncResultStatus.Failed),
            results);

        return Result<SyncOfflineCollectionsResultDto>.Success(dto);
    }

    // Replays one operation via its existing validated command, carrying the offline date, OR and the
    // idempotency key. Returns success + the failure status/message so the caller can classify it.
    private async Task<(bool Ok, int? StatusCode, string? Message)> DispatchAsync(SyncOfflineOperationDto op, CancellationToken ct)
    {
        try
        {
            switch (op.Kind)
            {
                case OfflineOperationKind.NpmDaily:
                {
                    var r = await sender.Send(new RecordDailyCollectionCommand(
                        op.StallId ?? Guid.Empty, op.BusinessDate, op.IsPaid ?? true,
                        op.FishKilos, op.ORNumber, op.ClientOperationId), ct);
                    return (r.IsSuccess, r.StatusCode, r.Error);
                }
                case OfflineOperationKind.MonthlyRental:
                {
                    var r = await sender.Send(new RecordPaymentCommand(
                        op.StallId ?? Guid.Empty, op.BusinessDate.Year, op.BusinessDate.Month,
                        op.Status ?? PaymentStatus.Paid, op.PartialAmount, op.Remarks, op.ORNumber, op.ClientOperationId), ct);
                    return (r.IsSuccess, r.StatusCode, r.Error);
                }
                case OfflineOperationKind.Slaughter:
                {
                    var r = await sender.Send(new RecordSlaughterCommand(
                        op.OwnerName ?? string.Empty, op.BusinessDate, op.ORNumber ?? string.Empty,
                        op.AnimalType ?? Domain.Enums.AnimalType.Hog, op.CustomAnimalType,
                        op.NumberOfHeads ?? 0, op.CustomRate, op.ClientOperationId), ct);
                    return (r.IsSuccess, r.StatusCode, r.Error);
                }
                case OfflineOperationKind.Trip:
                {
                    var occurredAt = op.OccurredAt
                        ?? DateTime.SpecifyKind(op.BusinessDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                    var r = await sender.Send(new RecordTripCommand(
                        op.TransporterId, op.DriverName ?? string.Empty, op.PlateNumber ?? string.Empty,
                        op.Route ?? string.Empty, op.ORNumber ?? string.Empty, op.Remarks, op.Organization,
                        occurredAt, op.ClientOperationId), ct);
                    return (r.IsSuccess, r.StatusCode, r.Error);
                }
                case OfflineOperationKind.TpmVendor:
                {
                    var r = await sender.Send(new AddVendorToMarketDayCommand(
                        op.VendorName ?? string.Empty, op.Goods ?? string.Empty, op.BusinessDate,
                        op.ORNumber, op.ClientOperationId), ct);
                    return (r.IsSuccess, r.StatusCode, r.Error);
                }
                default:
                    return (false, 400, "Unknown operation kind.");
            }
        }
        catch (ValidationException vex)
        {
            // Validation failures are terminal — the queued payload is malformed and won't pass on retry.
            return (false, 400, string.Join("; ", vex.Errors.Select(e => e.ErrorMessage)));
        }
        catch (Exception)
        {
            // Anything else (e.g. a transient DB error) is retryable on the next sync.
            return (false, 500, "Sync error while processing the operation.");
        }
    }

    // 5xx (and unknown) are transient → retry; 4xx are terminal business/validation rejections.
    private static bool IsTransient(int? statusCode) => statusCode is null or >= 500;
}
