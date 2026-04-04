using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.SaveOrNumber;

public class SaveOrNumberCommandHandler(IPaymentRepository paymentRepository, IUnitOfWork unitOfWork) : IRequestHandler<SaveOrNumberCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SaveOrNumberCommand request, CancellationToken ct)
    {
        var payment = await paymentRepository.GetByIdAsync(request.PaymentId, ct);
        if (payment == null)
            return Result<bool>.NotFound();

        payment.RecordPayment(request.ORNumber, Guid.Empty, payment.Status, payment.PartialAmount);
        await paymentRepository.UpdateAsync(payment, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }
}
