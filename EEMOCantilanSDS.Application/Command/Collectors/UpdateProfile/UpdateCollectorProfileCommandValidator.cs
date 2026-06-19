using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Collectors.UpdateProfile;

public sealed class UpdateCollectorProfileCommandValidator : AbstractValidator<UpdateCollectorProfileCommand>
{
    public UpdateCollectorProfileCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address.")
            .MaximumLength(150);

        RuleFor(x => x.ContactNumber)
            .MaximumLength(20);
    }
}
