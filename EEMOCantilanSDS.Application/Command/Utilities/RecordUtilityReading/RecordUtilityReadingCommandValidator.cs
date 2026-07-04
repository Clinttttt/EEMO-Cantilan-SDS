using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Utilities.RecordUtilityReading;

public class RecordUtilityReadingCommandValidator : AbstractValidator<RecordUtilityReadingCommand>
{
    private const decimal MaxReading = 100_000_000m;
    private const decimal MaxRate = 10_000m;

    public RecordUtilityReadingCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.BillingMonth).InclusiveBetween(1, 12);
        RuleFor(x => x.BillingYear).InclusiveBetween(2000, 2100);

        RuleFor(x => x.ElecPreviousReading).GreaterThanOrEqualTo(0).LessThanOrEqualTo(MaxReading);
        RuleFor(x => x.ElecCurrentReading).GreaterThanOrEqualTo(0).LessThanOrEqualTo(MaxReading);
        RuleFor(x => x.ElecCurrentReading)
            .GreaterThanOrEqualTo(x => x.ElecPreviousReading)
            .WithMessage("Electricity current reading cannot be less than the previous reading.");
        RuleFor(x => x.ElecRatePerKwh).GreaterThanOrEqualTo(0).LessThanOrEqualTo(MaxRate);

        RuleFor(x => x.WaterPreviousReading).GreaterThanOrEqualTo(0).LessThanOrEqualTo(MaxReading);
        RuleFor(x => x.WaterCurrentReading).GreaterThanOrEqualTo(0).LessThanOrEqualTo(MaxReading);
        RuleFor(x => x.WaterCurrentReading)
            .GreaterThanOrEqualTo(x => x.WaterPreviousReading)
            .WithMessage("Water current reading cannot be less than the previous reading.");
        RuleFor(x => x.WaterRatePerCubicMeter).GreaterThanOrEqualTo(0).LessThanOrEqualTo(MaxRate);

        RuleFor(x => x.Remarks).MaximumLength(500);
    }
}
