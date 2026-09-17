using EEMOCantilanSDS.Domain.Enums;
using EEMOCantilanSDS.Domain.Constants;
using EEMOCantilanSDS.Application.Common.Slaughterhouse;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.UpdateSlaughter;

public class UpdateSlaughterCommandValidator : AbstractValidator<UpdateSlaughterCommand>
{
    public UpdateSlaughterCommandValidator(ISlaughterAnimalLabelProvider? animalLabelProvider = null)
    {
        RuleFor(x => x.OwnerName)
            .NotEmpty().WithMessage("Owner name is required.")
            .MaximumLength(100);

        RuleFor(x => x.TransactionDate)
            .NotEmpty().WithMessage("Transaction date is required.");

        RuleFor(x => x.ORNumber)
            .NotEmpty().WithMessage("OR number is required.")
            .MaximumLength(50);

        RuleFor(x => x.Animals)
            .NotEmpty().WithMessage("At least one animal type is required.")
            .Must(animals => animals.Sum(a => a.NumberOfHeads) > 0)
            .WithMessage("Total number of heads must be greater than 0.");

        RuleForEach(x => x.Animals).ChildRules(animal =>
        {
            animal.RuleFor(a => a.AnimalType)
                .IsInEnum().WithMessage("Invalid animal type.");

            animal.RuleFor(a => a.CustomAnimalType)
                .NotEmpty().WithMessage("Custom animal type is required.")
                .MaximumLength(100)
                .When(a => a.AnimalType == AnimalType.Other);

            animal.RuleFor(a => a.CustomAnimalType)
                .MustAsync(async (name, ct) =>
                {
                    if (SlaughterAnimalNames.IsCanonicalNameOrAlias(name)) return false;
                    if (animalLabelProvider is null) return true;
                    var labels = await animalLabelProvider.GetAsync(ct);
                    if (SlaughterAnimalNames.CollidesWithAny(name, [labels.Hog, labels.Carabao, labels.Cow]))
                        return false;
                    var customNames = await animalLabelProvider.GetCustomNamesAsync(ct);
                    return SlaughterAnimalNames.UsesEstablishedSpelling(name, customNames);
                })
                .WithMessage("Custom animal type cannot duplicate a built-in name or use different casing from an existing custom animal.")
                .When(a => a.AnimalType == AnimalType.Other && !string.IsNullOrWhiteSpace(a.CustomAnimalType));

            animal.RuleFor(a => a.CustomRate)
                .NotNull().WithMessage("Custom rate is required.")
                .GreaterThan(0).WithMessage("Custom rate must be greater than 0.")
                .When(a => a.AnimalType == AnimalType.Other);

            animal.RuleFor(a => a.NumberOfHeads)
                .GreaterThanOrEqualTo(0).WithMessage("Number of heads cannot be negative.");
        });

        RuleFor(x => x.Animals)
            .Must(animals => !SlaughterAnimalNames.HasDuplicates(animals
                .Where(a => a.AnimalType == AnimalType.Other)
                .Select(a => a.CustomAnimalType)))
            .WithMessage("A custom animal name may be listed only once.");
    }
}
