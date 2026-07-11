using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.DailyCollections.SettleNpmMonth;

public class SettleNpmMonthCommandValidator : AbstractValidator<SettleNpmMonthCommand>
{
    public SettleNpmMonthCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2000, 2100);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.ORNumber).MaximumLength(50);
    }
}
