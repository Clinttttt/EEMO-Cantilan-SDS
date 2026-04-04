using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetPaymentRecord;

public class GetPaymentRecordQueryValidator : AbstractValidator<GetPaymentRecordQuery>
{
    public GetPaymentRecordQueryValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.Year).GreaterThan(2000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
