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

        // Corrections are optional, but a stated figure must be a usable one: a renewal cannot record a
        // negative rent or a negative area.
        RuleFor(x => x.MonthlyRate!.Value)
            .GreaterThanOrEqualTo(0m).WithMessage("Monthly rental cannot be negative.")
            .When(x => x.MonthlyRate.HasValue);
        RuleFor(x => x.AreaSqm!.Value)
            .GreaterThanOrEqualTo(0d).WithMessage("Area cannot be negative.")
            .When(x => x.AreaSqm.HasValue);
    }
}
