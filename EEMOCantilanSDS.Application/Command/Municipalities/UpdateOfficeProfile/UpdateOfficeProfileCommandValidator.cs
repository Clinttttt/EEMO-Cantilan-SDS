using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Municipalities.UpdateOfficeProfile
{
    public class UpdateOfficeProfileCommandValidator : AbstractValidator<UpdateOfficeProfileCommand>
    {
        public UpdateOfficeProfileCommandValidator()
        {
            RuleFor(x => x.OfficeName)
                .NotEmpty().WithMessage("Office name is required.");
        }
    }
}
