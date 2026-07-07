using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Onboarding.SubmitOnboarding
{
    public class SubmitOnboardingCommandValidator : AbstractValidator<SubmitOnboardingCommand>
    {
        public SubmitOnboardingCommandValidator()
        {
            RuleFor(x => x.Token).NotEmpty();
        }
    }
}
