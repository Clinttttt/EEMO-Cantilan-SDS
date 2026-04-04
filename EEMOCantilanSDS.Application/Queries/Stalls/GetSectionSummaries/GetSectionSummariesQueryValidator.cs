using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetSectionSummaries;

public class GetSectionSummariesQueryValidator : AbstractValidator<GetSectionSummariesQuery>
{
    public GetSectionSummariesQueryValidator()
    {
        RuleFor(x => x.FacilityCode).IsInEnum();
        RuleFor(x => x.Year).GreaterThan(2000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
