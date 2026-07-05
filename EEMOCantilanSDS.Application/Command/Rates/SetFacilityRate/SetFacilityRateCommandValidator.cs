using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Rates.SetFacilityRate
{
    public class SetFacilityRateCommandValidator : AbstractValidator<SetFacilityRateCommand>
    {
        public SetFacilityRateCommandValidator()
        {
            RuleFor(x => x.Amount)
                .GreaterThanOrEqualTo(0m).WithMessage("Rate amount cannot be negative.");
        }
    }
}
