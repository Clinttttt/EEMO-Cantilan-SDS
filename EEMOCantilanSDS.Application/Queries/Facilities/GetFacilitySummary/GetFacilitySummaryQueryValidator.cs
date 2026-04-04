using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilitySummary;

public class GetFacilitySummaryQueryValidator : AbstractValidator<GetFacilitySummaryQuery>
{
    public GetFacilitySummaryQueryValidator()
    {
        RuleFor(x => x.FacilityCode).IsInEnum();
        RuleFor(x => x.Year).GreaterThan(2000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
