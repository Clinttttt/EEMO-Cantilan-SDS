using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ReturnOnboardingToDraft
{
    public class ReturnOnboardingToDraftCommandValidator : AbstractValidator<ReturnOnboardingToDraftCommand>
    {
        public ReturnOnboardingToDraftCommandValidator()
        {
            RuleFor(x => x.AssessmentRequestId).NotEmpty();
            RuleFor(x => x.Note).MaximumLength(2000);
        }
    }
}
