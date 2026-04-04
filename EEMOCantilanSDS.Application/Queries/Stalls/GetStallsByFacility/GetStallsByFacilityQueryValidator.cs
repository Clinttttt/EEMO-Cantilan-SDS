using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Stalls.GetStallsByFacility;

public class GetStallsByFacilityQueryValidator : AbstractValidator<GetStallsByFacilityQuery>
{
    public GetStallsByFacilityQueryValidator()
    {
        RuleFor(x => x.FacilityCode).IsInEnum();
    }
}
