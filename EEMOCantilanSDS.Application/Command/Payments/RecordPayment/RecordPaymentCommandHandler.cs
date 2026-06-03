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
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<RecordPaymentCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(RecordPaymentCommand request, CancellationToken ct)
    {
        var stall = await stallRepository.GetByIdAsync(request.StallId, ct);
        if (stall == null)
            return Result<bool>.NotFound();

        var collectorId = currentUser.CollectorId;
        var recordedBy = currentUser.Username ?? "Admin";

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
            await paymentRepository.AddAsync(newPayment, ct);
        }
        else
        {
            var existingPayment = await paymentRepository.GetByIdAsync(existingPaymentDto.Id, ct);
            if (existingPayment == null)
                return Result<bool>.NotFound();
                
            existingPayment.UpdateStatus(request.Status, request.PartialAmount ?? 0m, request.Remarks, recordedBy, collectorId);
            await paymentRepository.UpdateAsync(existingPayment, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
}
