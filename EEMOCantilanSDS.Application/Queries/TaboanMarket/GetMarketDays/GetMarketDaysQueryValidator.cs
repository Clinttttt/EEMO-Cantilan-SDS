using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.TaboanMarket.GetMarketDays;

public class GetMarketDaysQueryValidator : AbstractValidator<GetMarketDaysQuery>
{
    public GetMarketDaysQueryValidator()
    {
        RuleFor(x => x.Year).GreaterThan(2000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
