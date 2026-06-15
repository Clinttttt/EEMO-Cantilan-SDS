using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Entities.Payments;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.RecordPayment;

public class RecordPaymentCommandHandler(
    IPaymentRepository paymentRepository,
    IStallRepository stallRepository,
    ICollectorRepository collectorRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<RecordPaymentCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RecordPaymentCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall == null)
            return Result<bool>.NotFound();

        // Collectors may only record against a facility they are assigned to. Admins/heads
        // (any non-Collector role) record from the web and are not assignment-restricted.
        if (currentUser.Role == "Collector")
        {
            if (currentUser.CollectorId is not { } actingCollectorId || stall.Facility is null)
                return Result<bool>.Forbidden();

            var actingCollector = await collectorRepository.GetByIdAsync(actingCollectorId, ct);
            if (actingCollector is null ||
                !actingCollector.FacilityAssignments.Any(a => a.FacilityCode == stall.Facility.Code))
            {
                return Result<bool>.Forbidden();
            }
        }

        var collectorId = currentUser.CollectorId;
        var recordedBy = currentUser.Username ?? "Admin";
        var orNumber = request.ORNumber?.Trim();

        var existingPaymentDto = await paymentRepository.GetPaymentRecordAsync(
            request.StallId,
            request.Year,
            request.Month,
            ct);
        
        if (existingPaymentDto == null)
        {
            var newPayment = PaymentRecord.Create(
                request.StallId,
                request.Year,
                request.Month,
                stall.MonthlyRate,
                recordedBy
            );
            
            newPayment.UpdateStatus(request.Status, request.PartialAmount ?? 0m, request.Remarks, recordedBy, collectorId);
            if (newPayment.Status != PaymentStatus.Unpaid && !string.IsNullOrWhiteSpace(orNumber))
            {
                if (!await paymentRepository.IsORNumberUniqueAsync(orNumber, ct))
                    return Result<bool>.Failure("OR number already exists.", 409);
                newPayment.SetOrNumber(orNumber, recordedBy);
            }
            await paymentRepository.AddAsync(newPayment, ct);
        }
        else
        {
            var existingPayment = await paymentRepository.GetByIdAsync(existingPaymentDto.Id, ct);
            if (existingPayment == null)
                return Result<bool>.NotFound();
                
            existingPayment.UpdateStatus(request.Status, request.PartialAmount ?? 0m, request.Remarks, recordedBy, collectorId);
            if (existingPayment.Status != PaymentStatus.Unpaid && !string.IsNullOrWhiteSpace(orNumber))
            {
                // Allow re-saving the same OR already on THIS record (e.g. partial -> full re-record);
                // only reject when the OR is being introduced and already exists elsewhere.
                var alreadyOnThisRecord = string.Equals(existingPayment.ORNumber?.Trim(), orNumber, StringComparison.Ordinal);
                if (!alreadyOnThisRecord && !await paymentRepository.IsORNumberUniqueAsync(orNumber, ct))
                    return Result<bool>.Failure("OR number already exists.", 409);
                existingPayment.SetOrNumber(orNumber, recordedBy);
            }
            await paymentRepository.UpdateAsync(existingPayment, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
