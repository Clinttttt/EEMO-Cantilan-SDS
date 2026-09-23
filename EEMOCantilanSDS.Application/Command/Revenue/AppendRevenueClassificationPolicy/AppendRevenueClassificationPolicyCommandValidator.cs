using EEMOCantilanSDS.Application.Common.Interface.Time;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Revenue.AppendRevenueClassificationPolicy;

public sealed class AppendRevenueClassificationPolicyCommandValidator
    : AbstractValidator<AppendRevenueClassificationPolicyCommand>
{
    public AppendRevenueClassificationPolicyCommandValidator(IClock clock)
    {
        RuleFor(x => x.ClassificationId).NotEmpty();
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
