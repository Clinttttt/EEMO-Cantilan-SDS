using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Dashboard.GetDashboardOverview;

public class GetDashboardOverviewQueryValidator : AbstractValidator<GetDashboardOverviewQuery>
{
    public GetDashboardOverviewQueryValidator()
    {
        RuleFor(x => x.Year).GreaterThan(2000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
