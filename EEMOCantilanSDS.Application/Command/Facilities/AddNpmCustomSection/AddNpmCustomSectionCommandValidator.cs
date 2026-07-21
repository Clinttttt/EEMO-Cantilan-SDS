using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Facilities.AddNpmCustomSection;

public class AddNpmCustomSectionCommandValidator : AbstractValidator<AddNpmCustomSectionCommand>
{
    public AddNpmCustomSectionCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Section name is required.")
            .MaximumLength(60).WithMessage("Section name cannot exceed 60 characters.");
    }
}
