using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Domain.Common;
using EEMOCantilanSDS.Domain.Enums;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.IssueOrNumber;

public class IssueOnlinePaymentOrNumberCommandHandler(
    IOnlinePaymentRepository onlinePaymentRepository,
    IPaymentRepository paymentRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork) : IRequestHandler<IssueOnlinePaymentOrNumberCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(IssueOnlinePaymentOrNumberCommand request, CancellationToken cancellationToken)
    {
        var transaction = await onlinePaymentRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction is null)
            return Result<bool>.NotFound();

        if (transaction.Status != OnlinePaymentStatus.Paid)
            return Result<bool>.Failure("Only an online payment awaiting OR can be receipted.", 409);

        var record = await paymentRepository.GetByIdAsync(transaction.PaymentRecordId, cancellationToken);
        if (record is null)
            return Result<bool>.Failure("Linked payment record not found.", 500);

        var actor = currentUser.Username ?? "Admin";

        // Mirror the OR onto the ledger record (so it surfaces in normal reports) and complete the
        // online transaction. The OR is manual staff input — never auto-generated.
        record.SetOrNumber(request.ORNumber, actor);
        transaction.CompleteWithOr(request.ORNumber, actor);

        await paymentRepository.UpdateAsync(record, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }
}
