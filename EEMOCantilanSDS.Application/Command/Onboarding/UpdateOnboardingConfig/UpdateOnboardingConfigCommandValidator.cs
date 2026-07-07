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
        }
    }
}
