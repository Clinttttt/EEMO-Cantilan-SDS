using FluentValidation;

namespace EEMOCantilanSDS.Application.Queries.Slaughterhouse.GetClientProfile;

public class GetClientProfileQueryValidator : AbstractValidator<GetClientProfileQuery>
{
    public GetClientProfileQueryValidator()
    {
        RuleFor(x => x.OwnerName)
            .NotEmpty()
            .MaximumLength(100);
    }
}
