using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetMonthlyExceptions;

public class GetStallMonthlyExceptionsQueryValidator : AbstractValidator<GetStallMonthlyExceptionsQuery>
{
    public GetStallMonthlyExceptionsQueryValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2000, 3000);
    }
}
