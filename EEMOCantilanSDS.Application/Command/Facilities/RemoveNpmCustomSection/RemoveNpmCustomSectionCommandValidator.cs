using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Facilities.RemoveNpmCustomSection;

public class RemoveNpmCustomSectionCommandValidator : AbstractValidator<RemoveNpmCustomSectionCommand>
{
    public RemoveNpmCustomSectionCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Section name is required.");
    }
}
