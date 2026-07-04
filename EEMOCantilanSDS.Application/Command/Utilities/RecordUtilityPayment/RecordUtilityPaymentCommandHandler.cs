using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Application.Dtos.Utilities;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityPayment;

public class RecordUtilityPaymentCommandHandler(
    IUtilityBillRepository utilityRepository,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<RecordUtilityPaymentCommand, Result<UtilityBillDto>>
{
    public async Task<Result<UtilityBillDto>> Handle(RecordUtilityPaymentCommand request, CancellationToken ct)
    {
        var bill = await utilityRepository.GetByIdAsync(request.BillId, ct);
        if (bill is null)
            return Result<UtilityBillDto>.NotFound();

        // A collector may only collect for a facility they are assigned to (NPM). Admins are unrestricted.
        if (currentUser.Role == "Collector")
        {
            if (currentUser.CollectorId is not { } actingCollectorId)
                return Result<UtilityBillDto>.Forbidden();

            var collector = await collectorRepository.GetByIdAsync(actingCollectorId, ct);
            if (collector is null || !collector.FacilityAssignments.Any(a => a.FacilityCode == FacilityCode.NPM))
                return Result<UtilityBillDto>.Forbidden();
        }

        var actor = currentUser.Username ?? "Admin";
        var elecOr = request.ElecORNumber?.Trim();
        var waterOr = request.WaterORNumber?.Trim();

        // OR uniqueness — per utility, excluding this bill so re-marking (or one receipt covering both
        // utilities of this bill) is allowed; reject an OR already used on another bill.
        if (request.ElecStatus != PaymentStatus.Unpaid && !string.IsNullOrWhiteSpace(elecOr)
            && !await utilityRepository.IsORNumberUniqueAsync(elecOr, bill.Id, ct))
            return Result<UtilityBillDto>.Failure("Electricity OR number already exists.", 409);

        if (request.WaterStatus != PaymentStatus.Unpaid && !string.IsNullOrWhiteSpace(waterOr)
            && !await utilityRepository.IsORNumberUniqueAsync(waterOr, bill.Id, ct))
            return Result<UtilityBillDto>.Failure("Water OR number already exists.", 409);

        bill.RecordPayment(
            elecOr, waterOr, currentUser.CollectorId,
            request.ElecStatus, request.ElecPartialAmount,
            request.WaterStatus, request.WaterPartialAmount,
            request.Remarks, actor);

        // Offline replay idempotency: stamp the client operation id so a re-sync is a no-op.
        if (request.ClientOperationId is { } clientOpId && bill.ClientOperationId is null)
            bill.SetClientOperationId(clientOpId);

        await unitOfWork.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode, FacilityCode.NPM, bill.BillingYear, bill.BillingMonth, ct);

        return Result<UtilityBillDto>.Success(UtilityBillDto.From(bill));
    }
}
