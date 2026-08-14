using EEMOCantilanSDS.Application.Common.Interface.Time;
using EEMOCantilanSDS.Domain.Common;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Facilities.GetFacilityHistory;

public class GetFacilityHistoryQueryValidator : AbstractValidator<GetFacilityHistoryQuery>
{
    public GetFacilityHistoryQueryValidator(IClock clock)
    {
        RuleFor(x => x.FacilityCode)
            .IsInEnum()
            .WithMessage("Invalid facility code");

        RuleFor(x => x.Year)
            .GreaterThan(2000)
            .LessThanOrEqualTo(clock.PhilippineToday.Year + 1)
            .WithMessage("Year must be between 2000 and next year");
    }
}
