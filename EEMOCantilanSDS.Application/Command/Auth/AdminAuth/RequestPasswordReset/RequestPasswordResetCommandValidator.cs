using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.RequestPasswordReset
{
    public class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand>
    {
        public RequestPasswordResetCommandValidator()
        {
            // Format-only checks. Whether the address belongs to an account is NEVER validated here — that
            // would leak which addresses exist; the handler answers uniformly either way.
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Enter the email address on your account.")
                .EmailAddress().WithMessage("Enter a valid email address.")
                .MaximumLength(200);
        }
    }
}
