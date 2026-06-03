using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilitySummaries;

public class GetFacilitySummariesQueryValidator : AbstractValidator<GetFacilitySummariesQuery>
{
    public GetFacilitySummariesQueryValidator()
    {
        RuleFor(x => x.Year).GreaterThan(2000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
