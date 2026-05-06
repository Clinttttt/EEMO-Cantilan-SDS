using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetTpmOverview;

public class GetTpmOverviewQueryValidator : AbstractValidator<GetTpmOverviewQuery>
{
    public GetTpmOverviewQueryValidator()
    {
        RuleFor(x => x.Year).GreaterThan(2000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
