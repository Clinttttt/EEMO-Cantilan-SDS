using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetMarketClosures;

public class GetNpmMarketClosuresQueryValidator : AbstractValidator<GetNpmMarketClosuresQuery>
{
    public GetNpmMarketClosuresQueryValidator()
    {
        RuleFor(x => x.Year).InclusiveBetween(2000, 3000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
