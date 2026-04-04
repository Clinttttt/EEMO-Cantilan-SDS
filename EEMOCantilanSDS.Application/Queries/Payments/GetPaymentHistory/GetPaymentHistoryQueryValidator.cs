using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetPaymentHistory;

public class GetPaymentHistoryQueryValidator : AbstractValidator<GetPaymentHistoryQuery>
{
    public GetPaymentHistoryQueryValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
    }
}
