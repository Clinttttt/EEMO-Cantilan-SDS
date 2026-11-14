using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.ResolveException;

/// <summary>
/// Records the Head's decision and nothing else: no balance, Collection or OR changes here, because the receipt or refund it cites happened through
/// its own path. Applying the money from here as well would count it twice. The audit interceptor keeps the before/after with the actor and time.
/// </summary>
public class ResolveOnlinePaymentExceptionCommandHandler(
    IOnlinePaymentRepository onlinePaymentRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<ResolveOnlinePaymentExceptionCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(ResolveOnlinePaymentExceptionCommand request, CancellationToken cancellationToken)
    {
        var transaction = await onlinePaymentRepository.GetByIdAsync(request.TransactionId, cancellationToken);
        if (transaction is null)
            return Result<bool>.NotFound();

        if (!transaction.IsAwaitingOfficeDecision)
            return Result<bool>.Failure("This payment is not awaiting a decision. Refresh the list.", ResultStatus.Conflict);

        transaction.RecordOfficeDecision(request.Disposition, request.Reference, request.Note,
            currentUser.Username ?? "Head", clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
