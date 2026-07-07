using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Onboarding.DeclineAssessmentRequest
{
    public class DeclineAssessmentRequestCommandValidator : AbstractValidator<DeclineAssessmentRequestCommand>
    {
        public DeclineAssessmentRequestCommandValidator()
        {
            RuleFor(x => x.Id).NotEmpty();
            RuleFor(x => x.DecisionMessage).MaximumLength(2000);
        }
    }
}
