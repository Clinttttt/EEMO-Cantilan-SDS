using EEMOCantilanSDS.Application.Command.OnlinePayments.ApplyHeld;
using EEMOCantilanSDS.Application.Common.Interface.Persistence;
using EEMOCantilanSDS.Application.Common.Revenue;
using EEMOCantilanSDS.Application.Dtos.Payments;
using EEMOCantilanSDS.Domain.Common;
using FluentValidation;
using MediatR;

namespace EEMOCantilanSDS.Application.Queries.OnlinePayments.GetHeldPaymentPlan;

/// <summary>The months a held online payment would pay if the Head applied it to the space's rent, asked before anything is saved.</summary>
public record GetHeldPaymentPlanQuery(Guid TransactionId) : IRequest<Result<HeldPaymentPlanDto>>;

public class GetHeldPaymentPlanQueryValidator : AbstractValidator<GetHeldPaymentPlanQuery>
{
    public GetHeldPaymentPlanQueryValidator() => RuleFor(x => x.TransactionId).NotEmpty();
}

public class GetHeldPaymentPlanQueryHandler(IOnlinePaymentRepository onlinePayments, IPaymentRepository payments, IStallRepository stalls, BillingRules billing)
    : IRequestHandler<GetHeldPaymentPlanQuery, Result<HeldPaymentPlanDto>>
{
    public async Task<Result<HeldPaymentPlanDto>> Handle(GetHeldPaymentPlanQuery request, CancellationToken ct)
    {
        var planned = await HeldPayment.PlanAsync(await onlinePayments.GetByIdAsync(request.TransactionId, ct), payments, stalls, billing, ct);
        return planned.IsSuccess
            ? Result<HeldPaymentPlanDto>.Success(planned.Value.Plan)
            : Result<HeldPaymentPlanDto>.Failure(planned.Error ?? "", planned.Status);
    }
}
