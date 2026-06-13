using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.OnlinePayments.Initiate;

public class InitiateOnlinePaymentCommandValidator : AbstractValidator<InitiateOnlinePaymentCommand>
{
    public InitiateOnlinePaymentCommandValidator()
    {
        RuleFor(x => x.StallId)
            .NotEmpty().WithMessage("A stall is required.");

        RuleFor(x => x.Year)
            .InclusiveBetween(2000, 2100).WithMessage("Invalid billing year.");

        RuleFor(x => x.Month)
            .InclusiveBetween(1, 12).WithMessage("Invalid billing month.");
    }
}
