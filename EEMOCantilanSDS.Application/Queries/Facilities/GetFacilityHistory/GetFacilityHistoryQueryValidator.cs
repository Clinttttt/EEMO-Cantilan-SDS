using EEMOCantilanSDS.Domain.Common;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityHistory;

public class GetFacilityHistoryQueryValidator : AbstractValidator<GetFacilityHistoryQuery>
{
    public GetFacilityHistoryQueryValidator()
    {
        RuleFor(x => x.FacilityCode)
            .IsInEnum()
            .WithMessage("Invalid facility code");

        RuleFor(x => x.Year)
            .GreaterThan(2000)
            .LessThanOrEqualTo(PhilippineTime.Today.Year + 1)
            .WithMessage("Year must be between 2000 and next year");
    }
}
