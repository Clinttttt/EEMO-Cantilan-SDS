using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Auth.CreateFirstConsoleAdmin
{
    public class CreateFirstConsoleAdminCommandValidator : AbstractValidator<CreateFirstConsoleAdminCommand>
    {
        public CreateFirstConsoleAdminCommandValidator()
        {
            RuleFor(x => x.FullName).NotEmpty().MaximumLength(120);
            RuleFor(x => x.Username)
                .NotEmpty().MaximumLength(60)
                .Matches("^[^\\s]+$").WithMessage("Username cannot contain spaces.");
            RuleFor(x => x.Email)
                .NotEmpty().EmailAddress().WithMessage("A valid email is required.")
                .MaximumLength(160);
            RuleFor(x => x.Password)
                .NotEmpty()
                .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
                .Matches("[A-Za-z]").WithMessage("Password must contain a letter.")
                .Matches("[0-9]").WithMessage("Password must contain a digit.");
        }
    }
}
