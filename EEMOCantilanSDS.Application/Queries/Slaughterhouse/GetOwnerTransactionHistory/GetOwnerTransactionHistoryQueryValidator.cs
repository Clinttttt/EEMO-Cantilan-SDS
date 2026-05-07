using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetOwnerTransactionHistory;

public class GetOwnerTransactionHistoryQueryValidator : AbstractValidator<GetOwnerTransactionHistoryQuery>
{
    public GetOwnerTransactionHistoryQueryValidator()
    {
        RuleFor(x => x.OwnerName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Year).GreaterThan(2000).LessThanOrEqualTo(2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
