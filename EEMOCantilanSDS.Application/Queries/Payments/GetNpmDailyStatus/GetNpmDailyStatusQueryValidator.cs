using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Payments.GetNpmDailyStatus;

public class GetNpmDailyStatusQueryValidator : AbstractValidator<GetNpmDailyStatusQuery>
{
    public GetNpmDailyStatusQueryValidator()
    {
        RuleFor(x => x.FacilityCode).IsInEnum();
        RuleFor(x => x.Year).GreaterThan(2000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
