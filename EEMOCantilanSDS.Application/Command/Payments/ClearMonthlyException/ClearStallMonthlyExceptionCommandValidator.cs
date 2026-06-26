using FluentValidation;

namespace EEMOCantilanSDS.Application.Command.Payments.ClearMonthlyException;

public class ClearStallMonthlyExceptionCommandValidator : AbstractValidator<ClearStallMonthlyExceptionCommand>
{
    public ClearStallMonthlyExceptionCommandValidator()
    {
        RuleFor(x => x.StallId).NotEmpty();
        RuleFor(x => x.Year).InclusiveBetween(2000, 3000);
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
    }
}
