using EEMOCantilanSDS.Domain.Constants;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Rates.SetFacilityRate
{
    public class SetFacilityRateCommandValidator : AbstractValidator<SetFacilityRateCommand>
    {
        public SetFacilityRateCommandValidator()
        {
            RuleFor(x => x.Amount)
                .GreaterThanOrEqualTo(0m).WithMessage("Rate amount cannot be negative.");

            // A rate key belongs to one facility's ordinance. The resolver reads a key without regard to the
            // facility it was filed under, so a market's monthly rent saved against the slaughterhouse would
            // quietly become the market's month — refuse the mismatch at the door.
            RuleFor(x => x)
                .Must(c => FacilityRateKeys.For(c.FacilityCode).Contains(c.Key))
                .WithMessage("That rate does not belong to this facility.");
        }
    }
}
