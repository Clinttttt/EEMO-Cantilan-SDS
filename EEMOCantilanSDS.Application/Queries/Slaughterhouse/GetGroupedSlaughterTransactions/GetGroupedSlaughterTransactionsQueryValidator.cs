using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetGroupedSlaughterTransactions;

public class GetGroupedSlaughterTransactionsQueryValidator : AbstractValidator<GetGroupedSlaughterTransactionsQuery>
{
    public GetGroupedSlaughterTransactionsQueryValidator()
    {
        RuleFor(x => x.Year).GreaterThan(2000).LessThanOrEqualTo(2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
