using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Admins.ResetAdminPassword;

public class ResetAdminPasswordCommandValidator : AbstractValidator<ResetAdminPasswordCommand>
{
    public ResetAdminPasswordCommandValidator()
    {
        RuleFor(x => x.AdminId).NotEmpty();

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters");

        RuleFor(x => x.ConfirmPassword)
            .NotEmpty().WithMessage("Enter your own password to confirm this action");
    }
}
