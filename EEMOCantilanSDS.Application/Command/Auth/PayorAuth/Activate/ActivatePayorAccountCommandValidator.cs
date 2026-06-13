using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Activate;

public class ActivatePayorAccountCommandValidator : AbstractValidator<ActivatePayorAccountCommand>
{
    public ActivatePayorAccountCommandValidator()
    {
        RuleFor(x => x.ActivationCode)
            .NotEmpty().WithMessage("Activation code is required.");

        RuleFor(x => x.ContactNumber)
            .NotEmpty().WithMessage("Contact number is required.")
            .MaximumLength(20).WithMessage("Contact number is too long.");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Full name is required.")
            .MaximumLength(100).WithMessage("Full name is too long.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.");
    }
}
