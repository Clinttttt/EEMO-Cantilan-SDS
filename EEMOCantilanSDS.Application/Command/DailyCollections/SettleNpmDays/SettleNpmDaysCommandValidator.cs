using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmDays;

public class SettleNpmDaysCommandValidator : AbstractValidator<SettleNpmDaysCommand>
{
    public SettleNpmDaysCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.Dates).NotEmpty().WithMessage("Select at least one day.");
        RuleFor(x => x.ORNumber).MaximumLength(50);
    }
}
