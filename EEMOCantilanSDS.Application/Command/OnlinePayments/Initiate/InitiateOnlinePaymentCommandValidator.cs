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

        // A count of owed days, when the payor is paying only some of them. No month has more days than this, and a
        // count of nought would ask for nothing at all.
        RuleFor(x => x.Days)
            .InclusiveBetween(1, 31).WithMessage("Choose between one and thirty-one days.")
            .When(x => x.Days.HasValue);
    }
}
