using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Payors.GetPayorPaymentHistory;

public class GetPayorPaymentHistoryQueryValidator : AbstractValidator<GetPayorPaymentHistoryQuery>
{
    public GetPayorPaymentHistoryQueryValidator()
    {
        RuleFor(x => x.StallId)
            .NotEmpty().WithMessage("Stall id is required.");
    }
}
