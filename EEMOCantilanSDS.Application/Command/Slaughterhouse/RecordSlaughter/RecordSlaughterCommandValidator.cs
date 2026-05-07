using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.RecordSlaughter;

public class RecordSlaughterCommandValidator : AbstractValidator<RecordSlaughterCommand>
{
    public RecordSlaughterCommandValidator()
    {
        RuleFor(x => x.OwnerName)
            .NotEmpty().WithMessage("Owner name is required.")
            .MaximumLength(100);

        RuleFor(x => x.TransactionDate)
            .NotEmpty().WithMessage("Transaction date is required.");

        RuleFor(x => x.ORNumber)
            .NotEmpty().WithMessage("OR number is required.")
            .MaximumLength(50);

        RuleFor(x => x.AnimalType)
            .IsInEnum().WithMessage("Invalid animal type.");

        RuleFor(x => x.CustomAnimalType)
            .NotEmpty().WithMessage("Custom animal type is required.")
            .MaximumLength(100)
            .When(x => x.AnimalType == AnimalType.Other);

        RuleFor(x => x.CustomRate)
            .NotNull().WithMessage("Custom rate is required.")
            .GreaterThan(0).WithMessage("Custom rate must be greater than 0.")
            .When(x => x.AnimalType == AnimalType.Other);

        RuleFor(x => x.NumberOfHeads)
            .GreaterThan(0).WithMessage("Number of heads must be greater than 0.");
    }
}
