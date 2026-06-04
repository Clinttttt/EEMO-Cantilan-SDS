using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Auth.CollectorAuth.Login;

public class CollectorLoginCommandValidator : AbstractValidator<CollectorLoginCommand>
{
    public CollectorLoginCommandValidator()
    {
        RuleFor(x => x.UsernameOrEmployeeId)
            .NotEmpty().WithMessage("Username or employee ID is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}
