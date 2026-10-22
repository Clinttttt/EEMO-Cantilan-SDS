using EEMOCantilanSDS.Application.Common.Onboarding;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Onboarding.UpdateOnboardingConfig
{
    public class UpdateOnboardingConfigCommandValidator : AbstractValidator<UpdateOnboardingConfigCommand>
    {
        public UpdateOnboardingConfigCommandValidator()
        {
            RuleFor(x => x.Token).NotEmpty();
            RuleFor(x => x.ConfigJson)
                .MaximumLength(200_000).WithMessage("Configuration is too large.");

            // The declared revenue operations are the one part of the document the server reads. Everything else stays the
            // workspace's own shape, so this checks only that block: recognised codes, a bounded local name, no repeats.
            RuleFor(x => x.ConfigJson)
                .Custom((json, ctx) =>
                {
                    foreach (var error in OnboardingOperationDeclarations.Validate(json).Concat(OnboardingTerminalSections.Validate(json)))
                        ctx.AddFailure(nameof(UpdateOnboardingConfigCommand.ConfigJson), error);
                });
        }
    }
}
