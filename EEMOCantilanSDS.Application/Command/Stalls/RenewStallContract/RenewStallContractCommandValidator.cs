using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Stalls.RenewStallContract;

public class RenewStallContractCommandValidator : AbstractValidator<RenewStallContractCommand>
{
    public RenewStallContractCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.EffectivityDate).NotEqual(default(DateOnly)).WithMessage("Effectivity date is required.");
        RuleFor(x => x.DurationYears).GreaterThan(0).WithMessage("Contract duration must be at least 1 year.");
        RuleFor(x => x.ActualOccupant).NotEmpty().WithMessage("Occupant is required.");
    }
}
