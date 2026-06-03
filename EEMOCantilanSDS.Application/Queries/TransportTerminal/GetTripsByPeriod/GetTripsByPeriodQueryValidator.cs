using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.TransportTerminal.GetTripsByPeriod;

public class GetTripsByPeriodQueryValidator : AbstractValidator<GetTripsByPeriodQuery>
{
    public GetTripsByPeriodQueryValidator()
    {
        RuleFor(x => x.Year).GreaterThan(2000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
