using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Onboarding.SubmitAssessmentRequest
{
    public class SubmitAssessmentRequestCommandValidator : AbstractValidator<SubmitAssessmentRequestCommand>
    {
        public SubmitAssessmentRequestCommandValidator()
        {
            RuleFor(x => x.Municipality).NotEmpty().MaximumLength(120);
            RuleFor(x => x.Province).NotEmpty().MaximumLength(120);
            RuleFor(x => x.RequestingOffice).NotEmpty().MaximumLength(160);
            RuleFor(x => x.FocalPerson).NotEmpty().MaximumLength(120);
            RuleFor(x => x.Position).NotEmpty().MaximumLength(120);
            RuleFor(x => x.OfficialEmail)
                .NotEmpty().WithMessage("Official email is required.")
                .EmailAddress().WithMessage("Official email is not valid.")
                .MaximumLength(160);
            RuleFor(x => x.ContactNumber).NotEmpty().MaximumLength(40);
            RuleFor(x => x.FacilitiesManaged)
                .NotEmpty().WithMessage("Please indicate the facilities managed.")
                .MaximumLength(500);
            RuleFor(x => x.ApproxVendors).MaximumLength(60);
            RuleFor(x => x.AuthorizationStatus).MaximumLength(120);
            RuleFor(x => x.Notes).MaximumLength(1000);
            RuleFor(x => x.Acknowledged)
                .Equal(true).WithMessage("You must acknowledge the terms to submit a request.");
        }
    }
}
