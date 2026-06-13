using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Payors.GenerateStallActivationCode;

public class GenerateStallActivationCodeCommandValidator : AbstractValidator<GenerateStallActivationCodeCommand>
{
    public GenerateStallActivationCodeCommandValidator()
    {
        RuleFor(x => x.StallId)
            .NotEmpty().WithMessage("A stall is required.");

        RuleFor(x => x.ContactNumber)
            .NotEmpty().WithMessage("The payor's contact number is required.")
            .MaximumLength(20).WithMessage("Contact number is too long.");

        When(x => x.ValidityDays.HasValue, () =>
        {
            RuleFor(x => x.ValidityDays!.Value)
                .InclusiveBetween(1, 90).WithMessage("Validity must be between 1 and 90 days.");
        });
    }
}
