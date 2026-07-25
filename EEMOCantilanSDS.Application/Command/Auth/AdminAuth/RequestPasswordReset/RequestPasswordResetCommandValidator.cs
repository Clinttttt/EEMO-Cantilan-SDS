using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.RequestPasswordReset
{
    public class RequestPasswordResetCommandValidator : AbstractValidator<RequestPasswordResetCommand>
    {
        public RequestPasswordResetCommandValidator()
        {
            // Only a presence/length check: any further validation (or a "no such account" message) would
            // leak whether an identifier exists. The handler answers uniformly regardless.
            RuleFor(x => x.UsernameOrEmail)
                .NotEmpty().WithMessage("Enter your username or email address.")
                .MaximumLength(200);
        }
    }
}
