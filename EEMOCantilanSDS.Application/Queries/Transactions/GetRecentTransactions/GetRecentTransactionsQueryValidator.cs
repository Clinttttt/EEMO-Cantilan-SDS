using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Transactions.GetRecentTransactions;

public class GetRecentTransactionsQueryValidator : AbstractValidator<GetRecentTransactionsQuery>
{
    public GetRecentTransactionsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 500).WithMessage("Limit must be between 1 and 500.");
    }
}
