using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ChangeMyPassword;

/// <summary>
/// The same password policy as the activation and emailed-reset flows. Stated here too rather than assumed: a required
/// change is the one moment the office is guaranteed to choose a new password, so it is the last place that should accept a
/// weaker one than the others.
/// </summary>
public class ChangeMyPasswordCommandValidator : AbstractValidator<ChangeMyPasswordCommand>
{
    public ChangeMyPasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Enter your current password.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("A password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches("[A-Za-z]").WithMessage("Password must contain a letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}
