using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetSlaughterOverview;

public class GetSlaughterOverviewQueryValidator : AbstractValidator<GetSlaughterOverviewQuery>
{
    public GetSlaughterOverviewQueryValidator()
    {
        RuleFor(x => x.Year)
            .GreaterThan(2000).WithMessage("Year must be greater than 2000.");

        RuleFor(x => x.Month)
            .InclusiveBetween(1, 12).WithMessage("Month must be between 1 and 12.");
    }
}
