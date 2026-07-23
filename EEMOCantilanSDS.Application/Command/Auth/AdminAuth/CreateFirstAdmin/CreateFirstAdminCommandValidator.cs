using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Auth.AdminAuth.CreateFirstAdmin;

public class CreateFirstAdminCommandValidator : AbstractValidator<CreateFirstAdminCommand>
{
    public CreateFirstAdminCommandValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Username)
            .NotEmpty()
            .Length(3, 50)
            .Must(x => !x.Contains(' ')).WithMessage("Username cannot contain spaces");

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches("[A-Za-z]").WithMessage("Password must contain a letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}
