using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.ResetPasswordByToken
{
    /// <summary>
    /// Mirrors the activation set-password rules so both password-setting flows enforce the same policy.
    /// </summary>
    public class ResetPasswordByTokenCommandValidator : AbstractValidator<ResetPasswordByTokenCommand>
    {
        public ResetPasswordByTokenCommandValidator()
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
