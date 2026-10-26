using EEMOCantilanSDS.Domain.Enums;
using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Stalls.RenewStallContract;

public class RenewStallContractCommandValidator : AbstractValidator<RenewStallContractCommand>
{
    public RenewStallContractCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.EffectivityDate).NotEqual(default(DateOnly)).WithMessage("Effectivity date is required.");

        // A TERM ONLY EXISTS ON A SIGNED CONTRACT. An occupancy renewed as an extension is open-ended by definition — the
        // entity substitutes DomainRules.OpenEndedTermYears for whatever is passed — so requiring a year here would refuse the
        // very case the arrangement exists to record. The same condition the create form already applies.
        RuleFor(x => x.DurationYears)
            .GreaterThan(0).WithMessage("Contract duration must be at least 1 year.")
            .When(x => x.Arrangement == OccupancyArrangement.SignedContract);

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
