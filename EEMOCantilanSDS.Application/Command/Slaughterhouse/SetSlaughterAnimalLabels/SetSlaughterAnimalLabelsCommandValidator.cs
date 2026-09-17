using EEMOCantilanSDS.Application.Common.Slaughterhouse;
using EEMOCantilanSDS.Domain.Constants;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Slaughterhouse.SetSlaughterAnimalLabels;

public sealed class SetSlaughterAnimalLabelsCommandValidator : AbstractValidator<SetSlaughterAnimalLabelsCommand>
{
    public SetSlaughterAnimalLabelsCommandValidator(ISlaughterAnimalLabelProvider animalNameProvider)
    {
        RuleFor(x => x.Hog).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Carabao).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Cow).NotEmpty().MaximumLength(100);
        RuleFor(x => x)
            .Must(x => SlaughterAnimalNames.BuiltInLabelsAreUnambiguous(x.Hog, x.Carabao, x.Cow))
            .WithMessage("Hog, Carabao, and Cow office labels must be distinct and cannot use another built-in animal's name.");

        RuleFor(x => x)
            .MustAsync(async (command, ct) =>
            {
                var customNames = await animalNameProvider.GetCustomNamesAsync(ct);
                return !new[] { command.Hog, command.Carabao, command.Cow }
                    .Any(label => SlaughterAnimalNames.CollidesWithAny(label, customNames));
            })
            .WithMessage("A built-in animal label cannot duplicate an existing custom animal name.");
    }
}
