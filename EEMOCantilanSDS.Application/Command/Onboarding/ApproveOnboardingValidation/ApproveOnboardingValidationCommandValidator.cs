using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ApproveOnboardingValidation
{
    public class ApproveOnboardingValidationCommandValidator : AbstractValidator<ApproveOnboardingValidationCommand>
    {
        public ApproveOnboardingValidationCommandValidator()
        {
            RuleFor(x => x.AssessmentRequestId).NotEmpty();
        }
    }
}
