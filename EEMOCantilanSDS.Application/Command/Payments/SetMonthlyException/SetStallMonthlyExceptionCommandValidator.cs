using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Payments.SetMonthlyException;

public class SetStallMonthlyExceptionCommandValidator : AbstractValidator<SetStallMonthlyExceptionCommand>
{
    public SetStallMonthlyExceptionCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2000, 3000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Remarks).MaximumLength(300);
    }
}
