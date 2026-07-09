using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Facilities.AddFacility;

public class AddFacilityCommandValidator : AbstractValidator<AddFacilityCommand>
{
    public AddFacilityCommandValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("A facility type is required.")
            .Must(c => Enum.TryParse<FacilityCode>(c, ignoreCase: true, out _))
            .WithMessage("Unknown facility type.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("A facility name is required.")
            .MaximumLength(120);

        RuleFor(x => x.ShortName)
            .NotEmpty().WithMessage("A short name is required.")
            .MaximumLength(16);

        RuleFor(x => x.Description)
            .MaximumLength(400);
    }
}
