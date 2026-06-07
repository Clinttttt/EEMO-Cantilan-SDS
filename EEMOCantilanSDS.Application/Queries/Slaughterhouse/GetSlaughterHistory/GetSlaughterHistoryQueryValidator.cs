using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterHistory;

public class GetSlaughterHistoryQueryValidator : AbstractValidator<GetSlaughterHistoryQuery>
{
    public GetSlaughterHistoryQueryValidator()
    {
        RuleFor(x => x.Year)
            .GreaterThan(2000).WithMessage("Year must be greater than 2000.");
    }
}
