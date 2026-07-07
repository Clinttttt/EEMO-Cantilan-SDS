using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Onboarding.ApproveAssessmentRequest
{
    public class ApproveAssessmentRequestCommandValidator : AbstractValidator<ApproveAssessmentRequestCommand>
    {
        public ApproveAssessmentRequestCommandValidator()
        {
            RuleFor(x => x.Id).NotEmpty();
            RuleFor(x => x.DecisionMessage).MaximumLength(2000);
        }
    }
}
