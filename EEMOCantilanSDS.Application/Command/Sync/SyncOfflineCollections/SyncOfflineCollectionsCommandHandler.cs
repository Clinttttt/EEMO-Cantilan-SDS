using EEMOCantilanSDS.Application.Command.DailyCollections.RecordDailyCollection;
using EEMOCantilanSDS.Application.Command.Payments.RecordPayment;
using EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;
using EEMOCantilanSDS.Application.Command.TaboanMarket.AddVendor;
using EEMOCantilanSDS.Application.Command.TransportTerminal.RecordTrip;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Dtos.Mobile;
using EEMOCantilanSDS.Application.Dtos.Revenue;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Sync.SyncOfflineCollections;

public sealed class SyncOfflineCollectionsCommandHandler(
    ISender sender,
    ISyncRepository syncRepository,
    ICurrentUserService currentUser,
    WcfCollectionWorkflow? wcfWorkflow = null,
    GovernedServiceWorkflow? governedWorkflow = null,
    CollectionComposerWorkflow? ecfWorkflow = null,
    FeeScheduleCollectionWorkflow? feeScheduleWorkflow = null)
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
            if (op.Kind == OfflineOperationKind.WcfCollection)
            {
                if (wcfWorkflow is null)
                {
                    results.Add(new SyncOperationResultDto(op.ClientOperationId, SyncResultStatus.Failed,
                        "WCF canonical sync is not configured."));
                    continue;
                }
                var outcome = await wcfWorkflow.PostMobileAsync(new WcfCollectionPostRequest(
                    op.PayloadVersion, op.ClientOperationId, op.BusinessDate, op.UtilityBillId ?? Guid.Empty,
                    op.ReceivedAmount ?? 0m, op.WaterSourceVersion ?? 0, op.AccountableDocumentId,
                    op.DocumentNumber, op.IssuedAtUtc,
                    StallId: op.UtilityBillId is null ? op.StallId : null, BillingYear: op.BillingYear, BillingMonth: op.BillingMonth), ct);
                var wcfStatus = outcome.IsSuccess ? SyncResultStatus.Synced
                    : outcome.Error?.StartsWith("RECONCILIATION_REQUIRED:", StringComparison.Ordinal) == true
                        ? SyncResultStatus.ReconciliationRequired
                        : IsTransient(outcome.StatusCode) ? SyncResultStatus.Failed : SyncResultStatus.Rejected;
                results.Add(new SyncOperationResultDto(op.ClientOperationId, wcfStatus,
                    outcome.IsSuccess ? null : outcome.Error,
                    outcome.Value?.ReferenceCode, outcome.Value?.CollectionId));
                continue;
            }
            if (op.Kind == OfflineOperationKind.FeeScheduleCollection)
            {
                if (feeScheduleWorkflow is null)
                {
                    results.Add(new SyncOperationResultDto(op.ClientOperationId, SyncResultStatus.Failed,
                        "Canonical Tabo/Slaughterhouse sync is not configured."));
                    continue;
                }
                var fee = await feeScheduleWorkflow.PostMobileAsync(new FeeSchedulePostRequest(
                    op.ClientOperationId, op.OperationCode ?? string.Empty, op.BusinessDate, op.ReceivedAmount ?? 0m,
                    VendorName: op.VendorName, Goods: op.Goods, Animal: op.AnimalType, CustomAnimalName: op.CustomAnimalType,
                    Heads: op.NumberOfHeads, OwnerName: op.OwnerName), ct);
                var feeStatus = fee.IsSuccess ? SyncResultStatus.Synced
                    : IsTransient(fee.StatusCode) ? SyncResultStatus.Failed : SyncResultStatus.Rejected;
                results.Add(new SyncOperationResultDto(op.ClientOperationId, feeStatus,
                    fee.IsSuccess ? null : fee.Error, fee.Value?.ReferenceCode, fee.Value?.CollectionId));
                continue;
            }
            if (op.Kind == OfflineOperationKind.ObligationCollection)
            {
                if (ecfWorkflow is null)
                {
                    results.Add(new SyncOperationResultDto(op.ClientOperationId, SyncResultStatus.Failed,
                        "Vendor fee canonical sync is not configured."));
                    continue;
                }
                var vendorFee = await ecfWorkflow.PostMobileObligationAsync(new MobileObligationPostRequest(
                    op.ClientOperationId, op.ObligationAccountId ?? Guid.Empty, op.BillingYear ?? op.BusinessDate.Year,
                    op.BillingMonth ?? op.BusinessDate.Month, op.ReceivedAmount ?? 0m, op.BusinessDate), ct);
                var vendorStatus = vendorFee.IsSuccess ? SyncResultStatus.Synced
                    : IsTransient(vendorFee.StatusCode) ? SyncResultStatus.Failed : SyncResultStatus.Rejected;
                results.Add(new SyncOperationResultDto(op.ClientOperationId, vendorStatus,
                    vendorFee.IsSuccess ? null : vendorFee.Error, vendorFee.Value?.ReferenceCode, vendorFee.Value?.CollectionId));
                continue;
            }
            if (op.Kind == OfflineOperationKind.RentCollection)
            {
                if (ecfWorkflow is null)
                {
                    results.Add(new SyncOperationResultDto(op.ClientOperationId, SyncResultStatus.Failed,
                        "Rent canonical sync is not configured."));
                    continue;
                }
                var rent = await ecfWorkflow.PostMobileRentAsync(new MobileRentPostRequest(
                    op.ClientOperationId, op.StallId ?? Guid.Empty, op.BillingYear ?? op.BusinessDate.Year,
                    op.BillingMonth ?? op.BusinessDate.Month, op.ReceivedAmount ?? 0m, op.RentSourceVersion ?? 0, op.BusinessDate), ct);
                var rentStatus = rent.IsSuccess ? SyncResultStatus.Synced
                    : IsTransient(rent.StatusCode) ? SyncResultStatus.Failed : SyncResultStatus.Rejected;
                results.Add(new SyncOperationResultDto(op.ClientOperationId, rentStatus,
                    rent.IsSuccess ? null : rent.Error, rent.Value?.ReferenceCode, rent.Value?.CollectionId));
                continue;
            }
            if (op.Kind == OfflineOperationKind.EcfCollection)
            {
                if (ecfWorkflow is null)
                {
                    results.Add(new SyncOperationResultDto(op.ClientOperationId, SyncResultStatus.Failed,
                        "ECF canonical sync is not configured."));
                    continue;
                }
                var ecf = await ecfWorkflow.PostMobileEcfAsync(new MobileEcfPostRequest(
                    op.ClientOperationId, op.UtilityBillId ?? Guid.Empty, op.ReceivedAmount ?? 0m,
                    op.ElectricitySourceVersion ?? 0, op.BusinessDate), ct);
                var ecfStatus = ecf.IsSuccess ? SyncResultStatus.Synced
                    : IsTransient(ecf.StatusCode) ? SyncResultStatus.Failed : SyncResultStatus.Rejected;
                results.Add(new SyncOperationResultDto(op.ClientOperationId, ecfStatus,
                    ecf.IsSuccess ? null : ecf.Error, ecf.Value?.ReferenceCode, ecf.Value?.CollectionId));
                continue;
            }
            if (op.Kind == OfflineOperationKind.GovernedService)
            {
                if (governedWorkflow is null)
                {
                    results.Add(new SyncOperationResultDto(op.ClientOperationId, SyncResultStatus.Failed,
                        "Governed-service canonical sync is not configured."));
                    continue;
                }
                var governed = await governedWorkflow.PostMobileAsync(new GovernedServicePostRequest(
                    op.PayloadVersion, op.ClientOperationId, op.OperationCode ?? string.Empty, op.BusinessDate,
                    op.ReceivedAmount ?? 0m, op.CollectionMode, op.PayerName, op.Reference,
                    op.AccountableDocumentId, op.DocumentNumber, op.IssuedAtUtc, op.VehicleClassCode, op.FeeOptionId), ct);
                var governedStatus = governed.IsSuccess ? SyncResultStatus.Synced
                    : governed.Error?.StartsWith("RECONCILIATION_REQUIRED:", StringComparison.Ordinal) == true
                        ? SyncResultStatus.ReconciliationRequired
                        : IsTransient(governed.StatusCode) ? SyncResultStatus.Failed : SyncResultStatus.Rejected;
                results.Add(new SyncOperationResultDto(op.ClientOperationId, governedStatus,
                    governed.IsSuccess ? null : governed.Error,
                    governed.Value?.ReferenceCode, governed.Value?.CollectionId));
                continue;
            }
            // Idempotent: a record already carrying this client operation id means it was synced.
            if (await syncRepository.IsOperationProcessedAsync(op.ClientOperationId, ct))
            {
                results.Add(new SyncOperationResultDto(op.ClientOperationId, SyncResultStatus.Synced, "Already synced."));
                continue;
            }

            var (ok, statusCode, message) = await DispatchAsync(op, ct);
            var status = ok
                ? SyncResultStatus.Synced
                : message?.StartsWith("RECONCILIATION_REQUIRED:", StringComparison.Ordinal) == true
                    ? SyncResultStatus.ReconciliationRequired
                    : IsTransient(statusCode) ? SyncResultStatus.Failed : SyncResultStatus.Rejected;

            results.Add(new SyncOperationResultDto(op.ClientOperationId, status, ok ? null : message));
        }

        var dto = new SyncOfflineCollectionsResultDto(
            results.Count(r => r.Status == SyncResultStatus.Synced),
            results.Count(r => r.Status == SyncResultStatus.Rejected),
            results.Count(r => r.Status == SyncResultStatus.Failed),
            results,
            results.Count(r => r.Status == SyncResultStatus.ReconciliationRequired));

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
                        op.FishKilos, op.ORNumber, op.ClientOperationId, op.IsAbsent ?? false,
                        MeatKilos: op.MeatKilos), ct);
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
                case OfflineOperationKind.NpmUtility:
                {
                    var preserveWaterSource = false;
                    if (wcfWorkflow is not null)
                    {
                        var resolution = await wcfWorkflow.ReconcileLegacyMobileWaterAsync(op, ct);
                        if (!resolution.IsSuccess)
                            return (false, resolution.StatusCode, resolution.Error);
                        if (resolution.Value?.RequiresReconciliation == true)
                            return (false, 409, "RECONCILIATION_REQUIRED: " + resolution.Value.Message);
                        preserveWaterSource = resolution.Value?.PreserveWaterSource == true;
                    }
                    var r = await sender.Send(new EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityPayment.RecordUtilityPaymentCommand(
                        op.UtilityBillId ?? Guid.Empty,
                        op.ElecStatus ?? PaymentStatus.Unpaid, op.ElecPartialAmount,
                        op.WaterStatus ?? PaymentStatus.Unpaid, op.WaterPartialAmount,
                        op.ElecORNumber, op.WaterORNumber, op.Remarks, op.ClientOperationId,
                        preserveWaterSource), ct);
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
