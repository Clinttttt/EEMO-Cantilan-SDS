using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Onboarding.SetAdminPasswordByToken
{
    public class SetAdminPasswordByTokenCommandValidator : AbstractValidator<SetAdminPasswordByTokenCommand>
    {
        public SetAdminPasswordByTokenCommandValidator()
        {
            RuleFor(x => x.Token).NotEmpty();

            RuleFor(x => x.NewPassword)
                .NotEmpty().WithMessage("A password is required.")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
                .Matches("[A-Za-z]").WithMessage("Password must contain a letter.")
                .Matches("[0-9]").WithMessage("Password must contain a digit.");
        }
    }
}
