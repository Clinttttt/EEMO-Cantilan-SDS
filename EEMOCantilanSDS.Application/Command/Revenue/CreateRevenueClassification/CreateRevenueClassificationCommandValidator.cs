using EEMOCantilanSDS.Application.Common.Interface.Time;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Revenue.CreateRevenueClassification;

public sealed class CreateRevenueClassificationCommandValidator : AbstractValidator<CreateRevenueClassificationCommand>
{
    public CreateRevenueClassificationCommandValidator(IClock clock)
    {
        RuleFor(x => x.SemanticCode)
            .NotEmpty()
            .Matches("^[A-Z][A-Z0-9_]{1,79}$")
            .WithMessage("Use 2-80 uppercase letters, digits, or underscores, starting with a letter.");
        RuleFor(x => x.DisplayName)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage("A display name is required.")
            .MaximumLength(160);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.EffectiveDate)
            .Must(date => date >= clock.PhilippineToday)
            .WithMessage("A revenue policy cannot take effect in the past.");
        RuleFor(x => x.PermittedInstrumentType)
            .Must(type => type is null || Enum.IsDefined(type.Value))
            .WithMessage("Choose a supported accountable instrument or leave it unresolved.");
    }
}
