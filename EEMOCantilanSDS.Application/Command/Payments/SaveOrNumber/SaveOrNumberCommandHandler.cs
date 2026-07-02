using EEMOCantilanSDS.Application.Common.Caching;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Interface.Services;
using EEMOCantilanSDS.Application.Common.Tenancy;
using EEMOCantilanSDS.Domain.Common;
using MediatR;

namespace EEMOCantilanSDS.Application.Command.Payments.SaveOrNumber;

public class SaveOrNumberCommandHandler(
    IPaymentRepository paymentRepository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    IEemoCacheInvalidator cacheInvalidator,
    ITenantContext tenantContext) : IRequestHandler<SaveOrNumberCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(SaveOrNumberCommand request, CancellationToken ct)
    {
        var payment = await paymentRepository.GetByIdAsync(request.PaymentId, ct);
        if (payment == null)
            return Result<bool>.NotFound();

        payment.SetOrNumber(request.ORNumber, currentUser.Username ?? "Admin");
        await paymentRepository.UpdateAsync(payment, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await cacheInvalidator.InvalidatePaymentAffectedViewsAsync(
            tenantContext.TenantCode,
            null,
            payment.BillingYear,
            payment.BillingMonth,
            ct);

        return Result<bool>.Success(true);
    }
}
