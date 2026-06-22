using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.Confirm;

public class ConfirmOnlinePaymentCommandValidator : AbstractValidator<ConfirmOnlinePaymentCommand>
{
    public ConfirmOnlinePaymentCommandValidator()
    {
        RuleFor(x => x.Reference)
            .NotEmpty().WithMessage("A payment reference is required.")
            .MaximumLength(64);
    }
}
