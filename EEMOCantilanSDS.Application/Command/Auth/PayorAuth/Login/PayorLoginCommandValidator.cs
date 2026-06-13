using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Auth.PayorAuth.Login;

public class PayorLoginCommandValidator : AbstractValidator<PayorLoginCommand>
{
    public PayorLoginCommandValidator()
    {
        RuleFor(x => x.ContactNumber)
            .NotEmpty().WithMessage("Contact number is required.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.");
    }
}
