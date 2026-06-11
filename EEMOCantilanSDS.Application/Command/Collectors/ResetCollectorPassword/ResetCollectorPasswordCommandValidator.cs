using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Collectors.ResetCollectorPassword;

public class ResetCollectorPasswordCommandValidator : AbstractValidator<ResetCollectorPasswordCommand>
{
    public ResetCollectorPasswordCommandValidator()
    {
        RuleFor(x => x.CollectorId).NotEmpty();

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters");
    }
}
